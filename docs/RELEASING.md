# Releasing an alpha

Alphas are Windows zips on GitHub Releases, marked **pre-release**, tagged
`alpha-X.Y.Z`. Players unzip and run; there is no installer and no
auto-updater yet (see "Later" below).

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
     --target <commit from step 3> --prerelease --draft \
     --title "alpha X.Y.Z" --notes-file /tmp/notes.md
   ```

5. **Test the draft on the PC.** Download the zip from the draft (not the
   local build), unzip into a fresh folder, run it (the exe is unsigned:
   Defender may show "Windows Security: Scan required" and scan it first,
   and SmartScreen may need More info → Run anyway; seen on alpha 0.1.0), check the Esc menu shows the right version, then play a
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

## Later

- **Velopack delta updates.** Download only what changed, update in place,
  sign the installer. Spike it before 0.2.
- **Rules and world data over the air.** `rules/` and `world/` already load
  from `StreamingAssets` at runtime, so a newer data pack could be fetched
  without a new build; online, both players would need the same pack.
- Code signing, so SmartScreen stops warning; a macOS build.
