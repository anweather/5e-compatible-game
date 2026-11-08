Needs: unity
Area: the wizard's view (movement speeds, SurfaceEffects, CharacterController, camera height, JumpHeight, DoubleJump),
remote wizards' size; no rules or core changes
Branch: player-mods (continue u2's branch; merge origin/dev first)

# The view reads the player stats (u2's player-mods core half, 2026-11-08)

u2 put the player's stats in the rules (`rules/sim.json` `player`, per slot `player.by.<slot>`, the MCP tool
`set_player`). Read `docs/RULES_MCP.md` "Player stats". At each rule switch each wizard reads
`Rules.Sim.Player.Of(slot)` (solo is slot 0) and applies:

- **speed:** multiplies the base speeds passed to `SurfaceEffects.SetBaseSpeeds`; `RestoreBaseSpeeds` returns to those,
  so wading and Shapeshift still stack on top.
- **size:** scales the model, the `CharacterController` (height, radius, centre, step offset) and the camera height;
  check there's room before growing.
- **jump:** multiplies `JumpHeight` and `DoubleJump.height`.
- **jumps:** `DoubleJump` allows `jumps - 1` jumps in the air.
- Remote wizards are drawn at their slot's size.

Andrew's examples: "make me faster", "make me bigger", "jump higher", "quadruple jump". Add a probe (`playermods`) that
sets each stat by `set_player` and checks the wizard moves, stands, jumps and air-jumps as expected, then `normal`
puts it back.

Before landing: `dotnet test core`, EditMode N/N in full, `playermods` and the movement probes, a ≤30 s 720p clip with
a poster (`player-mods.mp4`): chat makes the wizard big, fast and quadruple-jumping. Push the branch only, remove this
claim on the branch. The lead lands it.

Also from the 2026-11-08 records (checked by the lead): the 28 records with no prompt are the log's `of` follow-up
lines, as u2 guessed; not a bug. The "I don't see a new request" replies had real prompts: Claude misjudged them
(open, for a chat prompt pass).
