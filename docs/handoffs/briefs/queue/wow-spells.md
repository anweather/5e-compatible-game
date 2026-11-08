Needs: core
Area: rules/spells.json, rules/hold.json (hands rows), rules/looks.json if needed (through the rules MCP), the core's
spell behaviour with C# goldens; no view code (the PC does looks and feel after)
Branch: wow-spells
After: casting-taps lands (both touch rules/hold.json hands)

# A big spell for every element (J-078; given to u2)

Andrew (J-078): the wow spells take the same-element throws, so Stone Ramp and Falling Star go. Read
`docs/design/casting-pace-options.md` "A Meteor for every element". Each costs what Meteor does (its cast, cooldown or,
later, mana), so Meteor becomes one of four rather than weaker.

- **Tornado** (air+air throw, the free slot): Whirlwind grown huge and lasting; a waterspout over the lake, a fire
  tornado over burning ground (its `carries` rows exist); lifts and flings raiders (`LiftInTheEye`).
- **Thunder Strike** (fire+air thrust, in place of Falling Star): a column from the sky, `lightning` plus `thunder`,
  using the reactions that exist (cloud to storm, conducting through water and metal, fusing sand, shattering ice,
  clearing gas).
- **Earth Spires** (earth+earth throw, in place of Stone Ramp): a jagged line of pillars toward the target
  (`earth-raise` makes `pillar`), throwing raiders up and leaving cover; an earth spell breaks them to rubble.
- **Iceberg** (the water+water throw, or the water slot that fits; Andrew's own idea, "similar to Frozen Tide"): a huge
  mass of ice that rises out of water (or out of a pool it makes) and crashes forward, freezing what it hits and leaving
  walkable ice; fire melts it into a flood. Propose the exact shape in the report if the rules allow more than one.

Balance them against Meteor with a test like spell-balance's (damage per raider and reach on a packed field). Goldens
for any new core behaviour. Before landing: `dotnet build core -c Release -warnaserror`, `dotnet test core` twice in
full, `dotnet format`. Push the branch only, remove this claim on the branch in the last commit. The PC does the looks
and compiles it; the lead lands it.
