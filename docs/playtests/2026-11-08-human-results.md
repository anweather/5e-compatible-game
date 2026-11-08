# Human playtest results, 2026-11-08 (night)

Andrew and a friend, two players, on `Builds/PlayTonight` (spell-balance c68b8df: dev with castle-cliff-canyon and
hold-batch, plus the new spell numbers; not the Mac's hold-bodies). About 48 min on the PC (23:38 to 00:27 MST).
His notes were spoken after; sorted here, his words kept where they decide something.

## What the logs show

PC logs: `%USERPROFILE%\AppData\LocalLow\anweather\Spell Chemistry Valley\` (Player.log, play-log-2026-11-07-233828.csv,
chat-records). No crash, no desync, the game closed cleanly. The PC's play log shows a chat-modding session and no Hold
waves, though Andrew lost a Hold run: the run was probably on the other peer's log, or not logged.

- **L1 `LandRocks.cs:123` NullReferenceException**, about 147 times, from the first "moved the land" act to the end of
  the run (`rock.Body` destroyed but still read, from `ValleyChemistry.LateUpdate`). Rocks stop following the ground.
- **L2 chat by voice:** about 30 records with an empty prompt and reply; "make it daytime" (twice) and "make the centaur
  three times taller" answered "I don't see a new request"; refused: steer a centaur to the lake, rabbits 20x faster,
  constant meteors ("too heavy"); a 100 m mountain came out about 21 m; slow turns (25 s, 17 s).
- **L3 hitches:** 5.7 s at start-up, 2-3 s at 1 and 2 min, about 2 s three more times; 100-300 ms on each chat apply.
  Otherwise 60-90 fps at 4K.
- **L4** the pause menu's IMGUI "control 11 ... only 11 controls" ArgumentException, 6 times (`PauseMenu.cs:459`).

## What he said

Bugs:

- **B1 The other player vanishes (main one).** No desync, but after a while the second player's body is gone. First
  seen after they lost the Hold; resetting (holding Back) brings him back, but only for a while. His spells still
  charge (Andrew hears it) and still change the world; only the body isn't drawn.
- **B2 Pants still disappear.** Low priority: "we might just work around that ... it's minor."
- **B3 Punching gives the puncher no feedback.** It hurts the other player, and the one punched gets feedback, but the
  puncher sees nothing on screen.
- **B4 Water slows you far too much** at its edge when you aren't swimming. The thin strip of water by the castle,
  "I don't know what it is", slows you a lot.

Casting and balance:

- **C1 Too much charging and waiting.** "The casting of the charges feel good", but a run is mostly charging or
  waiting on cooldowns. He wants spells to spam fast ("just throw stuff around everywhere") as well as spells to charge
  up. Options to brainstorm: another pass at casting, removing cooldowns, or using the mana pool, which exists but does
  little. A design question first.
- **C2 Meteor is still by far the best spell for everything.** Spell balance otherwise seemed OK. Give each element a
  spell with meteor's wow factor rather than weakening meteor: a big thunder strike, jagged earth spires popping out, a
  tornado coming down. Ice is open ("hail or rain feels kind of boring, giant snowball, not sure").
- **C3 The raider waves' balance seems good.** Wants a second raider path, from the east of the castle, opposite the
  stable.

Controls and screen (controller first):

- **M1 The pad's menu is "completely bonkers".** D-pad down opens "a million different things"; down moves left and
  right, and left and right move up and down, backwards. Oversimplify it: d-pad down shows two hands, and each stick
  picks that hand's element. That's all.
- **M2 Too much text everywhere,** explaining things; it confuses more than it helps.

Mods (chat):

- Six-legged dolphins were funny. He wants player mods: move faster, bigger, jump higher, double jump into quadruple
  jump.

World (asked for, lower priority than the bugs):

- **W1 A river that makes sense.** Replace the odd thin strip by the castle with a better river; another river could
  flow into the lake from the other side. Punchy's rivers are "really cool".
- **W2 Nine valleys.** Make the world about three times as big across: keep the valley in the bottom left by the ocean
  and add valley-sized squares, three across and three up, nine in all, joined. Make sure the game works on larger
  areas. More intentional biomes, designed from the world's properties so it feels organic; look at Punchy's biomes.
- **W3 A flat desert for the Hold** in the bigger world, a more open defence than the climb up the hill.
- **W4 Punchy's creatures:** pull the latest creature models from Punchy, and a script to re-sync them on demand.
  "I don't wanna spend a ton of time on this."
