Needs: unity
After: `casting-leftovers` has landed (both touch attuning and the hands). Don't start before.
Area: rules/hold.json, world/maps/valley/hold.json, unity/Assets/Prototype/Chemistry/World.Hold*.cs, World.Raiders.cs, World.Defense.cs, the Hold's view (keep, heart, HUD, attuning), their tests and probes
Branch: hold-batch

# The Hold, the next batch (Andrew, 2026-11-07; given to the Mac)

From the 2026-11-06 playtest (`docs/playtests/2026-11-06-human-results.md`) and Andrew today. Design:
`docs/design/the-hold.md`. Update its "As built" as you go.

1. **Timed waves, a 10-minute run.** The next wave comes when a visible timer runs out, not when the last wave dies, so
   pushing raiders back doesn't buy time. Tune `warmup`, `betweenWaves` and the wave count so a run to the last boss
   takes about 10 minutes. Show the countdown on the HUD and keep the "what's next" warning before each wave.
2. **The keep's health.** Too high today: he let raiders hit it and nothing much happened. Tune it so neglecting the
   keep loses the run around the middle waves. Measure with the two-peer bot runs (they hit the 40 min cap today).
3. **Raiders attack something real: a crystal at the heart.** Today raiders reach the top, walk into the building and
   attack the air. Put a crystal in the middle of the keep at the `heart` join. Raiders path to it and strike it, and
   the strikes take the keep's health. The crystal **cracks visibly** as health falls (a few stages, with light and
   sound), and shatters at 0.
4. **Attuning moves to the towers, one element per tower.** Each tower is its own attuning point for one element, and
   the ground round each shows its element (embers, puddles, stones, wind-blown leaves, or whatever reads clearly from
   the walls). The keep's attuning room goes. A hand still attunes the way D16 says (`attuneTicks`), just at a tower.

Rules: shared state through the core (D4), with C# tests and goldens updated only where behaviour is meant to change
(say which). Numbers live in `rules/hold.json` or the map's `hold.json`, not as constants. No sandbox port.

Before landing: EditMode N/N (the Mac's 8 ApiKeyTests are known), `dotnet test core`, the Hold probes, a two-peer run
with no desync, a bot run's length, 480 px shots, and a ≤30 s 720p clip with a poster
(`unity/Builds/demo-videos/hold-batch.mp4`) showing the wave timer, raiders striking the crystal as it cracks, and a
tower attuning. Push the branch only. Remove this claim file on the branch in the last commit. The lead lands it.
