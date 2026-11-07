Needs: unity
Area: unity/Assets/Prototype/Scripts/SpellCaster.Hands.cs, unity/Assets/Prototype/Scripts/SpellCaster.Combos.cs, unity/Assets/Prototype/Scripts/PlayerControls.cs, unity/Assets/Prototype/Scripts/CastHud.cs, unity/Assets/Prototype/Scripts/SpellListMenu.cs, unity/Assets/Prototype/Scripts/MenuControls.cs, unity/Assets/Prototype/Scripts/ControlsMenu.cs, unity/Assets/Prototype/Scripts/CastBeats.cs
Branch: unified-casting-view
After: `unified-casting-core` and `hold-ui` have landed (both touch the hands' input and HUD). Don't claim this before.

# Unified casting, the view half (D16)

The design is `docs/design/unified-casting.md` and the decision is D16. The core half gives hands a charge and a merge.

- **Controls:** use the doc's controls table.
  - The pad's element layout goes, and hands run everywhere.
  - Keyboard:
    - The clicks are the thrusts.
    - Q and E are the throws.
    - Click plus Q or E is the sweep.
    - Hold to charge, release to fire.
    - B cancels.
  - The F2 debug layout keeps the radial.
- **Icons:**
  - One per hand, where the element orbs are now: the element's glyph, with a mark for the gesture (a short burst, a
    streak, a ring) from the moment the gesture starts.
  - A charge ring fills in three steps, on CastBeats' rumble beats.
  - The merge: the two icons fly together and slam into the pair's glyph, with a beat.
  - Cooldown greys the icon. A locked gesture shows a lock and buzzes.
  - HoldShow's "Left / Right" line goes, if `hold-ui` hasn't removed it already.
- **The spell list:**
  - Tab and d-pad ↓ open the same list.
  - It shows the two hands first, where you attune (a short moment, not mid-cast), then the hand table for what's
    attuned, then the kit and discoveries.
  - The spell selector is hidden during the Hold.
- **The menu by keyboard:** every page takes arrows or WASD and Enter (`MenuControls.cs:37-40` is pad-only today).
- **The controls page** (`ControlsMenu.cs`) and the rules MCP's reach text (`Workbench.Draft.cs` `ReachOf`, after
  `mcp-controls-text` lands) describe the hands, not the radial.

Before landing:
- EditMode on your tip.
- The probes you touched.
- A two-peer run with different hands and no desync.
- The demo clip rule: one clip of a charge, a merge, and one hand rapid-firing while the other charges.
- Shots checked at 480 px.

Land with `land.sh here --compiled`.
