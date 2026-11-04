#!/bin/zsh
# playtest/cheap/run.sh <model> <folder> [minutes] [seed] [budget USD]
# One exploring session: a model plays the core through Playtest.Text (MCP) with no other tool, no
# shell and none of the repo's settings. On this machine's existing sign-in; it never signs in.
# A model stops when it feels done, so the session runs in segments: each a new conversation with
# the time left and the titles noted so far, on a fresh world. That keeps the context small too.
# Leaves commands.jsonl, findings.jsonl, and stream-N.jsonl and result-N.json for each segment.
model=$1; out=${2:A}; minutes=${3:-20}; seed=${4:-20261104}; budget=${5:-10}
here=${0:A:h}; repo=${here:h:h}
mkdir -p "$out"
dll="$repo/core/Playtest.Text/bin/Release/net8.0/Playtest.Text.dll"
[ -f "$dll" ] || { echo "build it: dotnet build core/Playtest.Text -c Release"; exit 2; }
dotnet_bin=$(command -v dotnet || echo "$HOME/.dotnet/dotnet")
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
done
echo "session over: $(( ($(date +%s) - start) / 60 )) min, \$$spent"
