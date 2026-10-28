---
name: ship-update
description: Push an in-place update of Spell Chemistry Valley to installed copies (Velopack) by building macOS and Windows, packing a delta and uploading it to GitHub Releases. Use when Andrew says "ship an update", "push an update", "test the updater", or wants installed clients to pick up what's on main.
---

# Ship an update in place

`docs/RELEASING.md`, "Updates in place", is the reference. The work is one
script: `tools/ship-update.sh X.Y.Z [--test] [--mac-only | --windows-only]`.
A real release is built in two halves (J-025): `--mac-only` on the Mac,
`--windows-only` on the PC, the same commit, into one draft.

## Pick the mode

- **`--test`**: an update-loop test. Published at once as
  `update-test-X.Y.Z` on the channels `osx-test` / `win-test` under the
  app id `SpellChemistryValleyTest`, which only test installs have (their own
  folder and update cache, so they never touch a real install). No player sees it. Fine to run without asking.
- **No flag**: a real update for players, the draft `alpha-X.Y.Z`. Follow
  the `release` skill around it (bump the version, the two-machine test, and
  **publish only on Andrew's go-ahead**).

The version must be newer than the last one on that channel (check the
releases page, or `gh release list`).

## Steps

1. `git branch --show-current && git status --short`: the commit to ship is
   checked out and the tree is clean (only `_Recovery*` untracked). The
   Editor is open on this project. If the PC is profiling or building, run
   it on the Mac: both platforms build there.
2. Run it in the background (builds ~5 min, uploads ~3 GB):
   `tools/ship-update.sh X.Y.Z --test` (vpk is in `~/.dotnet/tools`).
3. It ends with the release URL. Check the release lists, per channel:
   `releases.<channel>.json`, the full `.nupkg`, a `-delta.nupkg` (except the
   very first one on a channel), the Setup and the portable zip.
4. Prove it on a machine with an older install from that channel: launch it,
   watch "Updating to alpha X.Y.Z..." then the restart, and check `Player.log`
   for `Self update: installed by Velopack as SpellChemistryValley X.Y.Z`. The
   Velopack log says whether it took the delta: macOS
   `~/Library/Logs/velopack_<app id>.log`, Windows
   `%LOCALAPPDATA%\<app id>\velopack.log` (the app id is `SpellChemistryValleyTest` for test releases).

## Gotchas

- On the Mac, an install under Documents, Desktop or Downloads freezes after
  the update's relaunch (macOS privacy prompt). Install to `~/Applications`.
- A copy unzipped from the portable zip downloads the full package on its
  first update; later ones are deltas.
- Don't delete releases or tags without Andrew saying so; test releases
  stay until he asks.
