Brief for the Mac: the Hold, phase 2, the fight. Read `docs/design/the-hold.md` (Rules for all of it, phase 2). Branch `hold-fight` off `hold-castle` (2d92c301): the castle isn't on dev yet, and its site may still move, so nothing here may depend on where it stands beyond its rows.

**Goal:** a run you can play to a loss with today's spells and controls. No hands, no upgrades, a rough endless curve.

**Build, as phase 2 says:** spawners as rows; raiders that attack wizards (5E attack roll, then dice), batter the gate and walls, and go for the keep; castle health replacing lives, on the HUD and on the castle; three adjectives (Armoured, Burning, Soaked) as rows, shown in a raider's name and look; a start plate in the castle; raiders cleared and damage mended when a run ends; the score is the wave reached.

**Also:** finish the burnt and broken look the fight now needs: walls, towers, and the colliders of stairs and decks reacting to damage.

**Keep:** the defense field working as it is, its golden unmoved. If `World.Raiders.cs` or `World.Defense.cs` must change behaviour for it, stop and say first. Core state, integer, seeded RNG, commands only (D4, D9). Numbers in the rules. Words in the glossary first.

**Check:** raiders at the far spawners still tick when every wizard is on the walls (reach). They ignore height today: say if that looks wrong on the slope. Whether a late joiner gets the run in progress.

**Tests:** core tests for each of the above and a `hold` probe scenario playing a run to a loss.

**Report:** one short message: the commit, gates with exit codes, and a numbered list for the PC, including two peers playing to a loss with no desync. Don't merge it. Ask before anything much larger than this brief.
