Brief from the PM: finish the Vivox spike with a real mic on the PC (D8, 0.1.12). Frugal mode: no sub-agents, no clips, a report of a few lines.

`origin/vivox-spike` (8f44eae9) answered sign-in and libraries. On the Mac, Vivox's audio thread stalled opening the capture device, so these are open. Spike arguments are in the `VivoxSpike.cs` header. Run in a player, never the hidden Editor: `vx_uninitialize` held its main thread 12 minutes.

1. Does Vivox open the Arctis on Windows, and how long does it take?
2. With a channel joined, does holding H (Whisper) work? Which side owns the mic, how do we hand over, and does the other side hear what's said to Claude?
3. Does a voice fade with distance as `Channel3DProperties` says (audible 32 m)? Second participant: a second player on this PC, or the Mac.
4. Do `vivoxsdk.dll` and `VivoxNative.dll` load in a Windows player, and does leaving take under a second?

Andrew speaks for 2 and 3: leave the player ready and give him exact steps. Push `vivox-spike` only. Report the commit, an answer to each, and how big the real feature is.
