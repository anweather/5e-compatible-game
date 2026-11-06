Brief from the PM: prove proximity voice with canned audio, so Andrew's run tomorrow only judges how it sounds (D8, 0.1.12). Frugal mode: no sub-agents, no clips, a report of a few lines. Replaces `vivox-pc-run.md`.

When: on the PC after `castle-seat` lands and `Builds/PcDev` is rebuilt, before the phase 5 UI brief. One Unity job at a time. Run in players, never the hidden Editor (`vx_uninitialize` held its main thread 12 minutes).

Start: `vivox-spike` (8f44eae9) is 197 behind dev. Merge dev into it first. `VivoxSpike.cs` header has the arguments. On the Mac, injection wasn't heard with input "No Device", so this needs a machine with a real capture device: the PC.

1. **A voice clip.** Make about 30 s of plain speech with Windows' own speech synthesis (System.Speech), 16-bit PCM mono, the rate Vivox's audio injection takes. Keep it under 2 MB; it ships nowhere but dev tools.
2. **A voice in the woods.** A spike argument that makes a player a beacon: it joins the positional channel, stands at a fixed spot in the woods about 60 m from the yard spawn, injects the clip on a loop (Vivox audio injection), and plays nothing out loud itself. Name the spot in the report.
3. **Proof without a person.** A second player is the listener, set by argument at 0, 8, 16, 24, 32 and 40 m from the beacon. Record what it actually plays (WASAPI loopback of the output device, or Vivox's own render level if it reports the mixed level; not the sender's energy, which doesn't fade). Pass: the level falls with distance as `Channel3DProperties` says, nothing past audible distance (32 m), and it rises again when the listener walks back. A table of level against distance.
4. **Also answer:** does the beacon's injection leave the Arctis free for the listener's mic and for Whisper (H)? Does leaving take under a second? Do `vivoxsdk.dll` and `VivoxNative.dll` load in a Windows player?
5. **Leave it ready for Andrew:** one command that starts the beacon, then his normal `Builds/PcDev`. Write the exact steps into `docs/playtests/2026-11-07-human-plan.md` under V1 to V3 (replace the TODO).

Push `vivox-spike` only. Report: the commit, the table, an answer to each in 4, the command, and how big the real feature is from here.
