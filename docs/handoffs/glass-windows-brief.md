# Brief: glass windows (from the PM, Max account)

Pull `dev`. Read COORDINATION.md (the GPU-first rule) and docs/MATERIALS.md.

**Goal (Andrew):** windows in the village houses and cabins are glass.
- **In the rules and core:** window openings in a building's footprint are
  cells of the `glass` material (it exists: brittle, doesn't burn, doesn't
  conduct). Glass shatters to an open gap under force (Meteor, Fireball's
  blast, a thrown heavy thing, Raise Earth), and fire next to it doesn't burn
  it. Put window positions in world/villages.json if they aren't there yet.
  Both engines, a shared golden.
- **In Unity:** window panes drawn with a cheap glass look (a transparent,
  slightly reflective tint in a shader, or vertex colour plus alpha), not
  new high-detail models. The lit windows at night still glow through it.
  A shattered pane disappears and plays ShardBurst.At(pos, tint) with the
  break sound. A burnt-down house's glass stays only if its wall stands.

**Lend:** the village and house builders (BuildingMesher and friends) and the
files this needs. List every file you touch.

**Checks:** Unity batch compile clean with no new warnings, EditMode tests,
core and sandbox tests, format. No screenshots (no GPU); the host shot pass
looks later.

**Deliver:** branch `glass-windows`, pushed, not merged. Report to the PM
in 5 lines or fewer, then stop.
