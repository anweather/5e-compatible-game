Needs: core
Area: tools/ship-update.sh (and the ship-update skill / docs/RELEASING.md where they describe it); no game code
Branch: ship-update-delta

# ship-update builds no delta when the draft already has this version (the lead, 2026-11-08)

Rebuilding alpha 0.1.12's Windows half at a new commit (d1d142c7, after the CRLF ground.json fix), `ship-update.sh
0.1.12 --windows-only` made no delta: its `vpk download --pre` took the draft's own old 0.1.12 full package as the
base, so the feed listed only 0.1.12 full and the old 3321b336 delta stayed on the draft. Installed 0.1.11 copies would
have updated through that stale delta. The PC repacked by hand against 0.1.11-full.nupkg from alpha-0.1.11 and
uploaded the feed and delta with --clobber.

Fix: the delta's base is always the last *published* release's full package, never a draft or this version. If the
draft already holds files for this version, replace all of them (feed, full, delta, Setup, Portable) together, or
stop with a clear message; never leave a mix of builds. Do the same for the osx half. Test what you can without a
Unity build (a dry run against the real releases with `gh`, or a function test of the base choice). Say in the report
what you couldn't test.

Before landing: `bash -n` and shellcheck if present, the `workers` gate (`tools/workers/gates.sh`), `dotnet test core`
once. Push the branch only, remove this claim on the branch in the last commit. The lead lands it.
