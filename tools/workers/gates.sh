#!/usr/bin/env bash
# gates.sh: every gate a landing must pass, from the repo's root. gates.sh <log>
#   Exits 0 only if each gate exits 0. What doesn't need the build runs alongside it, each with its own log, and each is
#   waited for and its own exit code read: a gate that fails must stop the landing (a hidden failure reached dev once).
#   SANDBOX_GATES=1 adds the sandbox's.
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

# jscpd runs out of Node's default heap on a 5 GB VM.
alongside "duplicates" env NODE_OPTIONS="${NODE_OPTIONS:-} --max-old-space-size=3072" npx --yes jscpd@4.0.5 unity/Assets/Prototype sandbox/src
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
[[ $failed == 0 ]] && gate "core tests" dotnet test core -c Release --no-build

for i in "${!pids[@]}"; do
  if wait "${pids[$i]}"; then echo "  ok  ${names[$i]}"
  else
    show "$log.${names[$i]}"; echo "  FAILED  ${names[$i]}"; failed=1
  fi
  cat "$log.${names[$i]}" >> "$log"; rm -f "$log.${names[$i]}"
done
grep -E 'Passed!|Failed!' "$log" | tail -1 | sed 's/^ */  /'
exit "$failed"
