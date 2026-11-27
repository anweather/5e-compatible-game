#!/usr/bin/env bash
# editmode.sh: run the Editor's EditMode tests in the open Unity Editor, by group (docs/TESTING_LAYERS.md).
#   tools/editmode.sh [unity|chem|all|sweep] [extra args for `unity command test run`]
#     unity   (default) the Unity-only tests: Prototype.Game.Tests (Scripts/Editor/Tests, Scripts/Playtest/Editor) and
#             Prototype.Probes.Tests (Scripts/Probes/Editor/Tests), with the view plans' Prototype.Plans.Tests
#             (Scripts/Plans/Tests, which dotnet runs too: this is their Mono pass), ~470 tests.
#             About 4 min on the Mac. Run it for any change to Scripts/ or Net/.
#     chem    the Chemistry tests under Mono (Chemistry.Tests, ~1570 tests): the same tests `dotnet test core` runs under .NET,
#             so only for a change to Chemistry/ (Mono can round differently) or the daily sweep. About 40 min on the Mac
#             (the PC ~14): run it in the background, never enter Play meanwhile.
#     all     both, as a bare `unity command test run --mode EditMode` does.
#     sweep   all, plus the Explicit Timing tests (an idle Editor only), then the duplicate-code gate (jscpd, as CI's
#             duplicates workflow runs it): the landing gates leave it to CI and the sweep.
#   Prints the totals and every failure from Library/TestResults.xml (the CLI's own JSON is cut short on a long run).
#   Needs `unity` on PATH ($HOME/.unity/bin) and UNITY_PROJECT_PATH, as the other tools here do. The yard scene must be built.
set -uo pipefail
group="${1:-unity}"; [[ $# -gt 0 ]] && shift
here="$(cd "$(dirname "$0")/.." && pwd)"
export UNITY_PROJECT_PATH="${UNITY_PROJECT_PATH:-$here/unity}"
case "$group" in
  # The assembly filter is a substring: "Prototype." picks the three test assemblies (and Chemistry.Tests is all that's left out).
  unity) filter=(--filter Prototype. --filter_type assembly); secs=900 ;;
  chem)  filter=(--filter Chemistry.Tests --filter_type assembly); secs=3600 ;;
  all)   filter=(); secs=3600 ;;
  sweep) filter=(--include_explicit true); secs=3600 ;;
  *) sed -n 2,15p "$0"; exit 2 ;;
esac
rm -f "$UNITY_PROJECT_PATH/Library/TestResults.xml"
unity command --timeout "$secs" test run --mode EditMode "${filter[@]}" "$@" --result-only > /dev/null
code=$?
node -e '
const fs = require("fs"), f = process.argv[1];
if (!fs.existsSync(f)) { console.log("no TestResults.xml: the run did not finish (exit " + process.argv[2] + ")"); process.exit(1); }
const x = fs.readFileSync(f, "utf8");
const run = x.match(/<test-run [^>]*>/)[0], at = k => (run.match(new RegExp(" " + k + "=\"([^\"]*)\"")) || [])[1];
console.log(`EditMode: ${at("total")} tests, ${at("passed")} passed, ${at("failed")} failed, ${at("skipped")} skipped, ${Math.round(at("duration"))} s`);
let failed = 0;
for (const m of x.matchAll(/<test-case [^>]*?fullname="([^"]*)"[^>]*?result="Failed"[^>]*>[\s\S]*?<message><!\[CDATA\[([\s\S]*?)\]\]>/g)) { failed++; console.log("  FAILED " + m[1] + ": " + m[2].trim().split("\n")[0].slice(0, 160)); }
process.exit(Number(at("failed")) > 0 ? 1 : 0);
' "$UNITY_PROJECT_PATH/Library/TestResults.xml" "$code"
result=$?
if [[ "$group" == sweep ]]; then
  # jscpd runs out of Node's default heap on a 5 GB VM; the pinned one (tools/jscpd/package-lock.json).
  jlog="$(mktemp)"
  if (cd "$here" && NODE_OPTIONS="${NODE_OPTIONS:-} --max-old-space-size=3072" bash tools/jscpd/run.sh unity/Assets/Prototype sandbox/src packages > "$jlog" 2>&1); then
    echo "Duplicate code: under the threshold"
  else
    echo "  FAILED duplicate code (jscpd):"; tail -20 "$jlog"; result=1
  fi
fi
exit "$result"
