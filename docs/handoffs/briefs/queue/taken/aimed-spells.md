Needs: unity
After: `mcp-controls-text` has landed (it touches SpellCaster.cs). Don't claim this before.
Area: unity/Assets/Prototype/Scripts/AimMarker.cs, unity/Assets/Prototype/Scripts/SpellCaster.cs, unity/Assets/Prototype/Scripts/SpellForces.cs
Branch: aimed-spells

# Aimed spells that land, and air spells that do something (D16, 2026-11-06 playtest)

From `docs/playtests/2026-11-06-human-results.md` and the table in `docs/design/unified-casting.md`:

- **Lock-on:**
  - Lightning auto-targets the nearest enemy near the crosshair.
  - Ray of Frost and the other aimed spells pull a little toward one.
  - It's a view helper that picks the target cell. The core still gets an ordinary cast, so lockstep is unaffected.
- **Pitch:** every aimed spell can aim up and down, so flyers can be hit. Lightning has none today.
- **Gust of Wind pushes raiders back** and can knock one off the cliff, with diminishing returns (once or twice per
  raider). If the push is shared state, it goes through the core as a command, never physics (D4). Today it seems to
  do nothing.
- **Whirlwind** visibly lifts and throws, or the report says it should be cut from the table.

Before landing:
- EditMode.
- A Hold probe that hits a flyer with Lightning and pushes a raider with Gust.
- Before/after shots at 480 px.
- A demo clip.

Land with `land.sh here --compiled`.
