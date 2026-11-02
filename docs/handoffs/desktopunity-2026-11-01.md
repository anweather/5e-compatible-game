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
- A memory reaper can kill background runs: don't restart without the PM's go. `TaskStop` can leave children running: check `ps` and kill by pid. Close only what you've proven is yours, by process id, never by title or name: Windows Terminal shares one process across windows, so close your own with a typed `exit`, and test players share their exe name with Andrew's copies.
- Andrew's game may hold MCP port 47777. A test client talks only to ports found with `Get-NetTCPConnection -OwningProcess <your pid>`.
- A test player that sends chat must use `-claude-stand-in <script>`, or it runs his real Claude. F9 only opens a terminal: never type `claude` there.
- Saving in a test player writes to the game's real settings. A told-once flag there hides a notice from Andrew: remove what your run set (`HKCU\Software\anweather\Spell Chemistry Valley`, by its own value name).
- u1 (VM) and a perf run can't share the PC: the PM sleeps it.

**Probes:** refresh, wait for `"compiling": false`, then `tools/probe.sh <name>`; `--shots <folder>` films. Long bash scripts with apostrophes fail as heredocs: write them to a file. `result.json` is in `unity/Temp/Probe/`; `state: aborted` is not a pass, so read `reason`.

**Hidden work, when Andrew's on the PC:** `Unity.exe -batchmode -projectPath <unity> -logFile <file>` (6000.3.27f1) gives an Editor with no window that takes `unity command`, probes and builds. After it starts, open the yard: `EditorSceneManager.OpenScene("Assets/Prototype/Scenes/ChemistryYard.unity")` through `script eval`, and again after an EditMode test run that opened scenes. `unity recompile --project-path <p> --strict` lists the warnings of this compile (the Editor log is cumulative). `unity close <p>` takes the path as an argument. Players run with `-batchmode` and no window, but not `-nographics` (it floods the log: no compute shaders). Set them BelowNormal. Two peers: `-host-online -room-mode Locked|HostOnly|Open`, `-join-code <code from NET JOIN CODE>`, each with its own `-logFile`. Never run `python -` with both a heredoc and `</dev/null`, or `cat` with no input: both hang.

**Perf:** build with `PrototypeBuild.Build(BuildTarget.StandaloneWindows64, "Builds/PerfRel/SpellChemistryValley.exe", BuildOptions.None)` through `unity command --timeout 280 script eval`. Run `-fps-probe -perf-scenario forestload -perf-seconds N`, flags `-perf-offscreen` (needed when the session is disconnected: a window then draws nothing and hides draw costs), `-perf-no-rain`, `-perf-scope-near`, `-perf-walk-through`, `-perf-look-switch`, `-host-online`, `-no-play-log`. Numbers: `docs/perf/2026-11-01-load.md`. Andrew's play logs are in `%USERPROFILE%\AppData\LocalLow\anweather\Spell Chemistry Valley\`: read them first for any lag report.

**Clips:** `ClipFilm` frames plus `sound.wav`, then ffmpeg, into `unity/Builds/demo-videos/` in the main checkout; MP4 720p, under 5 MB, with a poster. The hidden Editor's Game view is portrait (about 482 × 797) and Cinemachine overrides the camera: `RowHousesProbe.Look` shows the way round, the brain off and the camera rendered to a 1280 × 720 texture.

**State, 2026-11-01 evening:** all landed on dev at 34b7025: game-chat, game-voice, voice-queue, live-desk, voice-trace, houses-view. Nothing of this session's is unmerged. This worktree is detached at 34b7025: branch off dev before work. Test player with everything in it: `unity/Builds/PlayTest/` in this worktree.

**Voice:** dictation starts and stops cleanly, reaching Running in under 0.6 s. Andrew's "no text" was the wrong microphone: Windows' default was a silent Arctis headset (-91 dB) while the Scarlett hears fine. The chat now shows "Listening on" and says where to change it, and `VOICE +ms` trace lines are in the log, with the player's words in them: decide that before a release. No partial or final has yet been seen from `WindowsDictation` by this session; the "Listening on" line and the last wording haven't been seen in a real player; nor has the level bar on a live mic. The pad chord has never been pressed. `-voice-no-meter` drops the meter if words go missing with it on. Whisper's notes and test set: `SpeechChoice.cs`, `tools/voice-samples/make.ps1`.

**Houses:** `RowHouses` draws only what a mod adds or edits, through `RowMesher`; base villages and cabins stay as the scene draws them. Tested through the MCP on two hidden peers and by `rowhouses`; docs in `docs/RULES_MCP.md`.
- Walls that burn or break stay drawn: the cells change, the mesh doesn't (the PM is filing this).
- A brown patch of ground detail shows on the first site's garage roof, cause not found.
- An edited village house keeps its old chimney smoke, and `ValleyChemistry` still holds the hidden scene object.
- A wing on a cabin is a plain closed box square to the grid, beside a cabin at an angle.
- Stacked catalogue mods that each restate a whole spell or row: the later wins whole (big-fireball then blue-fireballs leaves radius 2).
- Simplified from builders.md: walls overlap at joins rather than mitre, eaves are always one 0.375 m grid step, a 45° wall steps on the roof, two roofs just intersect, `worn` is only darker.
- Never checked: anything in a real window or from a wizard's height, walking in or against a new wall, a late joiner seeing a house, viga, canale, porch, beam, shutters and planter on film, `Build Chemistry Yard` rerun.
- The game has no save of its own (`World.Save` is reached only from tests), so nobody sees the refused-saves change.

**Next:**
1. Andrew's first real chat message proves what no one has: the allow-list, `dontAsk`, `Read(./notes.md)`, Claude writing notes, and the wording of the not-signed-in failure (`ClaudeRun.NeedsSignIn` is a guess). Ask the PM how it went, and fix what it shows.
2. Whatever the PM has next, likely walls that stay drawn. A window pass over the houses first, when Andrew's off the PC.
3. Open: #30 and #32 (the GPU climb in rain fell with the main-thread fixes, cause not found); `-perf-water-check` shows 9 to 11 river tiles alpha-only; grass switch 80 to 110 ms, probably the land's layers (not measured apart).
