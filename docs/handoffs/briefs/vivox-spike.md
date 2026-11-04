Brief from the PM: a Vivox spike for proximity voice in 0.1.12 (D8, accepted; `docs/MULTIPLAYER.md` §4). Frugal mode: no sub-agents, no clips, a report of a few lines. Andrew has turned on Voice and Text Chat for the project in the Unity Dashboard.

Branch `vivox-spike` off `origin/dev`. Push that branch only, never `dev`. Nothing here goes in 0.1.11.

Answer these, smallest code that shows it:
1. **Sign-in:** add `com.unity.services.vivox` and sign in with the `NetSession` session's existing UGS sign-in, with no key in the repo. Does a positional channel join?
2. **The mic (the main risk):** can Vivox and Whisper's `Microphone` capture share it? Does holding H while in a voice channel work, and does the other side hear Claude's prompt? Say which owns the mic and how we hand over.
3. **Distance:** with `Set3DPosition` three times a second, does a voice fade with distance as `Channel3DProperties` says? A virtual second participant is fine for now.
4. **Libraries (J-072):** list Vivox's native libraries for Windows and whether they land in a player with nothing else to install.

Voice stays out of the core and out of lockstep: nothing in `Chemistry/`. Report the commit, an answer to each, and how big the real feature is.
