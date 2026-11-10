# Judgment calls

(Not to be confused with `docs/DECISIONS.md`, the architecture decisions D1, D2, ...)

Every decision the coordinator asked Andrew for. **Before you ask Andrew
anything, look here first.** If a past decision covers the question, follow
it, and add 1 to its *Revisited* count when the question came up again.
Ask again only if something has changed, and say what.

Agents: send judgment calls to the coordinator ("Project Manager"), not to
Andrew directly. The coordinator checks this log, then asks Andrew if needed,
and records the answer here.

Each entry has:
- **Asked:** the question, when, and who raised it.
- **Also comes up when:** other sessions or situations likely to hit the same question.
- **Answer:** what Andrew decided.
- **Why:** Andrew's reason if he gave one. Otherwise, the coordinator's reason
  for its recommendation that Andrew accepted, marked "(recommendation)".
- **Revisited:** how many times the question has come up again after the
  answer, with dates.

## Open

### J-021: Finer ground, five design questions (backlog #15, Later)

- **Asked:** 2026-10-27, raised by World's spike (`docs/FINER_GROUND.md` on
  `finer-spike`, never merged). It recommends option 2: finer height only
  (four 0.75 m sub-heights per cell), with water reading the mean.
- **Also comes up when:** earth shaping, trenches, the save format, cave
  shafts.
- **The questions:**
  1. Which shapes go finer? Proposed: slanted lines and round craters only.
  2. Is a half-raised cell a wall at its highest sub-square, or at its mean?
  3. Is water moving a cell at a time acceptable along a smooth channel?
  4. Should it land before saves ship?
  5. Does a shaft open when all four sub-squares reach the bottom, or when
     their mean does?
- **Answer:** pending; ask when it comes up in the order.
- **Revisited:** 0.

## Decided

### J-089: Accept D17, the sparse World? (2026-11-10)

- **Asked:** the lead, after u2's step 1 (`sparse-world-1`: the design, D17 proposed, the core's cells through chunk
  accessors).
- **Also comes up when:** the 12 x 10 loading, any change to how the World holds cells.
- **Answer (Andrew):** accept after the bench: D17 is accepted if `sparse-world-bench` shows packed hashes, saves and
  unpacking fit the tick budget; the lead then starts step 2. If they don't, the fallback (packed arrays) goes back
  to Andrew.
- **Revisited:** 0.

### J-088: A baked land.json whose compressed bytes differ by runtime (2026-11-10)

- **Asked:** the PC's compile of `bake-lean`: the streaming writer feeds the deflater a row at a time, so the nine's
  baked `land.json` decompresses to the same land but its compressed bytes differ between .NET 8 and the Editor.
- **Also comes up when:** any writer of a map file; `MapIdentity.Hash`, which hashes every file's bytes for a map
  that isn't generated.
- **Answer (the lead):** accepted. The streaming writer is used only by the bake, whose maps are generated, and a
  generated map's identity is its inputs (`GeneratedMap.InputHash`), never its baked files. Committed maps keep
  `LandFile.Write`. If the streaming writer is ever used for a committed map, its bytes must be made stable first.
- **Revisited:** 0.

### J-087: Restart the PC's Editor when its memory runs away? (2026-11-09)

- **Asked:** the lead, after the editor-lean trial found the Editor (up 20 h) holding 49 GB of private memory
  (26 GB native), with commit at 92 of 102 GB and jobs killed for low memory.
- **Also comes up when:** any low-memory kill, a long session, #145.
- **Answer (Andrew):** he restarts it himself; the lead asks when it passes about 20 GB, with the tree clean and
  the branch pushed.
- **Revisited:** 0.

### J-086: What a rule switch and a look change may touch (2026-11-09)

- **Asked:** Andrew, on why a bigger world slowed spells when only the cells near players are active. The lead:
  the tick is bounded by reach (1.2 ms on the nine), but code outside it walked the whole map: a rule switch
  sanitized every cell, a look change redrew every chunk.
- **Also comes up when:** any new system that reacts to a switch, a mod, weather or a save; the 12 x 10's sparse
  world; anything that loops over `World.Cells` or every chunk.
- **Answer (Andrew):** a rule switch affects active cells now, and every other chunk when it activates. A look
  change affects only chunks being drawn; a chunk drawn later takes the current look then. Cost follows the active
  region and the drawn tier, never the map's size.
- **Doing:** core half to u2 (`rule-switch-by-chunk`, #190); view half on the PC with #185 (#186).
- **Revisited:** 0.

### J-085: Same biomes side by side: the patchwork (2026-11-09)

- **Asked:** Andrew: why can't similar biomes sit side by side? Two or three forests together, or a big desert,
  would quilt less. The lead: the spacing rule (J-080: no two copies of a square template within 3 world squares,
  each repeat weighed down) is about templates, but each template carries one anchor biome and there are 13, so a
  big one-biome region can't fill (the world sketch paper measured it: 41% of drawn world squares got their biome).
- **Also comes up when:** template library size, world sketch in the core, latitude bands, any "looks like a quilt"
  report.
- **Answer (Andrew):** split biome from template. The biome comes from the world sketch and latitude across world
  squares, so forests and deserts can span many; the spacing rule stays, for a template's village, landmarks and
  layout only. Queued for u2 as `biome-from-sketch`, after `world-12x10-plan`.
- **Revisited:** 0 (J-080's repeat rule narrowed, not reversed).

### J-084: Start the 12 x 10 world's first proof now? (2026-11-09)

- **Asked:** start the cheap proof (u2: the 12 x 10 EW plan and a known ring over every chunk; the PC: three
  distance dials and a sweep probe on the nine), ahead of maps phase 8's view half?
- **Also comes up when:** ordering world growth against maps phase 8 and mana.
- **Answer (Andrew):** start both now. u2 takes `world-12x10-plan` after `drowned-chain-core`; the PC does
  `tier-dials` now; phase 8's view half waits.
- **Revisited:** 0.

### J-083: The 12 x 8 world's rows (2026-11-09, `docs/design/world-12x8-scope.md`)

- **Asked:** does "8 tall" count the ice cap and desert edge rows?
- **Also comes up when:** sizing any generated world, writing a map.json `squares`, the plan's wall rows.
- **Answer (Andrew):** 8 land rows plus an ice row north and a desert row south: 12 x 10 world squares in all,
  the way `PlanGrid` already adds wall rows.
- **Revisited:** 0.

### J-082: Wait for Andrew's playtest of the nine? (2026-11-08)

- **Asked:** Andrew told the lead, unprompted: "I don't need the Windows build for playtesting yet. Keep working
  without my playtest."
- **Also comes up when:** any step in STATE's order that ends in "Andrew plays it".
- **Answer:** don't build `Builds/PcDev` for him and don't block on his play. Unity-task walk checks and clips stand
  in for the playtest; the order goes on (nine-slopes, then the far ring on paper, then the deferred items).
- **Why:** he'll ask for a build when he wants one.
- **Revisited:** 0.

### J-081: An outside architecture review (2026-11-08)

- **Asked:** Andrew pasted a review of the repo's architecture, CI and tests.
- **Also comes up when:** splitting assemblies or test projects, refactoring the core, CI speed.
- **The lead's triage:** kept the review's "keep" list (engine-free core, rules as JSON, lockstep, goldens). Taken for
  u2 now as `core-boundaries`: CI triggers and caching, test projects split by boundary, Unit/Simulation categories,
  World.React's Apply split (goldens byte-identical), dependency directions in ARCHITECTURE.md. Deferred until after
  the 3 x 3 playtest: a runtime asmdef for Scripts (the PC's compile time; measure first). Dropped after the
  reviewer's second look at dev: splitting World.Sim's passes (keep its stages, slices and order; SlicedTickTests and
  TickStageBench already guard them). No weekly CI review (Andrew: "let's not"). Already done: the sandbox is frozen (2026-10-31); a Sweep category exists (test-speed-core).
- **Weighed:** CI takes 4.5-5 min and isn't the bottleneck (the PC's Editor is), so CI changes are cheap tidying;
  the test split and React refactor help agents most (smaller things to read and test).
- **The lead, 2026-11-09, on u2's two leftovers:** step 4 (a `Chemistry/Reactions/` folder) is closed as done:
  react-split already split Apply and a cold build has no S3776. S3776 stays in `WarningsNotAsErrors` (a parallel Mac
  build can still raise it; CLAUDE.md's `-m:1` note).
- **The lead, 2026-11-09, the Scripts asmdef measured and dropped:** a Scripts-only change recompiled in 12.7 s
  (median of 3) before and 11.9 s after one runtime asmdef (6%; the bar was 30%). EditMode 1862/1862, smoke and online
  passed with it, but the new assembly holds 505 of 506 files, so nothing is saved. Not landed. A real gain needs
  several smaller assemblies (Net or the Probe* files apart), a bigger refactor nobody has asked for. Note: a yard
  rebuild under a new asmdef rewrites NetWizard.prefab's `m_EditorClassIdentifier` lines.
- **Revisited:** 0.

### J-080: The world squares (playtest 2026-11-08 follow-up, `docs/design/world-squares.md`)

- **Asked:** 2026-11-08, by the lead; answered on https://claude.ai/artifact/SU4mkw9cWLdtgzjz5iDApQ (`picks`).
- **Also comes up when:** the world generator, map folders, saves and thing ids, streaming, landmarks, the view's far
  distance.
- **Answer:** Andrew:
  - **Nine folders, generated at load:** "Let's try at load first. If too slow, we'll look into other options.
    Ideally it happens not all at once, but only when needed."
  - **North of the valley takes negative chunk rows,** so the valley keeps its ids and saves.
  - **One curated world now, a library next,** "but want to make dynamic later with a seed at the start to load".
  - **Repeats allowed, weighted down,** "but make sure they are at least 3 squares away". No repeats in the 3 × 3.
  - **A stand-in great tree** now; the face is art later.
  - **The 3 × 3 is a proof of concept.** The goal is "this modular puzzle solve that's building and dynamic at the same
    time": generate a new chunk on demand when someone comes within line of sight of an existing one.
  - **See big landmarks far away** (Punchy does this well): pre-select about **five chunks out** from the active area
    (not past a hard edge such as the ocean): which biome, its height and elevation profile, and its landmarks. Draw
    that far ring at low detail behind a light fog, and solve each chunk fully as you get closer.
  - **The ocean can hold landmarks:** an ocean biome might be nothing but a great rock spire you can travel to.
  - **Stretch goal, keep in mind:** the world wraps around as a torus (a doughnut, so top and bottom wrap too), with a
    configurable size, "for now let's say 12 of those wide" (12 valley-sized squares, by the lead's reading).
- **Why:** his; the on-demand, far-ring and torus parts change the plan from baking to solving deterministically on
  every peer as the world is explored.
- **The revised plan's three calls (same page, same day):** the whole world plan solved at load on every peer (not in
  blocks); the square folders on disk as a cache in the player's data folder; generated at `sight` 4 chunks and known
  at `know` 7 (about 670 m of haze). All three as recommended.
- **Later the same evening, to the Mac:** coastlines are part of the edge puzzle with rivers, roads, ranges and canyons
  ("make sure the coastlines are consistent": a coast profile per side, matched across sides and corners); and each
  landmark form gets its own layout ("the layouts should be unique": the forest keeps its clustered square, the
  mountain village climbs the mountain in a line, the lake village curves round the shore; the Mac's
  `landmark-intent-fixes`).
- **Solver rules for rivers and roads (to the lead, same evening):** rivers flow from high to low elevation; roads lead
  somewhere and span at least three world squares ("a road that starts in one section and ends in the next isn't
  really that useful"); roads avoid big height changes ("no roads going up"); and roads get scenic outcrops along them
  ("something interesting if you're walking out there").
- **Roads have a reason (after the paper prototype):** points of interest before roads; at most about six main roads
  per world, long, apart from each other, fenced and marked, with waystation shops; lesser roads and trails off them to
  caves or between small towns; mountains are gone round; a canyon has only one or two bridges and roads route to them.
  "Dynamic but intentional", with the tiles keeping the road interface.
- **Inland seas and world archetypes (to the lead):** add large inland seas; then "generate a world based on an [LLM]
  prompt with different constraints", let a few rounds pick the best on some criteria, and save its seed as the
  archetype for that description, to regenerate easily. The lead's additions (plan, "World archetypes"): Claude sets
  constraints, never the world; score on numbers first, a judge picks, Andrew decides; save the solved plan and the
  generator version with the seed, since a seed alone drifts when the generator changes.
- **Oceans, continents and latitude (to the lead):** oceans are a feature too, large ones on the edges, so the whole
  world can be an island, or several continents wrapped round. The world wraps east-west only; north and south are firm
  edges of impassable ice mountains. Temperate and desert in the middle latitudes, blending north from the desert to the
  northern mountains and south to the southern mountains. (This replaces the torus as the default; the torus stays an
  option.)
  Then, after the prototype: the polar regions blend into the biomes beside them, with the same blending rules as
  elsewhere; where ice meets sea, a jagged ice cliff, not a smooth wall; where the desert edge meets sea, a sandy beach
  with a sandstorm just beyond it to the south.
- **The volcano village (to the Mac):** no houses up at the peak. Houses start at the mountain's base and go up "like a
  corridor", a road winding round the mountain with houses on its upslope and downslope sides, "but they shouldn't be
  so steep". The Mac's "wind" layout: a gently rising road part-way round the cone's lower slopes, houses alternating
  sides, nothing above about a third of the cone's height (landmark-look-pass).
  Then: "the hill is too steep to put houses on ... ground leveling before we put houses in ... part of the village
  stamp ... terraces on the steep mountain." The Mac grades each house's pad and the winding way into terraces.
  Or, his alternative, taken: "like Kakariko Village in Ocarina of Time ... a village at the base of the mountain, and
  the path out of the village is the road leading up the mountain." A levelled shelf at the foot with the houses on
  it, one winding, graded trail out of the village up toward the crater, no houses on the steep cone.
- **The big spells:** "way better now ... good enough for us to move ahead with" (wow-looks, 44a8103a).
- **Spawn points (2026-11-08):** "Let's add more spawn points in the 3x3 too." The lead's reading: one spawn per
  square from the plan (on a road, near its inn, village or landmark), a Host "Start in" choice, joiners beside the
  host, a waystone at each, respawn at the nearest one reached. u2 in chunk-gen-polish step 5, the PC in play-nine
  step 6.
- **A hung Editor (2026-11-08):** play-nine's Editor (pid 44784) froze for 20 min after a whole-nine overhead
  render (D3D12 fence lost, 14 GB). Andrew: yes, end that pid. One-off; the rule "never kill an Editor" stands, and
  overhead shots stay to one or two squares at a time.
- **Revisited:** 0.

### J-078: Casting pace and big spells (playtest 2026-11-08, C1 and C2)

- **Asked:** 2026-11-08, by the lead, from `docs/design/casting-pace-options.md`; answered on the decision page
  (https://claude.ai/artifact/3gArUafzNLv9yAdMjJyZu3, its `picks` collection).
- **Also comes up when:** any cast timing, cooldown, mana or upgrade change; a new spell; balance passes; the HUD.
- **Answer:** Andrew:
  - Option **C**: free taps, and mana pays for charging and merging, built as A (tap is quick, hold is the charge) first.
  - A tapped spell keeps a **tiny** cooldown, just enough that one button can't machine-gun.
  - The wow spells take the **same-element throws**: Stone Ramp and Falling Star go. Tornado (air+air throw), Thunder
    Strike (fire+air thrust), Earth Spires (earth+earth throw), Meteor kept.
  - Ice: **"Iceberg. Similar to frozen tide."** (his own idea, beside Frozen Tide).
- **Why:** the recommendation, except the ice spell, which is his.
- **Revisited:** 0.

### J-079: The nine-valley world (playtest 2026-11-08, W1-W4)

- **Asked:** 2026-11-08, by the lead, from `docs/design/world-nine-options.md`; answered on the decision page.
- **Also comes up when:** maps phases 6-9, the biome port, start-up time, a second map, the desert Hold.
- **Answer:** Andrew:
  - **Anchors:** he names an anchor biome per square and the climate draws the borders and the variety inside.
  - **A curated 7 biomes** (Temperate, Grassland, Mountains, Tundra, Salt Flat or Sonoran, Foggy Marsh, Ashlands or
    Volcano); the other rows stay in the file, off.
  - **Generate at start-up or ahead of time: not picked.** His note: "How quickly can we generate on demand? I'd love to
    explore that." So measure on-demand generation per chunk before deciding.
  - **Thread the world rebuild: yes**, with a determinism test on the PC and the Mac (the core's first threading).
- **Why:** the recommendations; the generate question stays open until on-demand speed is measured.
- **The aim, in his words (later the same day):** on-demand loading isn't required, "but it will be interesting to
  see". The balance he's after: "something that looks curated and organic" and something "more dynamically loaded
  [that] drives joy when you go to it because it's unexpected without being something that [is] all stamped out." Read
  every world choice against that: curated anchors and landmarks, organic climate between them, and surprises that
  don't repeat like a stamp.
- **Revisited:** 0.

### J-077: Who coordinates the workers?

- **Asked:** 2026-11-06, by Andrew, after the cost report showed coordination at half of all spend.
- **Also comes up when:** a new worker or machine, a long PM session, a landing, feature intake, the board.
- **Answer:** Andrew: workers land their own branches with `land.sh auto` and take briefs from a queue; the backlog-groom agent (Sonnet) does feature intake; the PM is a short session only for planning, decisions and releases; the board only with a decision (D15).
- **Why:** PM cost follows the clock; routine steps shouldn't pay for a day of context.
- **Revisited:** 0.

### J-076: How is proximity voice proved before a person tries it?

- **Asked:** 2026-11-06, by Andrew, when the PM asked whether 0.1.12 waits for a live Vivox run with him.
- **Also comes up when:** any voice, sound or audio feature; anything that seems to need a person to listen.
- **Answer:** Andrew: prove it first with canned audio (Vivox audio injection from a bot) and a measured fade with distance; he then judges by ear in his next playtest, walking toward a fixed voice in the woods.
- **Why:** the machinery can be checked without him; his time goes on whether it sounds right.
- **Revisited:** 0.

### J-075: How do a hand's buttons work?

- **Asked:** 2026-11-05, by Andrew, after the first hands were built with the bumper as a modifier.
- **Also comes up when:** weapons, new spells, pad layout, an unattuned hand.
- **Answer:** Andrew: the button is the gesture. Trigger short range, bumper long range, both an area; an empty hand punches, throws or shoves. The same gesture on both hands makes something new; different gestures each do their own, and the chemistry decides. Spells stay unique, never re-skinned. Weapons fit later under the same rule.
- **Why:** one rule you can feel, and mixed gestures cost no table entries.
- **Revisited:** 0.

### J-074: What's the first game loop, and how do wizards cast in it?

- **Asked:** 2026-11-04, by Andrew (#152): the game has mechanics but no objective.
- **Also comes up when:** progression, roles, new modes, the second map, sandbox controls.
- **Answer:** Andrew: real-time wave defence, the Hold, in the valley for now, on the hill west of Hollowmere. Castle first, as a set piece from primitives. Endless waves. An element per hand, changed only in the keep, with a kit on top (`docs/design/the-hold.md`).
- **Why:** chemistry decides the fight, it needs the least content, and staying in the valley avoids waiting for maps. Hands keep combining and make co-op roles.
- **Revisited:** 0.

### J-073: Maps as data: the design's six questions

- **Asked:** 2026-11-04, by the Mac's design (`docs/design/maps-as-data.md`).
- **Also comes up when:** terrain through the MCP, a second map, the dragon, game modes.
- **Answer:** Andrew: land is built at run time. A mountain doesn't stop raiders, sight or spells yet. A live stamp is at most radius 32 cells. Sea level is set per map for now, with no stamps across water or live water level. The host picks the map. The dragon's reset undoes stamps, but the automatic dragon goes off: "Would feel really bad to build something then have it deleted."
- **Why:** the design's reasons; building has to feel safe.
- **Revisited:** 0.

### J-072: What may a player have to install for voice?

- **Asked:** 2026-11-03, by the PM: Whisper needs the VC++ runtime and Vulkan, and neither was in the build.
- **Also comes up when:** any native library; the installer; other platforms.
- **Answer:** Andrew: nothing by hand. "Making someone [download] a library is not an acceptable option." Bundle it with the application through Velopack, or download it as part of first-run setup. Also: the model list stays Haiku and Opus; no Sonnet row.
- **Why:** an install has to just work.
- **Revisited:** 0.

### J-071: Bring your own API key in 0.1.11

- **Asked:** 2026-11-03, raised by Andrew: players set their own Anthropic API key in Settings.
- **Also comes up when:** the release notes; storing any secret; Mac and Linux players.
- **Answer:** Andrew: yes, in 0.1.11, so people can bring their own keys. PM's default: encrypted per user with DPAPI on Windows, never in PlayerPrefs, and checked in Settings with `GET /v1/models`.
  Settings also offers Claude Code by path (default `claude`) with environment variables for a separate instance; Claude only for now, other providers later. Plus a "make a separate key" line and a per-session spend cap.
- **Why:** players pay for their own use, and the game holds no shared key.
- **Revisited:** 0.

### J-070: Which voice goes in 0.1.11?

- **Asked:** 2026-11-03, by the PM after Andrew said he wants voice in 0.1.11: talking to Claude (Whisper), proximity voice (Vivox), or both.
- **Also comes up when:** any release's voice scope; Vivox and the Unity Gaming Services setup.
- **Answer:** Andrew: talking to Claude first, using Whisper. Vivox comes later. The Whisper downloads are cleared.
- **Why:** Windows dictation is broken (#58), and Whisper's seam is already built. Vivox isn't started and needs his setup.
- **Revisited:** 0. J-061 said Windows first; this moves Whisper ahead of it.

### J-069: Cleanup item 4 and a gesture for walking rules

- **Asked:** 2026-11-03, by the PM, after the Mac found item 4 needs a game assembly first, and after the triggers-view clip.
- **Answer:** Andrew: drop cleanup item 4 (probes stay in Assembly-CSharp). No casting gesture when a walking rule fires, at least for now.
- **Why:** item 4 cost player size, not behaviour, and the full fix is days across every view file. The cast's own show reads well enough.
- **Also comes up when:** a game asmdef or PlayMode tests are proposed; trigger views.
- **Revisited:** 0.

### J-068: Made-up bodies before gaits by rule?

- **Asked:** 2026-11-03, by the PM, from `docs/design/bodies-and-surfaces.md` §2.
- **Answer:** Andrew: bodies first. Grafted bodies ship while legs still move by their own poses, so a centaur's torso bobs with the deer's back and a faun's hoofed legs step like a deer's hind pair, until gaits are by rule.
- **Why:** gets custom creatures into his hands sooner; gaits fix the motion later without changing the rows.
- **Also comes up when:** any graft, or the gaits-by-rule work (`builders.md` §3).
- **Revisited:** 0.

### J-067: Six design calls from the play test of 2026-11-02

- **Asked:** 2026-11-03, by the PM, from `docs/design/events-and-placing.md` and `bodies-and-surfaces.md`.
- **Answer:** Andrew: yes to all six recommendations. A summoned creature is alive and wanders (a still thing is possible later). A mod decides whose steps fire a trigger, the asker's by default. Triggers wait at least 3 ticks between firings, at most 4 a tick in a room. A centaur keeps each half's colours. Each dock row says what happens when the water drops, on posts by default. A raft is a floating thing you stand on, built after the dock.
- **Why:** the smallest general choices that make his asks work, each leaving the other option open.
- **Also comes up when:** any new trigger, summon or water-surface primitive.
- **Revisited:** 0.

### J-066: Which model does the in-game chat use?

- **Asked:** 2026-11-02, by the PM, after #59 change 3 (`docs/telemetry/chat-context-2026-11-02.md`).
- **Answer:** Andrew: Haiku by default. No fallback: an unavailable model fails loud and says where to change it, because a retry on another model spends money nobody asked for.
- **Why:** with the context fix it passes the strict check at about 1/35 of Opus's list price and no slower. Plainer replies, and
  one cantrip in four comes out a point spell.
- **Also comes up when:** the harness and API picker (backlog 27), anything measuring chat quality.
- **Revisited:** 0.

### J-065: Six open calls, cleared with Andrew

- **Asked:** 2026-11-02, by the PM, one at a time.
- **Answer:** (1) The terms: see J-060's update. (2) Release builds keep voice timing and drop the words (#53). (3) The Esc menu
  loses Same network Host / Join, Frame-rate log and Open log folder; Headwear stays. (4) A camera cutting through a deer is left
  as it is (#47). (5) The sound pass waits for him to listen on the board; nothing goes in before that. (6) The eight fully merged
  branches go; `wip/u2-mods-add-things`, `pause-menu`, `wip/w2-playtest-rules-a`, `wip/w2-whirlwind-pull` and `forest-tick-cost` stay.
- **Update:** the per-prompt chat record keeps prompts, spoken or typed, but moves away from Player.log, so sending a log doesn't send it.
- **Update:** cutting Frame-rate log removed the play log's off switch, so Andrew put one back: a "Play log" on/off in Settings, honouring old opt-outs.
- **Why:** PM's recommendations, except the menu, his picks.
- **Also comes up when:** a friend needs to send a log: with no menu entry, tell them the folder.
- **Revisited:** 0.

### J-064: Real Claude for automated chat runs; text apart from voice

- **Asked:** 2026-11-02. Andrew told DesktopUnity directly; relayed to the PM, and in his commit 344a17c.
- **Answer:** automated headless runs through the game on his own signed-in
  Claude are allowed, for chat speed and token work. Signing in stays his alone.
  Text and voice are separate features, with Microsoft's pre-canned voices for now,
  so chat tuning doesn't wait on #58.
- **Why:** it's the same account, and "I can't just sit at the computer all day".
- **Limits (PM):** a fixed prompt set of at most 8 per run, each report says how
  many prompts it spent, and no runs once the weekly limit passes 85%. A gate that
  doesn't measure Claude keeps using the stand-in.
- **Where it could come up:** any probe that wants real Claude; narrows the rule that
  automated runs never use his session.
- **Revisited:** 0.

### J-063: Stucco fire, the Esc menu's leftovers, and which VMs stay

- **Asked:** 2026-11-02, by the PM.
- **Also comes up when:** building materials, menu pages, VM upkeep.
- **Answer:** Andrew: "Stucco burns, but slower than wood." Esc menu: Same network Host / Join, Frame-rate log and Open log folder move to a
  Developer page; the Headwear button goes for now and can come back on a player page. Remove w2 and w4; keep u2 and w3 for the Max account.
  The deer in #47: "Last one I saw looked good", so it stays.
- **Why:** Andrew's calls.
- **Revisited:** 0.

### J-062: The primitives and builders designs' questions

- **Asked:** 2026-11-01, by u1's designs (`docs/design/primitives.md`, `builders.md`).
- **Also comes up when:** creature data, houses, liquids, temporary materials.
- **Answer:** Andrew: "Go", to all the recommendations. The Mac does the creature data move, before `punchy-people`. Fauna mods apply
  per room, view only. A house edit over a player pushes them out, logged. New houses are hollow, so a burnt one leaves a ruin;
  villages stay solid. Products as data and temporary materials come before liquids. Base lava stays until a mod's liquid lava has
  been played. A temporary material lasts while its mod is on, and a revert always clears it. Two paints mixing average all their numbers.
- **Why:** u1's reasons, in the designs.
- **Revisited:** 0.

### J-061: Which speech to text for talking to Claude in the game?

- **Asked:** 2026-11-01, by DesktopUnity through the PM: Windows dictation, or whisper.cpp on the device.
- **Also comes up when:** voice on Linux, Steam Deck or Mac; privacy notes; a release.
- **Answer:** Andrew: Windows first, then Whisper. Ports and adapters: "a good seam for us to unplug the windows side and plug in the
  whisper side". The port knows nothing of Windows; each recogniser is an adapter.
- **Why:** he gets to talk to the game soon, and the Deck version comes after without rework. Windows dictation sends audio to Microsoft, so the setting is plain to the player.
- **Revisited:** 0.

### J-060: May the game start the player's own Claude behind an in-game chat?

- **Asked:** 2026-11-01, by the PM, because J-056 said the game doesn't start Claude.
- **Also comes up when:** shipping chat to players, the terminal panel, anything that launches Claude Code.
- **Answer:** Andrew: yes. Chat in the game is routed to the player's normal Claude Code, run in headless mode, one short-lived
  process a message, with the input and the MCP config. No long session: it writes what it did to a markdown file and the next
  one reads it. Replaces J-056's "the game doesn't start Claude"; the rest stands: their own install and sign-in, nothing stored,
  nothing paid for.
- **Why:** it should feel like playing a game, not building one.
- **Before it ships to players:** Anthropic's legal page sets conditions on running Claude Code in a product (Commercial Terms, an
  unmodified binary, each user's own credentials). The PM reads this as allowed. Confirming that is a step before release, not before building.
- **Update, 2026-11-02:** Andrew: "Not a commercial product yet. Let's keep going." So it no longer holds an alpha or a shipped agent config; it comes back before anything commercial. He also asked to evaluate the Claude API, with $100 a month of usage.
- **Revisited:** 1.

### J-059: Must a player walk inside the houses Claude builds?

- **Asked:** 2026-11-01, by the PM, on building parts (`docs/handoffs/briefs/primitives-walls.md`).
- **Also comes up when:** rooms, interiors, a wall on a cell's edge, "add a room".
- **Answer:** Andrew: "Outside first is the way." Houses look right from outside and burn and shatter properly; "add a
  wing" works, and "add a room" shows as new walls. The data describes real wall segments, so walls on cell edges can
  come later with no format change. The PM reads this as house building going next, with creature data alongside.
- **Why:** it reaches MCP house building soonest, and the edge-wall change touches fire, gas, water, pathing and physics.
- **Revisited:** 0.

### J-058: Do creatures stay Punchy's Editor assets, and how do they move?

- **Asked:** 2026-11-01, Andrew: "we're gonna kind of diverge ... I'll use the same style of ... what punch is doing but I
  want this all in all powered by data and not powered by the editor". On animation, as dictated, "let's try to use
  cinematic if we can make it make a dynamic ... define simple rules that like how legs move and how arms move and
  like a gate like a six legged gate" (the PM reads "cinematic" as kinematic, and "gate" as gait).
- **Also comes up when:** any Punchy port, a new species, limbs, houses, animation.
- **Answer:** creatures and buildings are data, not Editor assets. We keep Punchy's style and diverge from its code;
  J-035's port now lands in data, and J-022's attribution stands. Motion comes from simple rules (a gait: which legs step
  together, with kinematics placing each foot) so any body plan walks, not from hand-posed cycles per limb.
- **Why:** a new animal or a house through the MCP is then just a row. Follows J-055 and J-057.
- **Revisited:** 0.

### J-057: How does new stuff like paint get into the game?

- **Asked:** 2026-11-01, Andrew, on paint: "I don't wanna hard code the world. I wanna find some primitives that can be
  modified to creat convincing effects. Maybe creating new temporary materials from the primitves of the game?"
- **Also comes up when:** any new material, liquid, cover or effect; rules changes; what the MCP offers.
- **Answer:** the world is built from a small set of general primitives. A new thing is a new combination of them,
  as data, not new code, and may be temporary. Extends J-055: a new primitive is added only when a whole family of
  effects needs it, never for one effect. Reshape existing special cases as primitives where that's cheap.
- **Why:** convincing effects from a few rules is the game's chemistry idea, and it's what makes the MCP powerful.
- **Revisited:** 0.

### J-056: How does chat in the game work, and the world tools design's other questions?

- **Asked:** 2026-11-01, by u1's design (`docs/design/world-tools.md`), through the PM.
- **Also comes up when:** any Claude feature in the game, a release that ships one, MCP setup.
- **Answer:** Andrew: "people need to bring their own Claude. Game doesn't log in for them. It's just like an IDE
  terminal where you can just talk to Claude with an MCP configured." So: a plain terminal panel in the game, with
  the MCP already configured; the player runs their own Claude there. The game never signs in, stores a credential
  or pays for Claude (question 9). The PM read his "Ok yea" as accepting its proposed answers too: every log wall
  first; cabins become village buildings; Open means anyone's Claude; a still squirrel will do; Claude keeps a
  hider's secret; animals as data per room; the panel for everyone, the room's mode deciding what goes through.
- **Why:** a terminal the player drives keeps the game out of Claude's sign-in, which Anthropic's terms bar it from.
  Having the game start Claude Code itself is "running Claude Code in your products" and has conditions, so it doesn't.
- **Revisited:** 0.

### J-055: Is the golden squirrel a feature to build?

- **Asked:** 2026-11-01, Andrew, correcting the PM's brief.
- **Also comes up when:** any example Andrew gives of what the MCP could do; world tools; spawn tables.
- **Answer:** no. It's an example of what someone could do through the MCP: "Don't actually hard code it. Let's see if
  we can make it happen via the existing systems." No squirrel in code or base rules. Prove it by doing it
  through the MCP, as a mod, and build only the smallest general gap that blocks it.
- **Why:** the goal is the tools, not the content.
- **Revisited:** 0.

### J-054: Which sound is the annoying chime, and what replaces it?

- **Asked:** 2026-10-31 by Andrew ("the little chime thing is super annoying"); answered 2026-11-01.
- **Also comes up when:** any new sound, a Sending or UI cue, the rest of the sound pass.
- **Answer:** Andrew: it's `arcane`; "No more chimes. Whoosh is ok." So `arcane` becomes the no-tone whoosh, and
  no sound in the game may be a chime. PM's reading: `shatter` loses its tinkle (`shatter-no-tinkle`) and the
  frost stand-in goes too. `splash`'s plop and `blink`'s soft pad aren't chimes and stay. The planned
  Sending chime in `docs/MULTIPLAYER.md` needs another sound when it's built.
- **Why:** it plays on most casts and its 1.5 s ring outlasts them.
- **Revisited:** 0.

### J-053: Land the lag fix with hosted p99 at 33.2 ms against a 33 ms gate?

- **Asked:** 2026-11-01, by DesktopUnity (one 30 s stretch of long hosted rain read 33.2 ms; the rest 31-33).
- **Also comes up when:** any perf gate missed by less than the run-to-run noise.
- **Answer:** PM's call, land it. Andrew's "make a call" standing rule applies.
- **Why:** 0.2 ms over in one stretch is noise next to the change (898 to 32 ms p99, 1720 frames over 50 ms
  to 0, sim 37% to 100%). Holding it would keep the 9 fps collapse in the game for a rounding error.
- **Revisited:** 0.

### J-052: Do a recoloured spell's ground flames take its colour?

- **Asked:** 2026-11-01, by the Mac (spell effects follow `look.color`; its `LookFire` also turns the flames blue).
- **Also comes up when:** element colours land, any look a spell leaves in the world, online play.
- **Answer:** PM's call, yes for now. Keep `LookFire`. When an element's colour lands it replaces this
  where the spell didn't choose a colour, and that work settles how a plain fire beside a blue one behaves.
  **Settled 2026-11-01 (DesktopUnity, `skin-world-view`):** a spell that chose a colour keeps it among fire of the
  element's. A new fire beside it catches that look; a fire already burning keeps its colour. A spell that chose
  nothing burns in the element's colour. `LookFire` stays for spells with their own colour.
- **Why:** orange flames under a blue fireball is the complaint Andrew started from. It's view-only, so it
  can't reach lockstep; two peers may differ at the edges of a fire.
- **Revisited:** 0.

### J-051: Is Linux its own build or part of the release?

- **Asked:** 2026-11-01, by Andrew: "We should treat Linux and win the same release version".
- **Also comes up when:** cutting any release, adding a platform, a friend on another OS.
- **Answer:** every `alpha-X.Y.Z` ships Windows and Linux from the same commit, and the Linux zip goes on
  that release. The Mac builds Linux, since the PC has no Linux Build Support. No Linux in-place updates
  yet: Linux players download each release. macOS stays shelved (J-049).
- **Why:** the host refuses a join unless version and commit match exactly, so a release with no Linux
  build locks Linux players out.
- **Revisited:** 0.

### J-050: The repository's licence

- **Asked:** 2026-10-31, by Andrew (the repo is public and had none).
- **Also comes up when:** contributors, forks, selling the game, sharing mods, adding third-party assets.
- **Answer:** PolyForm Noncommercial 1.0.0 for the game (`LICENSE.md`); CC-BY 4.0 for the mods in
  `rules/mods/`. The SRD 5.2.1 attribution is in `NOTICE.md`.
- **Why (recommendation):** anyone can read, learn, mod and share, nobody can sell it or a fork; Andrew
  keeps every option to sell it later; players can share and remix mods freely. Not legal advice.
- **Revisited:** 0.

### J-049: Ship the Mac app again?

- **Asked:** 2026-10-31, by the PM (the Mac said it's about an hour plus one human test).
- **Also comes up when:** every release; any Mac build or notarization question.
- **Answer:** no. Windows only; the Mac app is shelved until Andrew brings it back up. Don't ask again.
- **Why:** Andrew's call.
- **Revisited:** 0.

### J-048: Does the game's MCP make mods, or only switch them?

- **Asked:** 2026-10-31, by Andrew, after the live-rules clip.
- **Also comes up when:** any MCP tool or prompt design; the mod gallery.
- **Answer:** it makes them. Asked for "a bigger fireball", Claude writes the change itself (a new draft
  with its own title) and applies that, not a saved mod from rules/mods. Saved mods only when the
  player names one.
- **Why:** Andrew: the point is that Claude knows how to change the rules, not that it picks from a list.
- **Revisited:** 0.

### J-047: Can the game's rules MCP be switched off?

- **Asked:** 2026-10-31, by DesktopUnity (live-rules-unity step 3). Andrew said "just make a call".
- **Also comes up when:** anything that opens a port or takes input from outside the game.
- **Answer:** an on/off switch on the mods page, on by default. It serves 127.0.0.1 only; web pages
  are refused (Host and Origin checks); any program running as the player can still use it while on.
- **Why (recommendation):** the design is "every player's Claude talks to their own game", so on by
  default; a player who doesn't use Claude can close the port in one click. Small to build.
- **Revisited:** 0.

### J-046: When does 0.1.10 go out?

- **Asked:** 2026-10-31, by the PM.
- **Also comes up when:** every release: ship what's on dev, or wait for what's close.
- **Answer:** after the in-flight items: the dragon tweaks, the IK punch with the unarmed strike, the
  cast sounds. Voice moves to 0.1.11.
- **Why:** they're close and make the release Andrew described.
- **Revisited:** 0.

### J-045: The punch (punch-crate.mp4, #18)

- **Asked:** 2026-10-31, by the PM (board).
- **Also comes up when:** any melee, knock-back or PvP question.
- **Answer:** too weak: about 2-2.5x stronger, so it's noticeable. It needs a real punch animation
  (IK). Punching other players: yes, as the SRD unarmed strike (1 + Strength modifier bludgeoning,
  plus knock-back), resolved in the core.
  Strength setting: punchShove 2.5 (a crate flies about 4.6 m), picked from the punch-ik clip; the
  crate's direction looked off and is to be checked in the live build.
  Confirmed 2026-10-31 after the direction fix (the shove followed the camera; now straight, 3.2 m
  side-on): keep 2.5, "more than a little nudge" is enough; tweak later.
- **Why:** Andrew watched the clip.
- **Revisited:** 0.

### J-044: Cast sounds A/B/C, and the new slam

- **Asked:** 2026-10-31, by the PM (board).
- **Also comes up when:** any cast-sound or casting-feel tuning.
- **Answer:** B (deep hum, stacked fifths) as the base, with a little of C's accelerating pulse (less
  than C). The new slam animation is good. Iterate sounds as audio files only, no video, made outside
  Unity, in a series like the clips (earth single, water + air pair, charged fire).
  Then picked B + pulse 30% (preset `cast-deep-hum-pulse` in the sound-design skill) to build in.
- **Why:** Andrew listened to the clips.
- **Revisited:** 0.

### J-043: The dragon's look (dragon-view clip)

- **Asked:** 2026-10-31, by the PM (board).
- **Also comes up when:** any dragon tuning; the release notes for 0.1.10.
- **Answer:** loves it ("hilarious"); the model is fine. Scale it up 50%, add a fire particle stream
  from the mouth to the fire wave on the land, the flight can be a little funnier. No new clip
  needed: ship it in the next release.
- **Why:** Andrew watched the clip.
- **Revisited:** 0.

### J-042: Keep writing every rule twice, or freeze the TS sandbox?

- **Asked:** 2026-10-30, by the PM (open since then).
- **Also comes up when:** any brief that changes chemistry; CI's sandbox workflow; the rules MCP.
- **Answer:** freeze the TypeScript sandbox. The C# core is the only engine; no porting.
- **Why:** writing every rule twice doubled the cost of rules work, and the rules MCP runs the core.
- **Revisited:** 0.

### J-041: Who may change a live room's rules?

- **Asked:** 2026-10-31, by the PM (rules MCP design).
- **Also comes up when:** room creation UI, the MCP's `apply`, griefing reports.
- **Answer:** the room picks a change mode when it's made: Open (anyone's changes just appear),
  Host only, or Locked. The MCP ships with the game; every peer validates incoming changes.
- **Why:** it's for fun, not policing; no hosted server to run (`docs/RULES_MCP.md`, D13).
- **Revisited:** 0.

### J-040: Cast feel and its sounds (`cast-feel-sound.mp4`)

- **Asked:** 2026-10-31, by the PM (#22).
- **Also comes up when:** any tuning of casting animation, hit-stop or cast sounds.
- **Answer:** really close. The charged cast is amazing: keep it. The uncharged cast is too
  quick for the slam together and release; the slam needs more oomph.
  Sounds (heard later): too snappy; lower and rumbly. First element low, a little different per
  element; the second layers over it; combine rises in pitch, escalating per charge step; release a
  short whoosh that cuts the charging sounds off.
- **Why:** Andrew watched the clip.
- **Revisited:** 0.

### J-039: Do the combo casts each need their own job?

- **Asked:** 2026-10-30, by Andrew: "the first spells do the same things
  largely, just different shapes".
- **Also comes up when:** any new spell, item or tool; a combo that feels
  like another one; choosing between a damage number and a material effect.
- **Answer:** yes. One verb per cast (light, blast, scald, launch, freeze,
  transform, roll, conduct, carry...), decided by the material system and
  drawn with the effects library. Casts chain: an element onto a lingering
  spell changes it. Flame Wave, Fog Bank, Flood, Molten Boulder and Dust
  Storm are replaced by Falling Star, Geyser, Wave, Forge and Rolling
  Boulder. The table is in docs/SPELL_COMBOS.md.
- **Why:** the game is about material interactions and mini games; spells
  that only differ in shape add buttons, not play. Chains give many outcomes
  from 20 casts without new entities.
- **Revisited:** 0

### J-038: Do grimoire discoveries survive a world reset?

- **Asked:** 2026-10-29, after combo-core stored the grimoire in the world
  save, so the online world reset (J-014) wiped it.
- **Also comes up when:** any per-player progress (discoveries, unlocks,
  items kept), a player joining another host's world, saving and loading.
- **Answer:** yes. Progress belongs to the player, not the world: "If you join
  you should bring your progress." Not urgent now, but build toward a player
  save as a system (BACKLOG 21).
- **Why:** discovery is the player's progress; losing it on a reset or a
  different host's world feels like a punishment.
- **Revisited:** 0

### J-037: How much RAM must stay free to start a worker VM?

- **Asked:** 2026-10-29, by Andrew (to run two worker VMs side by side).
- **Also comes up when:** start.cmd refuses a VM, adding a worker, opening
  an Editor on the PC while workers run.
- **Answer (2026-10-29):** Workers do the Unity work in VMs; the PC runs no
  Editor. So start.cmd keeps 6 GB free when no host Unity is running, and
  J-017's 10 GB when one is. Windows workers start at 4 GB and cap at 8 GB.
  A Linux worker (1 GB start, 1.9 GB peak on the full core and sandbox
  checks) may start down to 8 GB free beside an Editor (Andrew, 2026-10-29).
- **Why:** each VM has a hard cap, so two busy workers (16 GB) plus the
  host (~11 GB) leave about 4 GB of 31 in the worst case; idle they sit at
  4 GB each. The 10 GB came from a host Editor's first build.
- **Revisited:** 0.

### J-034 to J-036: Punchy upgrades (from Build's research, 2026-10-28)

- **J-034, generated areas from Punchy's biomes:** NOT YET. Paper cuts
  first. Build's plan stays on file: B, baked areas, about 2–4 days; A,
  runtime generation in the core, about 10–15 days, as streaming part 3.
- **J-035, animals:** BACKLOGGED, not started (Andrew: "backlog these"; docs/BACKLOG.md 17–19). When picked up, port ALL THREE, in Gameplay's lane, one small branch
  each, tested in the valley each time:
  1. cornered and leashed animals mill about (upstream 7ee4511);
  2. per-animal variety (traits, StyleGroup hairstyles and hair colour);
  3. water life (fish, ducks, eels, dolphins; Habitat Swims/Floats/Leaps).
  First, record the vendored base (upstream b183507) in the Punchy README.
- **J-036, "normal maps":** skipped. Punchy has no creature normal maps.
- **Why:** Andrew's choices.
- **Revisited:** 0.

### J-032: The 0.1.6 RC has a harmless error burst online. Fix it, or ship?

- **Asked:** 2026-10-27. WaterSplashes threw a NullReferenceException for
  about 1 s after every host or join (it held the destroyed solo wizard's
  transform). The one-line fix is `fix/splash-online` (`b45a82b`).
- **Answer (2026-10-27):** ship 0.1.6 AS IS. Don't delete the draft or
  rebuild. The fix goes into dev after the publish, for 0.1.7. The notes
  mention the known log error.
- **Why:** Andrew's call: speed over a harmless log error, in alpha
  (J-026).
- **Revisited:** 0.

### J-031: When do we cut 0.1.6?

- **Asked:** 2026-10-27, by Andrew ("how soon until we can cut a 0.1.6?").
- **Answer (2026-10-27):** cut it as soon as the cloth's cost is proven (at
  most about +1 ms a frame at 4 wizards; Andrew's "good enough if
  performance holds"). It ships what's on dev then. The paper cuts still in
  flight go into 0.1.7.
- **Why:** Andrew's choice over waiting 3–4 h for the paper-cut batches:
  ship sooner (J-019).
- **Revisited:** 0.

### J-030: Add a light-making spell?

- **Asked:** 2026-10-27, from Andrew's playtest (#15). There's no light
  spell; only items make light.
- **Answer (2026-10-27):** add the LIGHT cantrip (SRD: an object sheds bright
  light 20 ft and dim 20 ft for an hour). Not Daylight for now. World builds
  the rules and the radial spot; Gameplay builds the glow.
- **Why:** Andrew picked the recommendation: cheap, and it fits night and
  caves.
- **Revisited:** 0.

### J-020: Caves, the six design questions (backlog #14)

- **Asked:** 2026-10-27, raised by World's spike (`docs/CAVES.md` on
  `caves-spike`, never merged). It recommends C: levels linked by shafts.
- **Also comes up when:** anything underground, digging, shafts, light in
  caves, saves (SaveFormat 2).
- **The questions,** with World's proposals:
  1. How deep, and how many levels? One, about 6 m down.
  2. Can players dig new caves anywhere, or only reach caves that exist?
  3. Fixed levels with stairs, ramps and shafts, or continuous slopes?
  4. What crosses a shaft? Water down, gas and smoke up, wizards falling
     (SRD damage past 10 ft) or climbing; no spells.
  5. Are caves always dark? Yes.
  6. Solo first, or online from the start?
- **Answer (2026-10-27):**
  1. One level, about 6 m down.
  2. Players only reach caves that exist: digging opens into them where they
     lie, and doesn't make new cave space.
  3. Fixed levels joined by shafts, stairs and ramps.
  4. Water falls and gas rises through them; wizards fall (SRD damage past
     10 ft) or climb. No spells cross (World's proposal; not asked).
  5. Caves are dark (World's proposal; not asked; Andrew can overrule).
  6. ONLINE FROM THE START: lockstep and the online probe from the first
     slice, not solo first.
- **Why:** Andrew picked the recommendations on 1–3. On 6 he chose online
  from the start over solo first.
- **Revisited:** 0.

### J-029: Should far-apart players see each other's big moments?

- **Asked:** 2026-10-27, by the coordinator, from World's interest-by-chunk
  plan.
- **Also comes up when:** interest by chunk, the event log online, sky
  effects.
- **Answer (2026-10-27):** yes, the notable ones (a meteor in the sky, a big
  fire's smoke, important log lines) reach everyone; the detail stays
  local.
- **Why:** Andrew picked the recommendation.
- **Revisited:** 0.

### J-028: When should we do online interest by chunk?

- **Asked:** 2026-10-27, by the coordinator, from World's plan
  (WORLD_STREAMING.md, "Part 2, next slice", `ebb1d75`).
- **Also comes up when:** the continent and world size, host CPU, late-join
  time, order-free randomness.
- **Answer (2026-10-27):** later, with the continent. The valley (9×9
  chunks, 4 players) runs whole cheaply today. World's other questions
  (moving every golden for order-free randomness; the host running every
  island) wait until then.
- **Why:** Andrew picked the recommendation, to keep the core stable now.
- **Revisited:** 0.

### J-026: Publish 0.1.5 before the bot's RC runs finish?

- **Asked:** 2026-10-27, by Andrew ("are we good to publish 0.1.5?").
  - Passed: R1 (the install), R3 (Relay both ways, with a reset), and the
    dev probes.
  - R2 (the bot): only 1 of 6 runs done. A bot bug (its sea place) left
    almost no on-world time.
  - Not done: Andrew's own look.
- **Also comes up when:** every release: how much RC testing is enough.
- **Answer (2026-10-27):** **publish now.** The bot keeps running on 0.1.5,
  and findings go to 0.1.6 (or a hotfix if serious).
- **Why:** Andrew: "Deciding to publish now since we are still alpha and
  pre 1.0." So while in alpha, a release can ship on the human-run tests
  (the install, Relay) and the dev probes, without waiting for the bot's
  full RC coverage. Everything a person tested passed. The bot's
  findings so far were all its own bug. Shipping sooner keeps playtests on
  what players actually have (J-019).
- **Revisited:** 0.

### J-025: Where are release-candidate builds made?

- **Asked:** 2026-10-27, by the coordinator, when the Mac spent 40+ min
  compiling Windows shader variants from scratch for the 0.1.5 RC, while
  the PC had them warm in its cache from PC-5.
- **Also comes up when:** every release, a shader or graphics settings
  change, the time to ship.
- **Answer (2026-10-27):** let 0.1.5's Mac build finish. From 0.1.6 on,
  **the PC builds the Windows half** (warm cache) and the Mac builds macOS,
  in parallel, both from the same main commit. Build updates RELEASING.md
  and ship-update to match.
- **Why:** Andrew picked the recommendation. Don't throw away nearly-done
  work. Each platform builds fastest on its own machine.
- **Revisited:** 0.

### J-024: Should rain make the ground look wet?

- **Asked:** 2026-10-27, by the coordinator. The rain WaterMesh fix stopped
  drawing rain's 1-unit film on open ground.
- **Also comes up when:** any weather visuals, terrain shaders, snow.
- **Answer (2026-10-27):** yes, a terrain wetness shader: darker and glossier
  while it rains, drying after. A view-only change (Gameplay), for 0.1.6.
- **Why:** Andrew picked it. It brings back the "wet" read without the
  chemistry or mesh cost.
- **Revisited:** 0.

### J-023: Skip the Editor's domain reload on entering Play mode?

- **Asked:** 2026-10-27, by the coordinator, after static-resets
  (`46abc35`) made the code safe for it.
- **Also comes up when:** Editor start-up time, new static fields (they
  must reset in SubsystemRegistration), Editor-only bugs on a second Play.
- **Answer (2026-10-27):** try it in 0.1.6. Build flips Enter Play Mode
  Options on a branch, measures Play-mode start (about 11 s now), and runs
  the probes twice in a row. It's Editor-only, so players aren't affected.
- **Why:** Andrew picked the recommendation. It saves most of the ~11 s on
  every Play, for every agent, and reverts easily.
- **Revisited:** 0.

### J-022: Does Zachary Franklin's permission cover modifying all of Punchy?

- **Asked:** 2026-10-27, by the coordinator, after Gameplay changed
  `ThirdParty/Punchy/Sky.hlsl` (Punchy has no LICENSE file).
- **Also comes up when:** any edit to or reuse of files in
  `ThirdParty/Punchy/`, the credits, and releases.
- **Answer (2026-10-27):** yes. "Permission applies. We are writing
  together on this."
- **Why:** Andrew and Zachary Franklin are writing it together. Keep the
  attribution in Credits.cs, and record each change in the file's header
  and the Punchy README.
- **Revisited:** 0.

### J-019: When do we cut releases?

- **Asked:** 2026-10-27, by Andrew: "when do we want to cut a new release?
  How many features are enough... if we wait too long we're play testing
  really old versions".
- **Also comes up when:** a big fix lands, the bot finds a blocker,
  someone asks "is X out yet?", any version bump.
- **Answer (2026-10-27):** **cut 0.1.5 today,** once the online reset
  (net-reset and net-reset-view), cheap SSAO (perf-pass) and the playtest
  bot have merged. After that, **a release about every 2 days,** or sooner
  after a big fix.
  - Each release: a merge freeze on dev, then dev → main, then the version
    bump, then the release-candidate build, then the bot's runs and a
    quick human look, then fix any blockers, then ship (docs/RELEASING.md).
  - The Build lane runs the release, and the coordinator calls the freeze.
  - Andrew (later the same day): "2 days is probably fine as a starting
    point", but we may release more often while so much is landing. So
    treat 2 days as the longest gap, not a fixed schedule: cut sooner when
    a solid batch is verified.
- **Why:** Andrew picked the recommendation. About 20 merges had landed
  since 0.1.4 that morning. Short releases keep playtests on what players
  actually have, and bugs found on a release candidate get checked against
  dev before filing.
- **Revisited:** 0.

### J-018: Which build does the playtest bot run against?

- **Asked:** 2026-10-27, by Andrew: don't rebuild for every playtest; use
  the last known build.
- **Also comes up when:** scheduling the bot, any playtest-build rebuild,
  filing a bug found by the bot.
- **Answer (2026-10-27):** **the current release candidate,** built once
  per release with the bot inside (behind `-playtest`). There's no rebuild
  per run and no rebuild on every dev change.
- **Why:** Andrew picked the recommendation. 0.1.4 predates the bot, so it
  can't run it; the bot has to ship inside a release build. That way it
  plays exactly what players get.
  - Findings are checked against dev (a replay on a dev build, or reading
    the fix log) before filing, so nobody files a bug dev has already fixed.
- **Revisited:** 0.

### J-017: How do we relieve the Editor-testing bottleneck?

- **Asked:** 2026-10-27, by the coordinator. Code was written much faster
  than one PC Editor could test it: 8+ branches queued, agents idle waiting.
- **Also comes up when:** any queue for the PC Editor, memory warnings,
  scheduling perf runs, MPPM tests.
- **Answer (2026-10-27):**
  - The Mac is a second test rig: it verifies other lanes' branches
    (compile, EditMode, probes; not Windows-only or PC perf numbers) and
    never edits them.
  - Batch-mode tests may run in parallel, when 10 GB or more is free.
  - Two Editors at once on the PC are allowed. Andrew: "We ran two editors
    overnight. Why can't we do it again?"
  - Limits:
    - start only with 10 GB or more free;
    - MPPM only with ONE Editor open (two Editors plus MPPM hit 52 of
      53 GB);
    - no perf measurements and no Windows builds beside another Editor's
      MPPM.
- **Why:** the earlier one-Editor rule came from the 52/53 GB incident,
  which involved MPPM. Two Editors without MPPM fit (11 GB free with one
  open). Perf numbers need a quiet machine.
- **Revisited:** 0.

### J-015: SSAO, off, cheap or as it is? (perf pass)

- **Asked:** 2026-10-27, raised by Build (the B1 sweep).
- **Also comes up when:** any visual-quality-versus-speed setting (shadows,
  HDR, post-processing), and quality presets later.
- **The evidence** (Mac player at 1080p, mean / p95 ms):
  - base 42.0 / 70.4;
  - off 32.7 / 61.0 (-22%);
  - cheap (depth-only, half resolution, low samples) 35.4 / 62.4 (-16%).
  - Off loses the contact darkening under the eaves, at the ground and
    between bushes. Cheap looks nearly the same as base.
  - Screenshots on the Mac:
    `unity/Builds/demo-videos/ssao-compare-*.png`.
- **Answer (2026-10-27):** **cheap SSAO.** Then measure soft shadows at
  Medium on top of it, and measure the result on the PC (PC-5).
- **Why:** Andrew picked the recommendation. It gets most of the gain
  (-16% mean, -11% p95 on the Mac) without changing the look, and weaker
  GPUs benefit most.
- **Revisited:** 0.

### J-016: May agents force-push their own unmerged branches after a rebase?

- **Asked:** 2026-10-27, by the coordinator, after World force-pushed its
  four rebased branches. Andrew had asked to be consulted before
  force-pushes.
- **Also comes up when:** every rebase of a pushed feature branch, by any
  agent.
- **Answer (2026-10-27):** **allowed, with limits.** Only an agent's own
  unmerged feature branch, after a rebase, with `--force-with-lease`. Never
  `dev`, `main` or another agent's branch; any other force-push needs
  Andrew.
- **Why:** Andrew picked the recommendation. Rebasing your own pushed
  branch is routine, and the lease stops it overwriting work.
- **Revisited:** 0.


### J-013: How may the playtest scripts run past the PowerShell execution policy?

- **Asked:** 2026-10-27, raised by World.
  - This PC's policy blocks .ps1 scripts. The coordinator's own attempt to
    pass `-ExecutionPolicy Bypass` was refused by its safety check.
  - World had been running its spike and run scripts with
    `-ExecutionPolicy Bypass`, which applies to one process and changes no
    setting. It has stopped until Andrew decides.
  - The options:
    (a) Task Scheduler passes `-ExecutionPolicy Bypass`;
    (b) set CurrentUser to RemoteSigned (a security setting);
    (c) sign the scripts;
    (d) port `run.ps1` and `build.ps1` to a small .NET console tool (no
    policy involved).
- **Also comes up when:** any script on this PC: the coordinator's tools,
  `tools/playtest`, release scripts, scheduled tasks.
- **Answer (2026-10-27):** **(d)**, a .NET console tool
  (`tools/playtest/Playtest.Runner`, in `core/Game.Core.sln`) replaces
  `run.ps1` and `build.ps1`. No PowerShell scripts, no Bypass, no policy
  change.
- **Why:** Andrew: "Agree with .NET tool." No security setting changes. It
  shares the bot's rules and tests with the core. For any future script on
  this PC, prefer bash (Git Bash) or .NET over .ps1 files.
- **Revisited:** 0.

### J-014: How should resetting the world online behave (backlog #4)?

- **Asked:** 2026-10-27, raised by Gameplay's plan (World builds the reset
  command, Gameplay the view).
- **Also comes up when:** any host-versus-client authority question, mods
  across a world rebuild, any countdown UX.
- **Answer (2026-10-27):** all three as recommended:
  - a 3 s countdown;
  - the host decides (a client asks; no vote);
  - mods stay on through a reset (re-stamped in the reset batch).
- **Why:** Andrew: "yes that sounds great".
  - With at most 4 players, the host is in charge.
  - A reset restores the world, not the settings.
- **Revisited:** 0.


### J-012: The backlog's priority order

- **Asked:** 2026-10-27, by Andrew ("backlog grooming and prioritization").
  The coordinator groomed `docs/BACKLOG.md` into Now / Next / Later, with a
  lane for each item.
- **Also comes up when:**
  - any lane goes idle (take the next item in its lane);
  - the playtest bot's backlog grows (merge its paper cuts in every few days);
  - a new feature idea arrives (place it in the order).
- **Answer:** approved as proposed (see "Suggested order" in
  `docs/BACKLOG.md`).
  - Now: the quit-crash fix, the playtest bot, the performance pass (19),
    resetting the world online (12), terrain hardness (4).
  - Next: components in the rules (10.1), items and an inventory (5), what
    night does (1), world life (20), swimming then climbing (13, 14),
    streaming's remaining work (11).
  - Later: first person then weapons (7, 6; weapons need a fit decision),
    voice (8), caves (3, a representation spike first), finer ground (18),
    IK (9).
- **Why:** (recommendation)
  - Small, high-value work that unblocks others comes first.
  - The performance pass comes before the next playtest with friends.
  - Components come before more items and weapons, so new things combine with
    the rest for free.
  - Caves wait for hardness and a representation spike.
- **Revisited:** 0.

### J-010: How should we handle the Windows quit crash (D3D12)?

- **Asked:** 2026-10-27, raised by Gameplay. It's an access violation in
  `D3D12Core.dll` on every quit of a Windows build, after saving.
  - The options were: D3D11 first; end the process ourselves; leave it and
    look for a fix.
  - Andrew had the Build lane research it first. Its ranked recommendation:
    (a) test whether players see it;
    (b) drop in Agility 1.618.5;
    (c) end the process at quit;
    (d) graphics jobs off;
    (e) D3D11 first.
- **Also comes up when:** a release (Build), any crash report or event-log
  entry from a Windows player, the playtest bot's crash detector (it will
  flag every quit), graphics API or Unity version changes.
- **Answer:** pending; first, the PC tests to run.
  - Sub-answer (2026-10-27): **test A only, no download.** Test B, the
    Agility 1.618.5 swap from NuGet, isn't approved. Ask again before any
    fix that needs downloaded binaries.
  - Test A result (Gameplay, 2026-10-27): no dialog, and the window closes at
    once (Alt+F4 0.9 s). But the process lingers, hidden, for 17–39 s, then
    crashes in D3D12Core and writes a 42 MB dump to
    `%LOCALAPPDATA%\CrashDumps` on every quit. Windows keeps 10 dumps, about
    420 MB. Every session also shows as a crash in Reliability Monitor.
    Shutdown seems stuck for about 20 s before the fault. A relaunch during
    that time, and Velopack's restart, weren't tested.
- **Answer (2026-10-27):** **the game ends itself on quit** (option c), in
  the Windows player only.
  - Steps on quit: save, shut down Netcode (so peers see a disconnect, not a
    timeout), flush the log, then terminate the process.
  - Test a quick relaunch and Velopack's update-and-restart.
  - No Agility swap (no downloads).
  - Gameplay builds it.
- **Why:**
  - Andrew: "No download" (for the tests). Ending the game on quit "makes
    sense to me".
  - The crash is unseen, but it leaves a hidden process for 17–39 s and
    writes a 42 MB dump on every quit.
  - Ending the process costs no speed, and Velopack is safe with it.
  - The known costs: Unity's own cleanup is skipped (OnDestroy and
    OnDisable, Netcode unless shut down first, the last lines of
    Player.log), and it could hide a real crash on quit later. Anything that
    must happen on quit has to happen before the exit.
- **Revisited:** 0.

### J-011: A growing demo reel of features

- **Asked:** 2026-10-27, by Andrew. Coordinate short videos and screenshots
  of each feature in an artifact that keeps growing, if the artifact system
  supports that.
- **Also comes up when:**
  - any merge of a feature you can see;
  - playtest fixes (their before and after);
  - releases (a reel per alpha?);
  - anyone asking "where can I see X?".
- **Answer:** the existing "Valley Demo Reel" artifact
  (https://claude.ai/artifact/MeFmq29Uv2CE6ZggRKVMkd) became data-driven: a
  fixed page plus `reel.json`, both published beside the clips. A new
  feature only adds its files and appends to `reel.json`, and the agent that
  merges it supplies the clip.
- **Why:** (recommendation)
  - The reel is shared by public link. Published files work for signed-out
    viewers; the db and asset store need a sign-in.
  - Files left out of a publish are kept, so appending is cheap.
  - A version's limit (256 MB, 511 files) leaves room for about 50 more clips
    before a Volume 2.
- **Revisited:** 0.

### J-001: Lanes and file ownership

- **Asked:** 2026-10-27, by the coordinator when it started.
- **Also comes up when:** any new agent or session joins, or a new system
  doesn't fit a lane.
- **Answer:** approved as proposed: Gameplay, World and Build lanes, each
  file with one owner, shared files through the coordinator (see
  COORDINATION.md).
- **Why:** (recommendation) the lanes follow what each session was already
  doing and where they had collided (NetWizard.prefab, ProjectSettings).
- **Revisited:** 0.

### J-002: Rebase and merge `feat/spell-radial`?

- **Asked:** 2026-10-27, by the coordinator; the branch was 197 commits
  behind `dev`.
- **Also comes up when:** any old feature branch turns up unmerged.
- **Answer:** Andrew said rebase and merge. On checking, it was already on
  `dev` (rebased as `1300ff9` / `f8d1a9f`), so the branch was deleted
  instead (see J-004).
- **Why:** Andrew wants finished work in `dev`, not left on branches.
- **Lesson:** before asking about an "unmerged" branch, check
  `git cherry` / the patch content. It may already be on `dev`.
- **Revisited:** 0.

### J-003: Clean up the merged agent worktrees?

- **Asked:** 2026-10-27, by the coordinator: 11 worktrees under
  `.claude/worktrees/agent-*`, all merged and clean.
- **Also comes up when:** subagent or session worktrees pile up after
  merges.
- **Answer:** yes.
- **Why:** they were all merged and had no uncommitted work.
- **Revisited:** 0. J-004 now makes merged-branch cleanup standing.

### J-004: May merged branches be deleted without asking?

- **Asked:** 2026-10-27, by the coordinator (the 5 local and 13 remote
  branches merged into `dev`).
- **Also comes up when:** after every merge, by any agent.
- **Answer:** yes, as a standing rule. A branch fully merged into `dev` can
  be deleted, locally and on origin, without asking. Never `dev`, `main`, an
  unmerged branch, or a checked-out one.
- **Why:** Andrew: "if they are all merged to dev, we can remove them. Save
  as a rule."
- **Revisited:** 0.

### J-005: Start streaming slice 5 (props sleep with their chunk)?

- **Asked:** 2026-10-27, raised by World. It needed `ThingSpawner` /
  `PhysicsProp` from the Gameplay lane.
- **Also comes up when:** any feature that spans two lanes.
- **Answer:** yes, split in two: World builds the event (part A), then
  Gameplay builds the subscriber (part B), merged in that order.
- **Why:** (recommendation) the split keeps each file with its owner, and
  it worked: both parts merged on 2026-10-27.
- **Revisited:** 0.

### J-006: What next for the Build lane after the perf merge?

- **Asked:** 2026-10-27, by the coordinator.
- **Also comes up when:** any lane goes idle.
- **Answer:** research the Windows quit crash (see J-010).
- **Why:** Andrew's choice.
- **Revisited:** 0.

### J-007: What next for the World lane?

- **Asked:** 2026-10-27, by the coordinator.
- **Also comes up when:** any lane goes idle.
- **Answer:** build the playtest bot: nondeterministic, replayable, in the
  real player; it finds paper cuts, never fixes them.
- **Why:** Andrew wants a steady, prioritised list of paper cuts with
  repro steps and before/after screenshots, without a human playtesting.
- **Revisited:** 0.

### J-008: The playtest bot's five design questions

- **Asked:** 2026-10-27, raised by World in `playtest/DESIGN.md` (phase 0).
- **Also comes up when:** any background or scheduled job on the PC (build
  space, run times, Claude cost), and any replay or repro tooling.
- **Answer:** all five as recommended:
  1. A dedicated playtest-build worktree with its own Library.
  2. It runs whenever the preflight passes; if a window flashes up,
     overnight and idle times only.
  3. Lock the frame rate during runs.
  4. Triage every 3 h, only when there are new runs; a digest every 2 days.
  5. v1 is solo only; online and desyncs wait for v2.
- **Why:** (recommendation)
  1. Its own Library keeps its builds away from the other checkouts (there's
     about 1 TB free).
  2. The preflight already keeps it out of the way.
  3. It makes exact replays possible.
  4. Triage only on new runs keeps the cost down.
  5. Solo first keeps v1 small.
- **Result (2026-10-27, phase 1):** the fallback in answer 2 isn't needed. A
  minimised or off-screen player renders every frame and never takes focus,
  so runs can go any time the preflight passes. Sound is muted in the
  Windows mixer (`AppMute`), so the audio detectors still hear it. An
  incremental build takes 16 s.
- **Revisited:** 0.

### J-009: Keep a judgment-call log?

- **Asked:** 2026-10-27, by Andrew.
- **Answer:** yes, this file. Record each question, where else it could come
  up, the answer, why, and how often it was revisited. Check it at every
  decision point.
- **Why:** so the same question isn't asked twice, and past answers guide new
  ones.
- **Revisited:** 0.

## Land a core half before its view half when it breaks play?
- **Asked:** 2026-11-07, by the PM, when u2's `unified-casting-core` (D16) turned out to strand free roam on the start hands until the view half lands.
- **Likely again:** any core/view split where the core changes input or rules the current view relies on.
- **Answer:** Andrew: hold the core half on its branch and land it together with the view half.
- **Why:** dev and the playtest build stay playable.
- **Times asked:** 1
