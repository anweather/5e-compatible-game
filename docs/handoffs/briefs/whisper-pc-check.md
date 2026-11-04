Brief from the PM: the Windows side of Whisper, for 0.1.11 (#58, J-070). Frugal mode: no sub-agents, no clips, a report of a few lines.

`origin/whisper-spike` (a524603b or later): Whisper is the default recogniser, proven on the Mac from WAV files only. Merge `origin/dev` into it in `host-ux`. Fix only what these checks show is broken, and push `whisper-spike` only.

1. **Compile:** a clean native Windows compile, EditMode, `smoke`, and `tools/probe.sh transcribe --only whisper` (Prototype > Fetch Whisper Model first).
2. **Player build:** a native Windows build must be clean. The Mac's cross-build reported 3 errors from one Asset Store shader graph (TerrainDemoScene_URP, SpeedTreeURP_Branch). Say whether they appear here.
3. **Native libs:** in that player, `libwhisper.dll` must load. It needs `ggml-vulkan.dll`, `vulkan-1.dll`, MSVCP140, VCRUNTIME140/_1 and VCOMP140. List which of these the build folder doesn't carry: that says what a friend's PC needs. Say whether the VC++ redistributable has to ship or be named in the notes.
4. **Mic:** leave the player ready, then ask Andrew by the PM's mailbox to hold H and speak on the Arctis. Report how long `Microphone.Start` and closing took, what it heard, and press timing from the play log.

Report: `bash tools/workers/bridge.sh send pm "whisper-pc-check" < report.md`.
