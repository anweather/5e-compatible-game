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

**State, 2026-11-02:** landed on dev: #57 lighting (09e4e10), #58 freeze (d5de6de). `chat-telemetry` is pushed at 2d612a8, not merged: chat-record plus the view's whole list. The `host-ux` worktree is on it, and no Editor is left running. Don't touch `unity/Builds/PlayTest/` unless the PM says: it's 1c97274, what Andrew plays. A hidden Editor skips `YardAutoBuild`: run Build Chemistry Yard by hand after a builder or creature change. Run virtual-player probes in the foreground, one at a time: the reaper kills a background parent while its script lives on. A chat log and mcp log go beside the Editor's `-logFile`, here the scratchpad.

**Next, from the PM:** `mods-add-things` (dbf45a9 on origin, never compiled in Unity): merge it into a PC branch, compile, do the view's four items in `docs/RULES_MCP.md` "Things", land both together, then the squirrel test in a real player.

**Voice (#58 stays open):** the freeze can't recur: a recogniser that never finished is left behind and disposed once it does, and the device lookup is off the main thread. Silence always ended cleanly; only sound coming in sticks it. Windows runs one dictation at a time, so the next press says "still busy" and voice returns when the old one ends; a truly stuck one would hold that until a restart. Never proven: the real stuck case, Dispose as the block (inferred from Andrew's trace), real words, `-voice-no-meter` with sound. It needs someone talking: run `tools/probe.sh dictation` with a longer hold while they talk, and try Win+H on the Scarlett, which reports 2 channels, 44.1 kHz, 32-bit against the others' 48 kHz 16-bit. `VOICE +ms` trace lines hold the player's words: decide before a release. Whisper's notes: `SpeechChoice.cs`, `tools/voice-samples/make.ps1`.

**Houses:** `RowHouses` draws only what a mod adds or edits, through `RowMesher`; base villages and cabins stay as the scene draws them. `RowRuin` follows the cells: stretches fall to stumps, the roof goes with what held it. Probes: `rowhouses`, `rowburn` (`--shots` films; without it the frame numbers are clean), `roomruin` (two peers and a late joiner). Docs in `docs/RULES_MCP.md`, "A house in play".
- Plain `stucco-wall` only breaks: the fixture adds `burn`. The PM holds that decision. A fireball on plain stucco is untried.
- Flames barely show in the frames: unknown whether that's the off-screen render. Mod walls burn at ground height (`FireHeight` knows only log walls).
- `#55`: a late joiner's replay differs for a checksum or two after any Fireball, no house needed.
- Stumps have no collider (up to 0.7 m), and a wall's blocker fills its 1.5 m cell, so from inside it stops you short of what's drawn.
- A pane cell doesn't burn, so its frame stands in a ruin. An edited solid village house leaves invisible fill once its walls go, and `ValleyChemistry` still holds the hidden scene object.
- The roof vanishes with no fall, and part of it hangs until half the walls are down. Arches look stepped up close.
- `find_site` passes lots with a steep bank by the wall (1.7 m at [262, 352]).
- A wing on a cabin is a plain closed box square to the grid, beside a cabin at an angle.
- Stacked catalogue mods that each restate a whole spell or row: the later wins whole (big-fireball then blue-fireballs leaves radius 2).
- Simplified from builders.md: walls overlap at joins rather than mitre, a 45° wall steps on the roof, two roofs just intersect, `worn` is only darker.
- Never checked: anything in a real window, the parapet, beam, shutters and planter on film, film from the second peer, a street of burning houses, `Build Chemistry Yard` rerun.
- The game has no save of its own (`World.Save` is reached only from tests), so nobody sees the refused-saves change.

**Next:**
1. Andrew's first real chat message proves what no one has: the allow-list, `dontAsk`, `Read(./notes.md)`, Claude writing notes, and the wording of the not-signed-in failure (`ClaudeRun.NeedsSignIn` is a guess). The record, bar and token list have only met a stand-in: read the first real `chat-log-*.jsonl` with `tools/chat-report.mjs`.
2. A window pass over the houses when Andrew's off the PC: flames on walls above all.
3. Open: #30 and #32 (the GPU climb in rain fell with the main-thread fixes, cause not found); `-perf-water-check` shows 9 to 11 river tiles alpha-only; grass switch 80 to 110 ms, probably the land's layers (not measured apart).
