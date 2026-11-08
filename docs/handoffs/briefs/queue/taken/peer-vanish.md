Needs: unity
Area: unity/Assets/Prototype/Scripts/Net/ (peer bodies), the wizard's body and its visibility, LandRocks.cs, punch
feedback (the puncher's screen); core only if the bug is there (with goldens)
Branch: peer-vanish

# The other player vanishes, rocks throw, punches feel like nothing (Andrew, 2026-11-08; given to the PC)

From `docs/playtests/2026-11-08-human-results.md`:

1. **B1, the main one.** In a two-player game with no desync, the other player's body stops being drawn after a while.
   First seen after they lost a Hold run; a reset (holding Back) brings it back, but only for a while. His spells still
   charge (the sound plays) and still change the world. Reproduce it with two peers on this PC (a Hold run lost, then
   reset, then wait), find why the remote body is hidden, culled, destroyed or left at a stale place, and fix it. Add a
   probe or test that fails before and passes after. Check the "locked PC, player hardly renders" memory first: rule
   out visibility-gated drawing before blaming the net code.
2. **L1** `LandRocks.cs:123` NullReferenceException, about 147 per run, after the first land move (`rock.Body` destroyed
   but still read from `ValleyChemistry.LateUpdate`). Fix and cover with a test.
3. **B3** a punch that lands gives the puncher no feedback. Add a clear hit on the puncher's screen (a hit flash or
   spark at the fist, a short pad rumble, the hit sound), reusing what the one punched already gets where it fits.

Before landing: `dotnet test core`, EditMode N/N in full, the two-peer run passing your new check, a ≤30 s 720p clip
(`peer-vanish.mp4`) with a poster: the other player still drawn after a lost Hold and a reset, and a punch with its
feedback. Push the branch only, remove this claim on the branch in the last commit. The lead lands it.
