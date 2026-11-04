Brief from the PM: voice must work on a friend's PC with nothing to download by hand (J-072, Andrew). Frugal mode: no sub-agents, no clips, a report of a few lines.

Branch `voice-libs-bundled` off `origin/dev`, in `host-ux`. Push that branch only.

1. **Bundle what voice needs** so it arrives through the Velopack install and updates: MSVCP140, VCRUNTIME140, VCRUNTIME140_1 and VCOMP140 (Microsoft allows app-local copies), and `vulkan-1.dll`, placed where `libwhisper.dll` finds them. Copy them in the build step, from a pinned, checked source, the way `WhisperModelIntoBuild` does for the model. Follow the licences, and say which apply. Check the whole dependency tree yourself; don't rely only on this list.
2. **No Vulkan driver:** with the bundled loader and no GPU driver, voice must fall back to CPU. If it can't, use a CPU-only Whisper build for Windows instead, which removes the Vulkan dependency altogether. Say which you chose. Only if neither works, stop and tell me: the fallback is a download during first-run setup.
3. **Prove it on a clean Windows:** Windows Sandbox if it's available, or another machine with no dev tools, GPU driver or redistributable. Run the release player there and transcribe a WAV. This PC already has these libraries, so a pass here proves nothing.
4. **Velopack:** confirm the packed release holds the libraries and the model, not only the build folder, and give the size of the download and of an update from 0.1.10.
5. **Error line:** when a library really is missing, say so plainly, not "Try again": retrying can't help. Typing keeps working.

Report: `bash tools/workers/bridge.sh send pm "voice-libs-bundled" < report.md`.
