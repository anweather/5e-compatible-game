# Releasing an alpha

Alphas are on GitHub Releases, marked **pre-release**, tagged `alpha-X.Y.Z`.
Up to 0.1.x they are Windows zips that players unzip and run; copies
installed with Velopack update themselves in place (see "Updates in place"
below).

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

## Steps

1. **Get main ready.** Everything for the release is merged and pushed, CI
   is green, and the smoke and online probes pass (`tools/probe.sh smoke`,
   `tools/probe.sh online`).
2. **Bump the version.** Set `bundleVersion` in
   `unity/ProjectSettings/ProjectSettings.asset` (Player Settings →
   Version) to `X.Y.Z`, commit it on main (`chore: version X.Y.Z`) and push.
   Application.version comes from it.
3. **Build.** In the Editor, on main's HEAD with a clean tree:
   **Prototype → Build → Release (Windows zip)**, or
   `unity command script eval -- --code 'return PrototypeBuild.Release();'`.
   It refuses a dirty tree (untracked `_Recovery*` aside) and unstable
   network ids, and logs `Release: OK, alpha X.Y.Z (abc1234), N MB -> path`.
   Check the commit in that line is main's HEAD.
4. **Draft the release.** Notes from the log since the last tag:

   ```sh
   last=$(git describe --tags --abbrev=0 --match 'alpha-*' 2>/dev/null)
   git log --no-merges --pretty='- %s' ${last:+$last..}HEAD > /tmp/notes.md   # edit it down
   gh release create alpha-X.Y.Z \
     unity/Builds/Release/SpellChemistryValley-alpha-X.Y.Z-windows.zip \
     --target <FULL 40-char SHA from step 3; a short one gives HTTP 422> --prerelease --draft \
     --title "alpha X.Y.Z" --notes-file /tmp/notes.md
   ```

5. **Test the draft on the PC.** Download the zip from the draft (not the
   local build), unzip into a fresh folder, run it (the exe is unsigned:
   Windows may show any of: Defender's "Windows Security: Scan required"
   (seen on 0.1.0), "Open File – Security Warning: Unknown Publisher" with
   Run (seen on 0.1.1), or SmartScreen's More info → Run anyway), check the Esc menu shows the right version, then play a
   Relay game against another machine on the same release (Esc → Play
   together → Host online / Join code).
6. **Publish:** `gh release edit alpha-X.Y.Z --draft=false`.

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

**Limits:** nothing is signed (Windows SmartScreen and macOS Gatekeeper warn
on first install; updates after that don't prompt). A player more than one
update behind needs every delta in between; if one is missing Velopack falls
back to the full package.

## Later
- **Rules and world data over the air.** `rules/` and `world/` already load
  from `StreamingAssets` at runtime, so a newer data pack could be fetched
  without a new build; online, both players would need the same pack.
- Code signing, so SmartScreen stops warning; a macOS build.
