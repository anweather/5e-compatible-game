#!/usr/bin/env bash
# unity-loop.sh: the Editor's inner loop, timed. For each file, adds a trailing newline (a change Unity must compile),
# refreshes the open Editor and times the wait until it has compiled and reloaded, then takes the newline away and times
# that too; then the same with a new public type (an API change, which rebuilds the assemblies that use this one),
# so the tree ends as it started. Prints one TSV row per edit: the file, the seconds from save to a ready
# Editor, the assemblies rebuilt (Library/ScriptAssemblies/*.dll newer than the edit) and the Editor log's domain reload
# milliseconds. Needs the open Editor on this checkout, idle (no Play mode, nobody else driving it). Compare runs only
# between freshly started Editors: one open for hours reloads slower and slower (3.5 s to 7 s seen), which swamps the
# difference being measured.
#   tools/telemetry/unity-loop.sh                 # the default set: one file per kind of feature
#   tools/telemetry/unity-loop.sh <file>...       # paths relative to the repo
#   ROUNDS=3 tools/telemetry/unity-loop.sh        # each file edited and restored ROUNDS times (default 1)
set -uo pipefail
here="${REPO:-$(cd "$(dirname "$0")/../.." && pwd)}"
export UNITY_PROJECT_PATH="${UNITY_PROJECT_PATH:-$here/unity}" UNITY_NO_BANNER=1 UNITY_NON_INTERACTIVE=1
U="${UNITY:-$(command -v unity || echo "$HOME/.unity/bin/unity")}"
log="${EDITOR_LOG:-$HOME/Library/Logs/Unity/Editor.log}"
[ -f "$log" ] || log="${LOCALAPPDATA:-}/Unity/Editor/Editor.log"
rounds="${ROUNDS:-1}"
S=unity/Assets/Prototype/Scripts
files=("$@")
if [ ${#files[@]} -eq 0 ]; then
  files=(
    "$S/World/ChemGrid.cs"
    "$S/Modes/HoldShow.cs"
    "$S/Player/SpellCaster.cs"
    "$S/Fauna/ValleyFauna.cs"
    "$S/Shell/ControlsMenu.cs"
    "$S/Chat/GameChat.cs"
    "$S/Probes/ProbeRoomProgress.cs"
    "$S/Plans/TreeBudget.cs"
  )
fi

now() { perl -MTime::HiRes=time -e 'printf "%.2f\n", time'; }
compiling() { "$U" command console status --result-only 2>/dev/null | grep -q '"compiling": *true'; }

wait_ready() {
  local t
  for t in $(seq 1 600); do
    "$U" status --until-ready >/dev/null 2>&1
    compiling || { sleep 1; "$U" status --until-ready >/dev/null 2>&1; compiling || return 0; }
    sleep 0.5
  done
  return 1
}

one() {
  local file="$1" what="$2" mark t0 t1 lines reload built
  mark="$(mktemp)"
  sleep 1
  lines=$(wc -l < "$log")
  t0=$(now)
  case "$what" in
    add) printf '\n' >> "$here/$file" ;;
    remove) perl -0pi -e 's/\n\n\z/\n/' "$here/$file" ;;
    api-add) printf 'public static class UnityLoopMark%s { }\n' "$$" >> "$here/$file" ;;
    api-remove) perl -0pi -e 's/public static class UnityLoopMark\d+ \{ \}\n\z//' "$here/$file" ;;
  esac
  "$U" command script eval --result-only -- --code 'UnityEditor.AssetDatabase.Refresh(); return 1;' >/dev/null 2>&1
  wait_ready
  t1=$(now)
  built=$(find "$UNITY_PROJECT_PATH/Library/ScriptAssemblies" -name '*.dll' -newer "$mark" -exec basename {} .dll \; | sort | paste -sd, -)
  reload=$(tail -n +"$((lines + 1))" "$log" | grep -oE 'Domain Reload Profiling: [0-9]+ms' | tail -1 | grep -oE '[0-9]+')
  rm -f "$mark"
  printf '%s\t%s\t%.1f\t%s\t%s\n' "$file" "$what" "$(echo "$t1 - $t0" | bc)" "${reload:-?}" "${built:-none}"
}

"$U" status --until-ready >/dev/null || { echo "unity-loop: no open Editor on $UNITY_PROJECT_PATH" >&2; exit 2; }
printf 'file\tedit\tseconds\treload_ms\tassemblies_rebuilt\n'
for f in "${files[@]}"; do
  [ -f "$here/$f" ] || { echo "unity-loop: no $f" >&2; continue; }
  for _ in $(seq 1 "$rounds"); do
    one "$f" add
    one "$f" remove
    one "$f" api-add
    one "$f" api-remove
  done
done
git -C "$here" diff --quiet -- "${files[@]}" || echo "unity-loop: a file didn't come back as it was: check git diff" >&2
