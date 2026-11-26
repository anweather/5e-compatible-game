#!/usr/bin/env bash
# gates.sh: every gate a landing must pass, from the repo's root. gates.sh <log>
#   Exits 0 only if each gate exits 0. What doesn't need the build runs alongside it, each with its own log, and each is
#   waited for and its own exit code read: a gate that fails must stop the landing (a hidden failure reached dev once).
#   SANDBOX_GATES=1 adds the sandbox's.
#   The core tests are by module (modules.json, modules.mjs; docs/TESTING_LAYERS.md): the test projects of the modules the
#   change touches (git diff GATES_BASE...HEAD, default origin/dev, plus what isn't committed; or GATES_FILES, one path
#   a line), the projects that reference them, and every other project's Unit fixtures as the smoke. A path in no module,
#   a map that doesn't load or no changes found runs them all, as GATES_ALL=1 does. Docs and tools alone run none.
#   Duplicate code (jscpd) isn't a landing gate: CI's duplicates workflow and the sweep (tools/editmode.sh sweep) run it.
#   The workers tests: gates.test.mjs (any gate failing fails this), queue.test.mjs (the brief queue), land.test.mjs (land.sh here), bridge-watch.test.mjs (the watcher's worker pass).
set -uo pipefail
log="${1:?gates.sh <log>}"
: > "$log"
names=(); pids=(); failed=0

show() { grep -v " warning S3776" "$1" | tail -20; }

alongside() {
  local name="$1"; shift
  "$@" > "$log.$name" 2>&1 &
  names+=("$name"); pids+=($!)
}

gate() {
  local name="$1"; shift
  if "$@" >> "$log" 2>&1; then echo "  ok  $name"
  else
    show "$log"; echo "  FAILED  $name"; failed=1
  fi
}

# Which core test projects run: the map's choice, printed with why. Anything unclear runs them all.
here="$(cd "$(dirname "$0")" && pwd)"
mode=all; run=(); smoke=(); smoke_filter=TestCategory=Unit
if [[ "${GATES_ALL:-0}" != 1 ]]; then
  if [[ -n "${GATES_FILES:-}" ]]; then files="$GATES_FILES"
  else
    base="${GATES_BASE:-origin/dev}"
    files="$( { git diff --name-only "$base...HEAD" && git diff --name-only HEAD && git ls-files --others --exclude-standard; } 2>/dev/null | sort -u)" || files=""
  fi
  if chosen="$(printf '%s\n' "$files" | node "$here/modules.mjs" choose 2>"$log.why")" && [[ "$chosen" == mode=* ]]; then
    eval "$chosen"
  else mode=all; echo "    everything: modules.mjs didn't answer" >> "$log.why"
  fi
fi
case "$mode" in all|some|none) ;; *) mode=all ;; esac
runs="${run[*]-}"; echo "  core tests: $mode${runs:+ ($runs)}"
[[ -s "$log.why" ]] && cat "$log.why"; rm -f "$log.why"
alongside "chat-report" node --test tools/chat-report.test.mjs
alongside "chat-strict" node --test tools/chat-strict.test.mjs
alongside "workers" node --test tools/workers/gates.test.mjs tools/workers/queue.test.mjs tools/workers/land.test.mjs tools/workers/bridge-watch.test.mjs
if [[ "${SANDBOX_GATES:-0}" == 1 ]]; then
  gate "sandbox types" npm --prefix sandbox run typecheck
  gate "sandbox tests" npm --prefix sandbox test
  gate "sandbox build" npm --prefix sandbox run build
fi

gate "core build" dotnet build core -c Release -warnaserror -m:1 # -m:1: a parallel build can turn Rules.Mcp's S3776 into an error
# Formatting after the build, not beside it: both write core's obj folders.
[[ $failed == 0 ]] && gate "core format" dotnet format core --verify-no-changes --no-restore
# The tests run 6 fixtures at once and peak near 7 GB; when the host is short (an Editor, a VM), run 3 so a
# landing doesn't push the watcher into pausing a worker.
workers=6; if command -v powershell >/dev/null 2>&1; then
  freegb="$(powershell -NoProfile -Command "[math]::Floor((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1MB)" 2>/dev/null | tr -d '\r')"
  [[ "$freegb" =~ ^[0-9]+$ ]] && (( freegb < 10 )) && workers=3
fi
# The Sweep category (whole runs, peer by peer) is the sweep's, layer 4 of docs/TESTING_LAYERS.md: a landing skips it.
# sweep.runsettings (everything) and this filter, not the default runsettings, so what a landing runs is said here.
tests=(-c Release --no-build -s core/sweep.runsettings)
if [[ $failed == 0 && $mode == all ]]; then
  gate "core tests" dotnet test core "${tests[@]}" --filter "TestCategory!=Sweep" -- NUnit.NumberOfTestWorkers=$workers
elif [[ $failed == 0 && $mode == some ]]; then
  # One run over a solution filter, so the chosen projects still run side by side as the whole suite does.
  node "$here/modules.mjs" slnf "$log.run.slnf" "${run[@]}"
  gate "core tests" dotnet test "$log.run.slnf" "${tests[@]}" --filter "TestCategory!=Sweep" -- NUnit.NumberOfTestWorkers=$workers
  if [[ ${#smoke[@]} -gt 0 ]]; then
    node "$here/modules.mjs" slnf "$log.smoke.slnf" "${smoke[@]}"
    gate "core smoke" dotnet test "$log.smoke.slnf" "${tests[@]}" --filter "$smoke_filter" -- NUnit.NumberOfTestWorkers=$workers
  fi
  rm -f "$log.run.slnf" "$log.smoke.slnf"
fi

for i in "${!pids[@]}"; do
  if wait "${pids[$i]}"; then echo "  ok  ${names[$i]}"
  else
    show "$log.${names[$i]}"; echo "  FAILED  ${names[$i]}"; failed=1
  fi
  cat "$log.${names[$i]}" >> "$log"; rm -f "$log.${names[$i]}"
done
# One line per test project (core has eleven: the Chem.<Module>.Tests and the tools').
grep -E 'Passed!|Failed!' "$log" | sed 's/^ */  /'
exit "$failed"
