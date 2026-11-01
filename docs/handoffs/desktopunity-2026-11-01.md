# DesktopUnity handoff, 2026-11-01

**Who:** the Gameplay session on the Windows PC, the only machine with a GPU. Worktree `.claude/worktrees/host-ux`, Editor at `unity/` there. Work comes from the Project Manager session.

**Owns:** the view (`unity/Assets/Prototype/Scripts/`), perf, Windows test players. Not the Mac's pause menu, spell list, radial, HUD orbs or spell effects. Not core tick stages while u1 works there; a climb in a tick stage goes to the PM.

**Standing rules** (more in `memory/`):
- Send the PM a pushed branch; the PM lands it. Don't push dev, except docs-only. Don't publish.
- Before every push: `git fetch`, then `git diff origin/dev...HEAD --stat` must list only your files. Never squash with `git reset --soft origin/dev`: 50fd301 reverted landed work that way. Use `git merge-base`.
- After a switch or merge, refresh and wait for the compile before any probe; never merge during a probe.
- Green doesn't mean it shows: look at the frames. A check once passed with steam invisible, and a walk "through" fire was on its edge.
- Report what wasn't tested. Never kill Unity: `unity close`, or the window. Never print process command lines (Hub token). Never sign in or use Andrew's Claude session; `claude --help` is fine.
- If Andrew's session is active (`query user`), open no windows on his screen and tell the PM before anything heavy. If he asks for the PC, stop at once. `query user` idle time is unreliable.
- A memory reaper can kill background runs: don't restart without the PM's go. `TaskStop` can leave children running: check `ps` and kill by pid. Check that a window is yours before closing it.
- u1 (VM) and a perf run can't share the PC: the PM sleeps it.

**Probes:** refresh, wait for `"compiling": false`, then `tools/probe.sh <name>`; `--shots <folder>` films. Long bash scripts with apostrophes fail as heredocs: write them to a file. `result.json` is in `unity/Temp/Probe/`; `state: aborted` is not a pass, so read `reason`.

**Perf:** build with `PrototypeBuild.Build(BuildTarget.StandaloneWindows64, "Builds/PerfRel/SpellChemistryValley.exe", BuildOptions.None)` through `unity command --timeout 280 script eval`. Run `-fps-probe -perf-scenario forestload -perf-seconds N`, flags `-perf-offscreen` (needed when the session is disconnected: a window then draws nothing and hides draw costs), `-perf-no-rain`, `-perf-scope-near`, `-perf-walk-through`, `-perf-look-switch`, `-host-online`, `-no-play-log`. Numbers: `docs/perf/2026-11-01-load.md`. Andrew's play logs are in `%USERPROFILE%\AppData\LocalLow\anweather\Spell Chemistry Valley\`: read them first for any lag report.

**Clips:** `ClipFilm` frames plus `sound.wav`, then ffmpeg, into `unity/Builds/demo-videos/` in the main checkout; MP4 720p, under 5 MB, with a poster.

**Branches:** `no-chimes` (710cb7d) and `look-switch` (09a770f) were sent to the PM; check they landed. `game-chat` (4d22090) is pushed, not merged; test player at `unity/Builds/ChatTest/` in this worktree.

**Next:**
1. Andrew's first real chat message proves what no one has: the allow-list, `dontAsk`, `Read(./notes.md)`, Claude writing notes, and the wording of the not-signed-in failure (`ClaudeRun.NeedsSignIn` is a guess). Fix what it shows.
2. `game-voice` waits on the PM's call: no microphone path exists. Option A, `DictationRecognizer`, is Windows only and online; option B, whisper.cpp, is on-device, untried. Either sits behind a small `ISpeech`, feeding `GameChat.Type`.
3. Open: #30 and #32 (the GPU climb in rain fell with the main-thread fixes, cause not found); `-perf-water-check` shows 9 to 11 river tiles alpha-only; grass switch 80 to 110 ms, probably the land's layers (not measured apart).
