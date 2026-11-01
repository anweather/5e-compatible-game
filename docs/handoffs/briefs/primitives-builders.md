Follow-up from the PM to `primitives-design`. Andrew: "One more idea to add in is a creature or house builder capability using the same primitives. For example, i should be able to say that all bears now have 6 legs. Or all deer are now centuars with a human top half."

Add a section to `docs/design/primitives.md` (keep the file under 240 lines; merge dev first). Creatures and buildings are made of parts, so treat those parts as primitives too:
- **Creatures:** what `CreatureSpecies`, its limbs (`LimbType`, `Fauna/Limbs/*.asset`) and `ChemYardBuilder`'s table hold today, and what's an Editor asset. Walk "all bears have 6 legs" and "all deer are centaurs with a human top half" through it: body plan, limb slots, gait and animation for extra legs, grafting one species' parts on another, hair and skin variety, behaviour. Say how far each gets and the smallest general gap. Note upstream Punchy's newer parts and that the Mac is porting them now (`docs/handoffs/briefs/punchy-life.md`): don't collide with it.
- **Buildings:** what `BuildingMesher`, the villages' plans and the cabins hold. Walk a house with a new roof, an extra storey and brick walls through it, tying to world-tools' G1 to G3.
- **Through the MCP:** a species or building as a rules row a mod patches, view only where it can be, and what stays out of the fingerprint. Which changes could reach shared state (a raider's size, a wall's cells) and how those are kept safe.
- **No named content in code (J-055, J-057).** Test: both examples asked of Claude, each ending as one mod.

Then report as the brief says, with the new primitives added to the list.
