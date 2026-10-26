# Setting up the Windows PC as the Unity machine

The PC (Ryzen 7 7700X, 32 GB, RTX 4080 Super) is the main Unity machine:
agents drive the Editor there, and the Mac is for review and for the second
player in real two-machine multiplayer tests. Claude Code runs on the PC and
is reached from a phone through Remote Control.

## 1. Install

| Tool | Version / notes |
|---|---|
| Git for Windows | Long paths on: `git config --global core.longpaths true` |
| Unity Hub, then Editor **6000.3.27f1** | Modules: Windows Build Support (IL2CPP optional). Same Editor version as `unity/ProjectSettings/ProjectVersion.txt` |
| .NET SDK 8 | `dotnet build core -c Release -warnaserror` and `dotnet test core` (CI uses 8.0.x) |
| Node 22.18 or later | The web sandbox (`sandbox/`): `npm ci`, `npm test`, `npm run typecheck` |
| ffmpeg | Demo videos (`winget install ffmpeg`) |
| Claude Code (native Windows, not WSL) | It has to launch and drive the Windows Unity Editor |
| Unity CLI (`unity`) | Drives the open Editor through the `com.unity.pipeline` package (already in `Packages/manifest.json`). Check with `unity --help` and `unity skill show` |

## 2. Get the project

```
git clone git@github.com:anweather/5e-compatible-game.git
cd 5e-compatible-game
git switch feat/chunked-valley        # current work; see "Branches" below
```

Open `unity/` in Unity Hub with 6000.3.27f1 and sign in to the Unity account
that owns the Asset Store packs.

## 3. Restore the Asset Store packs

They aren't in git. `unity/AssetPacks.json` lists them. Run
**Prototype → Restore Asset Packs**. It takes each pack from the Editor's
download cache (`%APPDATA%\Unity\Asset Store-5.x` on Windows) or downloads it
first, then imports it non-interactively (subsets where the manifest says
so). The account must have every pack in My Assets. Never commit pack folders
or anything under `Generated/`, `Builds/` or `Recordings/`; they're gitignored.

Then rebuild the scene: **Prototype → Build Chemistry Yard** (it also builds
the valley).

## 4. Check it works

1. `dotnet build core -c Release -warnaserror` and `dotnet test core`: all pass.
2. In the Editor: 0 compile errors. EditMode tests all pass
   (`unity command test run --result-only`).
3. Play the yard: walk, cast 1–8, Tab for the Earth page, M for mods.
4. **Prototype → Build → Windows**, then run
   `Builds\Windows\SpellChemistryValley.exe -fps-probe` and note the FPS line.
   The Windows build has never been run before this.
5. Two-machine multiplayer: Host online on one machine, join by code on the
   other (Relay). Never tested across two real machines before this.

Record the results (FPS mean and 95th percentile, any errors) in `docs/QUALITY.md`.

## 5. Differences from the Mac, for agents

| Mac | Windows |
|---|---|
| Scratch files in `/private/tmp/...` | Use the session scratchpad Claude Code gives you, or `%TEMP%` |
| `osascript` reads and clicks Unity dialogs | PowerShell UI Automation, e.g. `[System.Windows.Automation.AutomationElement]`. Read a dialog's text before clicking anything |
| `ps -o rss= -p <pid>` for Editor memory | `Get-Process Unity \| Select-Object WorkingSet64` |
| `cp -c` (APFS clone) for a cheap second project copy for batchmode tests | No cheap clone: a full copy of `unity/` including `Library/` (~10 GB). Or run batchmode tests in the one Editor's project only when it's closed |
| `~/.dotnet/dotnet` | `dotnet` on PATH |
| Unity log `~/Library/Logs/Unity/Editor.log` | `%LOCALAPPDATA%\Unity\Editor\Editor.log` |

## 6. Editor stability notes

- Audio stays **on** (Starter Assets footsteps; voice chat planned). Never
  disable audio as a workaround.
- `Enable Output Suspension` is off (Project Settings → Audio). With it on,
  an unattended silent Editor suspends audio, FMOD fails to resume it, and
  the next audio update hits the "ADTM … deadlock" check: a hang or a crash.
  If `FMOD failed to switch back to normal output` still shows in the Editor
  log, restart the Editor before entering Play mode.
- Video recording: write frames to disk (the overnight showcase's JPEG mode),
  never the in-memory movie encoder at 1080p; it ran the Mac out of memory.

## Branches

Pushed work branches, none merged to `main` yet:

| Branch | What |
|---|---|
| `feat/chunked-valley` | Current: chunked world (docs/WORLD_STREAMING.md), built on everything below |
| `overnight/integration` | Physics, items, earth shaping, live rules, online, overnight demo |
| `feat/chunked-core`, `feat/earth-shaping`, `feat/live-rules`, `feat/physics-props`, `refactor/unity-code-health` | The individual features, for review |
