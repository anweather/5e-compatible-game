# Releasing an alpha

Alphas are on GitHub Releases, marked **pre-release**, tagged `alpha-X.Y.Z`.
Since 0.1.2 each release has Velopack installers and packages for Windows
and macOS, and installed copies update themselves in place (see "Updates in
place" below). 0.1.0 and 0.1.1 were Windows zips, described in "What a
release contains".

## What a release contains

`Builds/Release/SpellChemistryValley-alpha-X.Y.Z-windows.zip` (in `unity/`),
one top-level folder with:

- `SpellChemistryValley.exe` and its `_Data`, `MonoBleedingEdge`,
  `UnityPlayer.dll` and friends: a **non-development** build;
- `README.txt`: running it, SmartScreen, controls, playing together, where
  `Player.log` is, the alpha caveat (template: `unity/ReleaseReadme.txt`);
- `CREDITS.txt`: the SRD 5.2.1 CC-BY-4.0 attribution, the Unity packs, "Made
  with Unity" (text: `Scripts/Credits.cs`, also shown in Esc → Credits).

The build carries `StreamingAssets/build.json` (version, commit, date). The
game logs "Spell Chemistry Valley alpha X.Y.Z (abc1234), built ..." at
startup and shows "alpha X.Y.Z (abc1234)" at the bottom of the Esc menu.

## Steps: the release candidate

A release about every 2 days, or sooner after a big fix (J-019 in
`JUDGMENT_CALLS.md`). The Build lane runs it; the coordinator calls the
freeze. The draft on GitHub *is* the release candidate (RC): the same
files players will get, tested before anyone can see it.

1. **Freeze dev.** The coordinator stops merges into `dev`. Everything for the
   release is merged and pushed, CI is green on `dev`, and the smoke and
   online probes pass (`tools/probe.sh smoke`, `tools/probe.sh online`).
2. **Merge dev into main** (`git switch main`, `git merge --ff-only
   origin/main`, `git merge --no-ff origin/dev`, push).
3. **Bump the version** on main: `bundleVersion` in
   `unity/ProjectSettings/ProjectSettings.asset` (Player Settings → Version)
   to `X.Y.Z`, committed as `chore: version X.Y.Z` and pushed. After a pull
   that changes ProjectSettings, restart the Editor before building.
4. **Build the RC in two halves, at the same time** (J-025): the PC builds
   Windows (its shader cache is warm; the Mac compiles every Direct3D
   variant from scratch, about 35 minutes) and the Mac builds macOS (the PC
   has no Mac Build Support). On each machine: Editor open on main's HEAD
   (the same commit on both), a clean tree, the yard rebuilt, then
   - the Mac: `tools/ship-update.sh X.Y.Z --mac-only`
   - the PC: `tools/ship-update.sh X.Y.Z --windows-only`

   Each builds its player, packs it with Velopack (a delta from the last
   release on its channel) and uploads only its own files (`releases.osx.json`
   and the `osx` packages, or `releases.win.json` and the `win` ones) into
   the one **draft** `alpha-X.Y.Z` (see "Updates in place" below). Start one
   machine first and the other once it prints "Release alpha-X.Y.Z is ready":
   whichever starts first creates the draft before building. The script
   refuses a draft that targets another commit, and stops if both made one
   (two releases with the tag: delete the extra, then `--skip-build`). Both
   halves must log the same `NET CONFIG` hash (`-host-online`).
   One machine can still build both: `tools/ship-update.sh X.Y.Z` on the Mac.
5. **Notes:** player-facing, from the merges since the last release
   (`git log origin/main@{1}..origin/main --first-parent`, or
   `alpha-<last>..main`): new, faster, fixed, how to install. Then
   `gh release edit alpha-X.Y.Z --notes-file notes.md`.
6. **Test the RC** (the PC's steps; the coordinator schedules them):
   - Windows: install from the draft's `SpellChemistryValley-win-Setup.exe`
     (not a local build); the Esc menu shows `alpha X.Y.Z (<sha>)`.
   - The playtest bot runs on the Windows RC (`-playtest`, J-018); findings
     are checked against dev before anything is filed.
   - A quick human look, and a Mac↔PC Relay game: `players=2`,
     `desync=False`, no exceptions in either `Player.log`.
   - Mac: the portable zip into `~/Applications`, the right version shows.
   - When quitting has changed: the Velopack restart check under "Updates
     in place" (test channel).
7. **Fix blockers on main** (a branch off main, merged into main, then main
   merged back into dev), rebuild the RC with `ship-update.sh X.Y.Z`
   (`--clobber` replaces the draft's files) and test again.
8. **Publish** on the go-ahead: `gh release edit alpha-X.Y.Z --draft=false`.
   Installed copies pick it up at their next start.

## Versions and players

- **Both players need the same build.** The joining player sends its
  version and commit in the connection request; the host refuses a
  different one with a reason ("Host is on alpha 0.1.1 (def5678); you're on
  alpha 0.1.0 (abc1234). Update from ..."), which the joiner sees in the
  HUD and the Esc menu before going back to solo. A build with different
  network prefabs is refused earlier by Netcode's config hash, with no
  reason; the joiner is then told it's "probably a different version".
  The Editor only compares versions, not commits, so it can join builds.
- **Update check.** At startup a non-development build (and the Editor)
  asks `api.github.com/repos/anweather/5e-compatible-game/releases` once,
  with a 5 s timeout, and if a published `alpha-X.Y.Z` is newer than its
  own version it shows "alpha X.Y.Z is out" on the HUD and an "Open
  download page" button in the Esc menu. Drafts don't count, so a draft is
  invisible to players until step 6. Offline or rate-limited, it says
  nothing. Code: `UpdateCheck`, `BuildVersion.NewestRelease`.
- **Bug reports.** Players send
  `%USERPROFILE%\AppData\LocalLow\anweather\Spell Chemistry Valley\Player.log`
  (Esc → Open log folder); its first lines name the version and commit.

## Updates in place (Velopack)

A copy installed with Velopack's installer updates itself: at launch it asks
GitHub for a newer release on its channel, downloads only what changed (a
code change is ~150 KB, against ~500 MB for the whole game), shows
"Updating to alpha X.Y.Z..." and restarts into it. Code: `SelfUpdate.cs`
(Velopack), `UpdateCheck.cs` (the check at launch, the overlay), the Esc
menu's "Update and restart" (with `-no-auto-update`). Zip copies and the
Editor keep the old "Open download page" path.

**Shipping one:** `tools/ship-update.sh X.Y.Z` (or the `ship-update` skill).
On the Mac or the PC, with the Editor open on the commit to ship and a clean
tree, it:

1. builds a macOS app and a Windows folder at that version
   (`UpdateBuild.Players`; Player Settings' version is put back after);
2. fetches the last update on each channel from GitHub, so it can make the
   delta (`vpk download github`);
3. packs each with `vpk` (the Windows one cross-packed with `[win]`), giving
   a full package, a delta, `Setup.exe` / `Setup.pkg`, a portable zip and
   the channel's feed, `releases.<channel>.json`;
4. uploads both channels into one GitHub release with `gh` (`vpk upload
   github` can't find a draft by its tag).

Without `--test` that's the draft `alpha-X.Y.Z` (test it, then publish as in
step 6 above). With `--test` it's `update-test-X.Y.Z`, published at once, on
the channels `osx-test` and `win-test` under the app id
`SpellChemistryValleyTest`: only copies installed from a test release follow
those (in their own folder and update cache), so trying the loop never reaches players, and the old
zip check ignores `update-test-*` tags. Needs vpk (`dotnet tool install -g
vpk`) and `gh` signed in.

**Installing (once per player):**

- **Windows:** run `SpellChemistryValley-win-Setup.exe` from the release (the
  SmartScreen warnings above still apply). It installs to
  `%LOCALAPPDATA%\SpellChemistryValley` with a Start menu shortcut.
- **macOS:** the app isn't signed or notarized, so Gatekeeper blocks it.
  Either run `SpellChemistryValley-osx-Setup.pkg` and allow it in System
  Settings → Privacy & Security → "Open Anyway", or unzip the portable zip
  into `/Applications` or `~/Applications` and do the same for the app.
  **Keep it out of Documents, Desktop and Downloads:** after an update
  Velopack relaunches the game on its own, and macOS then holds the
  game's file access in those folders for a privacy prompt, which freezes
  it at startup (seen in the spike, 2026-10-26).
- A copy unzipped from the portable zip has no local package to patch, so
  its first update downloads the full package; every one after that is a
  delta. The installers don't have that cost.

**Check the restart on Windows before publishing** (new since QuickExit, the
quit-crash fix in J-010). A Windows player no longer lets Unity shut down:
`QuickExit.cs` catches every quit, runs `BeforeExit`, leaves an online
session and ends the process. So an update's restart (`SelfUpdate` calls
Velopack's `WaitExitThenApplyUpdates`, then `Application.Quit`) goes through
it. Velopack waits for the process to end however it ends, so it should be
fine, but test it on the first ship after QuickExit and after any change to
quitting:

1. On the PC, install the current test release with its
   `SpellChemistryValley-win-Setup.exe` (`update-test-*`, app id
   `SpellChemistryValleyTest`), start it once, quit.
2. Ship the next one with `tools/ship-update.sh X.Y.Z --test`.
3. Start the installed copy: "Updating to alpha X.Y.Z...", then it closes
   and comes back by itself within a few seconds, and the Esc menu shows the
   new version and commit.
4. Check that no old `SpellChemistryValley.exe` is left in Task Manager, that
   `%LOCALAPPDATA%\SpellChemistryValleyTest\velopack.log` shows the update
   applied with no "waiting for process" timeout, and that there's no new
   D3D12Core crash in the Event Viewer (Windows Logs → Application).
5. Once more with the Esc menu's "Update and restart": start with
   `-no-auto-update` while a newer test release is out.

If the restart fails only with QuickExit, `-clean-quit` turns it off for a
comparison run.

**Limits:** nothing is signed (Windows SmartScreen and macOS Gatekeeper warn
on first install; updates after that don't prompt). A player more than one
update behind needs every delta in between; if one is missing Velopack falls
back to the full package.

## Later
- **Rules and world data over the air.** `rules/` and `world/` already load
  from `StreamingAssets` at runtime, so a newer data pack could be fetched
  without a new build; online, both players would need the same pack.
- Code signing, so SmartScreen stops warning; a macOS build.
