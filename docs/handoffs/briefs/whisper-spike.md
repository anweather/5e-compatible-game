Brief from the PM: a Whisper spike for in-game dictation (#58). Andrew's go, 2026-11-03.

Why: the game's dictation uses Windows' recogniser, which waits on Microsoft's online speech service; on Andrew's PC it never answers (no text, then a 20 s freeze on the next press). Win+H works through a different path. We can't fix Microsoft's side, and Windows dictation never worked on the Mac.

Goal: find out whether local Whisper (whisper.cpp through the MIT `whisper.unity` package, or the leanest equivalent) can replace it. Branch `whisper-spike` off the latest origin/dev; push the branch only, never dev.
1. Push-to-talk records from the default mic into Whisper, and the text lands where dictation puts it today (same callers, same events), behind a switch, so the old path stays until Andrew picks.
2. Transcription off the main thread; no frame hitch. A timeout we control, so no freeze ever.
3. Measure on the Mac (and say what to expect on the PC's GPU): load time, seconds per short phrase for the tiny.en and base.en models, frame time while transcribing, memory, and the player download added by each model. Note the licence of every file added.
4. Ten spoken test phrases of the game's own kind (spell names, "make the deer three times bigger", a 5E term or two): the words heard, per model.
5. Leave a `-voice-check`-style command so Andrew can try it on the PC with his Arctis in two minutes.
Report: the commit, the numbers table, the phrase results, the recommended model, what a real switch would still need (Windows build, model download or bundled, settings). No core change; determinism unaffected (text goes in the way typed text does).
