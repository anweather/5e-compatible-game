#!/usr/bin/env bash
# Runs probe scenarios in the open Unity Editor and prints their JSON results
# (docs/FAST_LOOP.md). Exit code 0 when every check passed, 1 when one failed
# or a run was aborted, 2 when the CLI couldn't start it.
#
#   tools/probe.sh smoke
#   tools/probe.sh smoke fire court        # one after another, each in its own Play session: one JSON each
#   tools/probe.sh fire --shots /path/to/scratchpad
#   tools/probe.sh perf --seconds 20
#   tools/probe.sh chatprompts                                     # with a stand-in; spends nothing
#   tools/probe.sh chatprompts --real-claude true --out /scratch   # spends real prompts (J-064)
#   tools/probe.sh sweep                   # every probe (layer 4, docs/TESTING_LAYERS.md)
#   tools/probe.sh sweep --half 1          # every other probe: the PC runs half 1, the Mac half 2
#   tools/probe.sh list
set -uo pipefail

# The CLI is ~/.unity/bin/unity on macOS and `unity` on PATH on Windows.
UNITY="${UNITY:-$(command -v unity || echo "$HOME/.unity/bin/unity")}"
here="$(cd "$(dirname "$0")/.." && pwd)"
export UNITY_NO_BANNER=1 UNITY_NO_PAGER=1 UNITY_NON_INTERACTIVE=1
export UNITY_PROJECT_PATH="${UNITY_PROJECT_PATH:-$here/unity}"

if [ $# -lt 1 ]; then
  echo "usage: tools/probe.sh <scenario>... [--shots <folder>] [--seconds N] | sweep [--half 1|2] | list" >&2
  exit 2
fi
if [ "$1" = list ]; then
  exec "$UNITY" command probe list --result-only
fi

# On Windows, a run pulls the Editor to the front for a few seconds as it
# enters Play mode, which drops someone out of a built game they're playing
# full screen. While the game is the window in front, wait (up to ten minutes,
# in case they've walked away and left it there). PROBE_NO_WAIT=1 skips this.
game_in_front() {
  command -v powershell.exe >/dev/null 2>&1 || return 1
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$here/tools/game-in-front.ps1")" >/dev/null 2>&1
}
wait_for_player_to_leave() {
  if [ "${PROBE_NO_WAIT:-0}" != 1 ] && game_in_front; then
    echo "probe: the game is in front (someone's playing); waiting for them to leave it, up to 10 minutes" >&2
    for _ in $(seq 1 120); do
      sleep 5
      game_in_front || break
    done
  fi
}

# Runs one scenario, or a batch (names joined by commas), and prints its result: each scenario's JSON for a batch,
# then the batch's. Returns 0 when everything passed.
run() {
  local names="$1"; shift
  wait_for_player_to_leave
  local started
  if ! started=$("$UNITY" command probe run --name "$names" "$@" --result-only 2>&1); then
    echo "$started" >&2
    return 2
  fi
  echo "$started" >&2
  # Entering Play mode may reload the domain, which restarts the Editor's command
  # server: retry until it answers, then wait for the run (or the batch) to finish.
  local result
  # A batch's next scenario starts when the Editor next ticks, and one in the background doesn't tick
  # in Edit mode on its own: `probe kick` wakes it between waits.
  for _ in $(seq 1 3600); do
    if result=$("$UNITY" command probe result --wait true --timeout 20 --result-only 2>/dev/null) \
       && echo "$result" | grep -Eq '"state": ?"(done|aborted)"'; then
      if [[ "$names" == *,* ]]; then
        for f in "$here"/unity/Temp/Probe/results/*.json; do cat "$f"; echo; done
      fi
      echo "$result"
      echo "$result" | node -e 'let s="";process.stdin.on("data",d=>s+=d).on("end",()=>{const r=JSON.parse(s);process.exit(r.state==="done"&&r.pass===true?0:1)})'
      return $?
    fi
    [[ "$names" == *,* ]] && "$UNITY" command probe kick --result-only >/dev/null 2>&1
    sleep 2
  done
  echo "probe: no result after waiting; see unity/Temp/Probe/result.json" >&2
  return 1
}

if [ "$1" = sweep ]; then
  shift
  half=0
  while [ $# -gt 0 ]; do
    case "$1" in
      --half) half="$2"; shift 2 ;;
      *) echo "sweep takes --half 1 or --half 2" >&2; exit 2 ;;
    esac
  done
  list=$("$UNITY" command probe list --result-only) || { echo "$list" >&2; exit 2; }
  # Every other probe by name for a half, so each half gets a mix; two-player ones (online) run on their own.
  read -r solo pairs < <(echo "$list" | node -e '
    let s="";process.stdin.on("data",d=>s+=d).on("end",()=>{
      const half=Number(process.argv[1]);
      const all=JSON.parse(s).map(e=>({name:e.name,players:e.players||1})).sort((a,b)=>a.name<b.name?-1:1)
        .filter((e,i)=>half===0||i%2===half-1);
      const one=all.filter(e=>e.players<2).map(e=>e.name), two=all.filter(e=>e.players>=2).map(e=>e.name);
      console.log((one.join(",")||"-")+" "+(two.join(",")||"-"));
    })' "$half")
  status=0
  [ "$solo" != - ] && { run "$solo" || status=1; }
  if [ "$pairs" != - ]; then
    IFS=, read -ra each <<< "$pairs"
    for p in "${each[@]}"; do run "$p" || status=1; done
  fi
  exit $status
fi

names=()
while [ $# -gt 0 ] && [[ "$1" != --* ]]; do names+=("$1"); shift; done
joined=$(IFS=,; echo "${names[*]}")
run "$joined" "$@"
exit $?
