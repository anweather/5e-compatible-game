#!/usr/bin/env bash
# Runs one probe scenario in the open Unity Editor and prints its JSON result
# (docs/FAST_LOOP.md). Exit code 0 when every check passed, 1 when one failed
# or the run was aborted, 2 when the CLI couldn't start it.
#
#   tools/probe.sh smoke
#   tools/probe.sh fire --shots /path/to/scratchpad
#   tools/probe.sh perf --seconds 20
#   tools/probe.sh list
set -uo pipefail

# The CLI is ~/.unity/bin/unity on macOS and `unity` on PATH on Windows.
UNITY="${UNITY:-$(command -v unity || echo "$HOME/.unity/bin/unity")}"
here="$(cd "$(dirname "$0")/.." && pwd)"
export UNITY_NO_BANNER=1 UNITY_NO_PAGER=1 UNITY_NON_INTERACTIVE=1
export UNITY_PROJECT_PATH="${UNITY_PROJECT_PATH:-$here/unity}"

if [ $# -lt 1 ]; then
  echo "usage: tools/probe.sh <scenario|list> [--shots <folder>] [--seconds N]" >&2
  exit 2
fi
if [ "$1" = list ]; then
  exec "$UNITY" command probe list --result-only
fi

name="$1"; shift
if ! started=$("$UNITY" command probe run --name "$name" "$@" --result-only 2>&1); then
  echo "$started" >&2
  exit 2
fi
echo "$started" >&2

# Entering Play mode reloads the domain, which restarts the Editor's command
# server: retry until it answers, then wait for the run to finish.
for _ in $(seq 1 60); do
  if result=$("$UNITY" command probe result --wait true --timeout 400 --result-only 2>/dev/null) \
     && echo "$result" | grep -Eq '"state": ?"(done|aborted)"'; then
    echo "$result"
    echo "$result" | node -e 'let s="";process.stdin.on("data",d=>s+=d).on("end",()=>{const r=JSON.parse(s);process.exit(r.state==="done"&&r.pass===true?0:1)})'
    exit $?
  fi
  sleep 2
done
echo "probe: no result after waiting; see unity/Temp/Probe/result.json" >&2
exit 1
