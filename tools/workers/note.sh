#!/usr/bin/env bash
# One line in the shared work log, so `status` can say where a job is without anyone reading a transcript.
# Usage: note.sh <who> <phase> [what]   e.g. note.sh pc-task compiled "aimed-spells 34864ec7, clean"
# Phases: start, switched, compiled, tests, probe, clip, pushed, blocked, done.
set -uo pipefail
[[ $# -ge 2 ]] || { echo "usage: note.sh <who> <phase> [what]" >&2; exit 2; }
log="${BRIDGE:-$HOME/.agent-bridge}/work.log"
mkdir -p "$(dirname "$log")"
who="$1" phase="$2"; shift 2
printf '%s %s %s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$who" "$phase" "$*" | cut -c1-200 >> "$log"
