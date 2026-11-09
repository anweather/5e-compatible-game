#!/usr/bin/env bash
# run.sh: the duplicate-code gate (docs/QUALITY.md) with the jscpd pinned in package-lock.json, from the repo's root:
#   tools/jscpd/run.sh unity/Assets/Prototype sandbox/src
# Installs the lock (npm ci, from npm's cache once it has it) and runs it; settings and threshold are in .jscpd.json.
set -euo pipefail
dir="$(cd "$(dirname "$0")" && pwd)"
npm ci --prefix "$dir" --prefer-offline --no-audit --no-fund --loglevel=error
npm --prefix "$dir" exec --offline -- jscpd "$@"
