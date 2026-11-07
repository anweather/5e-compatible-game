# Human playtest results, 2026-11-06 (evening)

Andrew ran `docs/playtests/2026-11-07-human-plan.md` a day early, on the PC.
Builds: `PcDev` at `e341d13` (dev, castle-seat and hold-progress landed) and
`PcVoice` at `bf845c0` (`vivox-spike`), started with
`tools/vivox/voice-in-the-woods.ps1`. Voice logs: `%TEMP%\voice-in-the-woods`.
His notes were spoken, then sorted here; his words are kept where they decide
something. Written in two parts while he played; part two is at the end.

## Proximity voice

| Id | Verdict | What he said |
|---|---|---|
| V1 | FAIL (direction) | Found where the voice comes from. No left/right at all: "I get nothing there." Wants stereo direction. A body standing there would help ("I don't have an NPC here") |
| V2 | PASS, too gentle | "The fading works pretty well", but too far-reaching: he can be far away and still hear it. Make it fade out sooner |
| V3 | FAIL (setup?) | He hears his own voice, but only near the beacon, and it gets louder as he gets closer |

Reading the logs (not proven yet): the beacon is a second player on the same PC,
and it joins with the real capture device (`input devices 7 ... transmitting`)
while it injects. So it most likely sends his mic from the beacon's spot as
well as the clip. That would explain an echo that only comes from the woods and
grows as he gets closer. A beacon that sends only the injected clip (its mic
muted) would tell that apart from a real self-echo. The walker's log also shows
`added speaker` four times in four minutes (22:51:53 to 22:54:59): the beacon
seems to drop out and rejoin.

Asks: a shorter audible distance than 32 m (or a steeper rolloff), working
stereo panning, and a visible speaker at the voice.

## The Hold

| Id | Verdict | What he said |
|---|---|---|
| T1, T2 | PASS | Cinders and Roots "seem fine"; he didn't notice the wrong hand being punished either way. No tuning asked for |
| T3 | not yet | |
| T4 | not yet | Wants **timed waves**: the next wave comes at a fixed time, so pushing raiders back doesn't buy time |
| T5 | partly | Health bars sit over the enemies (good). Raiders climb the road's slope now (good). The whole ground by the raider spawn was on fire: "this is kind of cool". The little crabs are hard to hit, which he reads as a good sign |

Bugs:

- **The upgrade offer can't be used.** Things like "close hand, calm, hands
  wide sweep" appear at the top of the screen. He couldn't tell they were
  upgrades, and couldn't pick one, with the controller or any other way. He
  doesn't know how to take a wave's reward. (The Hold phase 5 UI is the
  `hold-ui` job; this is what it has to fix.)
- **Raiders stop attacking at the castle.** Once they reach the top they walk
  into the building and attack the air.
- **The wooden gate still doesn't appear.**
- **Gust of wind seems to do nothing.** It should push raiders back, and could
  knock them off the cliff, with diminishing returns (once or twice per raider).
- **Frost ray aiming is finicky:** he aims at a raider but it doesn't track. It
  aims continuously but doesn't hit continuously.
- **Combination spells seem broken:** R1 R1 R1 L1 with fire and air didn't work.
  It may just be finicky.
- **Villagers walk up and down the road** during the Hold. He doesn't see why.

Asks:

- **Lock-on for aimed spells:** lightning should auto-target, and frost ray
  and other aimed spells should lock onto an enemy a little. Not every spell a
  skill shot.
- **Hide the spell selector at the bottom** during the Hold; he doesn't use it.
- **Take the Hold's controls into the main game** in place of the spell
  combination system: "I think this works a little bit better".
- **More raider bodies:** reuse the deer and other animals as wave raiders.
- Overall: "this is working a lot better than last night", "this is actually
  kind of fun".

## The valley

- **The dock looks great.** "This is really coming together."
- **A dolphin jumped through the dock.** Maybe the dock is missing collision.
- **The castle's pad:** the flat ground is "much better than yesterday".
- **The castle approach** (looking at the castle from the town, the right
  side): instead of the sloped hill of rock and mud, he wants a straight cliff
  face from there to the castle. The tree valley there would become a
  valley or canyon running up, with a switchback climbing the mountain beside
  the cliff, and the cliff going a bit further.

## Part two: the Hold up to the first boss (T3, T5)

Balance:

- **Fireball is too strong.** It hits for about 56, and he sees no reason to
  use anything else.
- **Meteor is "completely broken"**, and he loved it ("that was great, I like
  that a lot"). Read as overpowered and fun: keep how it feels, check it
  against fireball.
- **Whirlwind, dust storm and "flaw"** (as dictated; probably a spell name)
  look like they do nothing. Make them worth casting.
- **The keep's health is too high.** He could run around and let raiders hit
  the keep, and nothing much happened. (He mentions the keep falling around
  wave seven, so wave seven is where to check.)

Aiming and flyers (T5):

- **The first flyers seem impossible to hit**, with fire or by switching to
  fire and air.
- **Lightning has no pitch.** It always points the same way, so he can't aim
  up or down or look high enough. Together with part one's lock-on ask: the
  aimed spells' targeting is "a little bit off" in general.

The castle:

- **Collision at the foot of the walls and the small towers.** Where the
  ground has been eaten away from them, he can walk through the bottom of
  their meshes. He should have to jump over that.
- **Walls float when the ground under them is gone.** They stay standing,
  charred. Walls should fall as they're damaged or undermined, which would
  also be a reason not to cast fireball everywhere.
- **The keep looks good, and the wall tops look much better**, but nothing
  takes you up there. His idea: move attuning out of the keep and into the
  towers, one element per tower, so you have to use the whole castle.
- **The ground to the left** (looking at the castle from the town) is empty.
  Keep it in mind for something later.

## Part three: Cinders, Roots, and how casting should work

- **T1, T2:** Cinders and Roots "seem fine". He didn't notice anything special
  about them. Neither asks for tuning.
- **Merge the Hold's casting with free roam's charging.** He likes the Hold's
  fixed element per hand with the direction key as a modifier. He also really
  likes charging in free roam: holding a spell longer and watching it grow is
  "really neat and satisfying", and gives a reason to hold longer. He wants one
  scheme with both: an element per hand, the direction modifier, and a charge.
  He doesn't know how yet. This is a design question, and it goes with part
  one's ask to bring the Hold's controls into the main game.

## Part four: the boss and a run's length

- **T3: PASS.** The first boss "seemed fine". No issues.
- **T4: too long.** Target a run of about **10 minutes**. His way there: bring
  the waves closer together, spawning them at fixed intervals instead of when
  the last wave dies (the same as part one's timed waves). He didn't answer
  whether 1.8 hours to unlock everything is too short.

## Also said that night (on issue #163, found 2026-11-07)

- **Punch sound:** "The sound for punch is not right at all. Or the sound for breaking a window." Filed on #18 (J4).
- **Burnt house:** "after house burns down, the windows stay and the collision boxes stay where they were." Filed on
  #54 (J6).
