# Playtest issues

What came up in real play, sorted by kind, each with an id to cite in
commits. Big new systems also get a section in `BACKLOG.md`; this file is
for the issues themselves and their state. Newest playtest first.

States: **open**, **done** (with the commit), **later** (moved to
`BACKLOG.md`, which says why).

## 2026-10-25: two players online on alpha 0.1.1

Andrew and a friend, Relay, a long session. "It worked great." Storm
sound was heard in a later build (main, not 0.1.1).

### Bugs

| Id | Issue | State |
|---|---|---|
| P1 | Pushing a player to the world's edge with Gust of Wind: a chunk didn't load and they got stuck at a wall | open: the chemistry covered only the area around the yard; making it cover the whole valley, waking chunks around players (world streaming part 2) |
| P2 | The world's edge is just a drop-off | done: invisible walls 3 m inside the end of the land, marked by a line of rocks; probe `edge` |
| P3 | Collision boxes of earth spells (raised and dug ground) don't match what's drawn | open |
| P4 | Tree collision is a bit off | open |
| P5 | Ray of Frost doesn't freeze water: nothing happens | done: it froze, but lake ice wasn't drawn (the lake plane stands for its cells); now it is, solid, at the surface |
| P6 | Performance drops with a lot of fire | open |

### Spells and how they feel

| Id | Issue | State |
|---|---|---|
| P7 | Casting is binary. Hold to charge: Fire Bolt and other thrown spells go further the longer you hold; earth spells raise or dig more steps | open |
| P8 | Too many earth spells: consolidate them into a few primitives | open |
| P9 | Earth needs more height range, up and down | open |
| P10 | A second fire spell | open |
| P11 | A spell that freezes water (see P5) | done: Sleet Storm (level 3, Water & Ice), a wide circle of ice |
| P12 | Spells should affect the other wizards, as Gust of Wind and the earth spells do: more than now, not so much it stops being fun | open |

### Movement and play

| Id | Issue | State |
|---|---|---|
| P13 | Swimming | later (BACKLOG 13) |
| P14 | Climbing | later (BACKLOG 14) |
| P15 | A ball to run at and kick around, with a fenced or walled court (soccer-like) so the start is less barren | open |
| P16 | Tag: one player is "it" (in another colour) and tags someone else | open |

### Interface

| Id | Issue | State |
|---|---|---|
| P17 | Volume sliders (master, effects, ambience): still a little loud | done: Sound page, master / effects / ambience |
| P18 | Move the on-screen instructions to a Controls page of the menu; flash them on screen the first time | open |
| P19 | Health, stamina and magic bars (infinite in the sandbox for now) | open |

### Look and sound

| Id | Issue | State |
|---|---|---|
| P20 | Water looks bad and patterned | open |
| P21 | Shaders: grass, water reflections | open |
| P22 | The glowing balls' lighting is very harsh | done: the light hangs 2 m above the ball, bright edge 0.5 (was 0.8), softer glow |
| P23 | The storm sounds are too loud | done: rain, wind and thunder turned down, on the ambience setting |

### World

| Id | Issue | State |
|---|---|---|
| P24 | More fixtures, further from the start: rocks, a river from the mountain into the lake, places to go to. Not too much: procedural generation is coming | open |
