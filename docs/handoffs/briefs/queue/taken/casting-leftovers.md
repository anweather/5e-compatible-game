Needs: unity
Area: unity/Assets/Prototype/Scripts/SpellCaster*.cs, the hands HUD and its icons, unity/Assets/Prototype/Scripts/Playtest/PlaytestPlayer.cs, the Probe*.cs that call LoadElement, unity/Assets/Prototype/Chemistry/World.Combos.cs
Branch: casting-leftovers

# Unified casting, the leftovers (D16; Andrew, 2026-11-07; given to the Mac)

What D16's landing (3e62bee1) left, from `docs/STATE.md` and `docs/design/unified-casting.md`:

1. **E is both the right throw and pick-up.** Pick-up wins in reach, so you can't throw right-handed near an item.
   Move pick-up to its own input: **F** on the keyboard, and a controller face button nothing else uses (check the
   table in the design's controls section). Update any on-screen prompt that says E for pick-up.
2. **One glyph per merged pair.** The merged icon is a generic two-tone disc today. Give each pair its own glyph, as
   the design's "The merge" says (Lightning a bolt, Steam a cloud, and so on for every pair in `rules/combos.json`),
   with the icons flying together into it. Glyphs are drawn in code or from one atlas; no per-pair asset packs.
3. **Retire the element path if it goes cleanly.** `LoadElement` and the ordered `pairs` stay only for the bot
   (`PlaytestPlayer.cs`) and old probes. Move them to hands-based casts. Remove the core path only if no golden or
   `CellHash` moves. If one would, stop at moving the callers and report what moved.

Rules: the view never decides shared state (D4); casts still go through the normal core commands. No rules changes.

Before landing: EditMode N/N (the Mac's 8 ApiKeyTests are known), `dotnet test core`, the `handlooks` and `airpeers`
probes, before/after shots at 480 px of the merged glyphs, and a ≤30 s 720p clip with a poster
(`unity/Builds/demo-videos/casting-leftovers.mp4`) showing a right throw near an item and three different merges.
Push the branch only. Remove this claim file on the branch in the last commit. The lead lands it.
