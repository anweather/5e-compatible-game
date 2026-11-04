#!/bin/zsh
# playtest/cheap/run.sh [--account NAME] [--model MODEL] [--sink SINK] [--out FOLDER] [--minutes N] [--seed N] [--budget USD]
# One exploring session: a model plays the core through Playtest.Text (MCP) with no other tool, no
# shell and none of the repo's settings. It uses an existing Claude sign-in and never signs in.
#   --account  whose sign-in pays: a name (account3 is ~/.claude-account3) or a config folder.
#              Default: this shell's. An API key in the environment is ignored.
#   --model    default haiku.
#   --sink     where reproduced findings go when the session ends: none (default, they stay in the
#              folder), folder:<dir> or github:<owner/repo>. See sink.py.
# A model stops when it feels done, so the session runs in segments: each a new conversation with
# the time left and the titles noted so far, on a fresh world. That keeps the context small too.
# Leaves run.json, commands.jsonl, findings.jsonl, sunk.jsonl, and stream-N.jsonl and result-N.json
# for each segment.
self=${0:A}; here=${self:h}; repo=${here:h:h}
usage() { sed -n '2,8p' "$self" | sed 's/^# //'; exit 2; }
zparseopts -D -F -- -account:=o_account -model:=o_model -sink:=o_sink -out:=o_out -minutes:=o_minutes \
  -seed:=o_seed -budget:=o_budget || usage
[ $# -eq 0 ] || usage
account=${o_account[-1]}; model=${o_model[-1]:-haiku}; sink=${o_sink[-1]:-none}; minutes=${o_minutes[-1]:-20}
seed=${o_seed[-1]:-20261104}; budget=${o_budget[-1]:-10}
out=${${o_out[-1]:-$repo/playtest/runs/cheap-$(date +%Y%m%d-%H%M)}:A}

if [ -n "$account" ]; then
  case $account in
    */*) export CLAUDE_CONFIG_DIR=${account:A} ;;
    *) export CLAUDE_CONFIG_DIR=$HOME/.claude-$account ;;
  esac
  [ -d "$CLAUDE_CONFIG_DIR" ] || { echo "no such account: $CLAUDE_CONFIG_DIR"; exit 2; }
fi
unset ANTHROPIC_API_KEY ANTHROPIC_AUTH_TOKEN
claude auth status 2>/dev/null | grep -q '"loggedIn": true' || {
  echo "not signed in (${CLAUDE_CONFIG_DIR:-the default account}): sign in yourself, this never does"; exit 3; }
python3 -I "$here/sink.py" --check "$sink" || exit 2
dll="$repo/core/Playtest.Text/bin/Release/net8.0/Playtest.Text.dll"
[ -f "$dll" ] || { echo "build it: dotnet build core/Playtest.Text -c Release"; exit 2; }
dotnet_bin=$(command -v dotnet || echo "$HOME/.dotnet/dotnet")

mkdir -p "$out"
python3 -I -c "
import json,sys
json.dump(dict(zip(('model','seed','commit'),sys.argv[2:])),open(sys.argv[1],'w'))" \
  "$out/run.json" "$model" "$seed" "$(git -C "$repo" rev-parse --short HEAD)"
start=$(date +%s); end=$(( start + ${minutes%.*} * 60 )); spent=0
cd "$repo"
for segment in {1..40}; do
  left=$(( end - $(date +%s) ))
  [ $left -lt 20 ] && break
  noted=$(python3 -I -c "
import json,sys
try: print('; '.join(json.loads(l)['title'] for l in open(sys.argv[1])))
except OSError: pass" "$out/findings.jsonl")
  cat > "$out/mcp.json" <<JSON
{ "mcpServers": { "play": { "command": "$dotnet_bin", "args": ["$dll", "serve", "--seed", "$seed", "--minutes", "$(( left / 60.0 ))", "--out", "$out"] } } }
JSON
  claude -p "Play now, on a fresh world, until the tools say TIME IS UP: that is $(( left / 60 )) minutes away, so don't stop before it. Try what hasn't been tried. Already noted, so don't report these again: ${noted:-nothing yet}." \
    --model "$model" --system-prompt "$(cat "$here/prompt.md")" \
    --mcp-config "$out/mcp.json" --strict-mcp-config --tools "" --allowedTools "mcp__play" \
    --permission-mode dontAsk --setting-sources "" --disable-slash-commands --no-session-persistence \
    --max-budget-usd "$(( budget - spent ))" --output-format stream-json --verbose \
    < /dev/null > "$out/stream-$segment.jsonl" 2>> "$out/stderr.txt" &
  pid=$!
  while kill -0 $pid 2>/dev/null; do
    [ $(date +%s) -ge $(( end + 180 )) ] && { echo "over time: stopping segment $segment"; kill $pid; break; }
    sleep 5
  done
  wait $pid 2>/dev/null
  tail -1 "$out/stream-$segment.jsonl" > "$out/result-$segment.json"
  spent=$(python3 -I -c "
import json,glob,sys
t=0.0
for f in glob.glob(sys.argv[1]+'/result-*.json'):
    try: t+=json.load(open(f)).get('total_cost_usd') or 0
    except ValueError: pass
print(round(t,4))" "$out")
  echo "segment $segment done at $(date +%H:%M:%S), spent \$$spent"
  [ $(( spent >= budget )) = 1 ] && { echo "budget reached"; break; }
  failed=$(python3 -I -c "
import json,sys
try: r=json.load(open(sys.argv[1]))
except ValueError: r={'is_error':True,'result':'no result'}
if r.get('is_error'): print(r.get('result') or r.get('subtype'))" "$out/result-$segment.json")
  [ -n "$failed" ] && { echo "segment $segment failed, stopping: $failed"; break; }
done
echo "session over: $(( ($(date +%s) - start) / 60 )) min, \$$spent"
python3 -I "$here/sink.py" "$out" "$sink"
