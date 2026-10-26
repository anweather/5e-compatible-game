# D8. Voice: Vivox, with spell effects applied by each listener (2026-10-24, proposed)

**Status:** Proposed (from `MULTIPLAYER.md`, not yet accepted)

- **Decision:** Vivox for voice. Start with a positional channel; move
  to participant taps played through Unity AudioSources so the game
  owns falloff and effects. Spell voice effects are a pure function of
  the shared spell state, evaluated by each listener. Private spells
  (Message, Sending, Telepathic Bond) use separate Vivox channels.
- **Why:** free to 5,000 PCU, same UGS sign-in, and taps give the raw
  audio access that spell effects need.
- **Reopen if:** the tap spike fails (then Dissonance over NGO), or
  the game moves to Steam or EOS for netcode.
