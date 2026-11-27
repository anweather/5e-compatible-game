#!/usr/bin/env bash
# play-entry.sh (docs/design/dev-speed-baseline.md): seconds from asking the open Editor to enter Play mode until the grid is up and 30 frames have drawn, N times.
set -uo pipefail
here="$(cd "$(dirname "$0")/../.." && pwd)"
export UNITY_PROJECT_PATH="${UNITY_PROJECT_PATH:-$here/unity}" UNITY_NO_BANNER=1 UNITY_NON_INTERACTIVE=1
U="$HOME/.unity/bin/unity"
now() { perl -MTime::HiRes=time -e 'printf "%.2f\n", time'; }
ev() { "$U" command script eval --result-only -- --code "$1" 2>/dev/null | jq -r '.result // empty'; }
for i in $(seq 1 "${N:-3}"); do
  "$U" status --until-ready >/dev/null
  t0=$(now)
  ev 'UnityEditor.EditorApplication.EnterPlaymode(); return 1;' >/dev/null
  for k in $(seq 1 600); do
    r=$(ev 'return UnityEngine.Application.isPlaying && ChemGrid.Current != null && UnityEngine.Time.frameCount > 30 ? "up" : "no";')
    [ "$r" = up ] && break
    sleep 0.25
  done
  t1=$(now)
  ev 'UnityEditor.EditorApplication.ExitPlaymode(); return 1;' >/dev/null
  "$U" status --until-ready >/dev/null
  for k in $(seq 1 120); do [ "$(ev 'return UnityEngine.Application.isPlaying ? "p" : "e";')" = e ] && break; sleep 0.5; done
  t2=$(now)
  printf 'play\t%.1f\texit\t%.1f\n' "$(echo "$t1 - $t0" | bc)" "$(echo "$t2 - $t1" | bc)"
done
