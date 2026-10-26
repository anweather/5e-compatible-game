# The live lab: play together, change the rules live, keep what's fun

The goal is a build loop with friends:

1. Play the same board together.
2. Anyone can bring an idea, written by hand, by Claude, or by their own
   LLM, and switch it on mid-game for everyone.
3. Vote on what worked.
4. Kept ideas become part of the next version; dropped ones are
   archived as failed experiments.

The web sandbox does this today. The Unity version should reuse the
same loop and the same data, so ideas proven in the sandbox carry
straight over.

## The loop

```
  idea ─▶ patch (JSON) ─▶ check ─▶ saved as an experiment
                                        │
                     switch on ◀────────┘   (hot-swap: everyone, same tick)
                          │
                     play together ─▶ vote keep / meh / drop + notes
                          │
          keep ─▶ Claude bakes it into rules/ ─▶ next version ships it as base
          drop ─▶ stays in the archive, off
```

**Who does what:**

| Step | Who | Where |
|---|---|---|
| Write a data mod | Anyone: by hand, with **Write it with Claude** (the page asks Claude), or with **Copy brief for another AI** (paste into any LLM, paste the answer back) | Experiments panel |
| Switch mods on or off | Anyone in the room | Experiments panel |
| Vote and leave notes | Anyone signed in | Experiments panel |
| Mark keep, drop or merged | The page owner | Experiments panel, status menu |
| Bake kept mods into `rules/` | Claude Code, asked by the owner | This repo, as a PR |
| New verbs or physics (code) | Claude Code or a contributor | This repo, as a PR. Republishing reloads everyone's page |

## Multiplayer: how it works

- **Lockstep, not state streaming.** Every page runs the same
  deterministic simulation (seeded RNG in the world state, fixed update
  order, no floating-point surprises).
  - Each player sends small **intents** to the host: walk, cast, place a
    critter, paint, switch mods, pause.
  - The host bundles them into numbered **batches**, a few hundred bytes
    each, six times a second.
  - Every page applies the same batches in the same order.

  This fits the platform's 4 KB message limit; a full board would not.
- **The host** is the longest-standing synced page in the room. When it
  leaves, the next one takes over automatically.
- **Recovery.** A page asks the host for a snapshot when it:
  - joins late,
  - misses a batch,
  - sees a new host, or
  - fails the checksum the host sends every 20 batches.

  The host saves the snapshot to the shared db at `rooms/<code>`, and
  the page continues from it.
- **Same code solo and online.** Playing alone is a room of one where
  you are the host.
- **Tested:** `sandbox/test/net.test.ts` runs several simulated pages in
  one process, covering:
  - a late join;
  - 30% lost batches;
  - the host leaving;
  - a mod switched on mid-game.

  A two-tab browser run against the fake platform (`dist/dev.html`)
  confirmed identical boards.

**What friends need:** the published sandbox shared with them at
**Contributor** or above, using the artifact's Share menu. Viewers can
watch the page but can't join rooms or save experiments.

**Known limits:**
- Your own actions take a round trip through the host (about 200–300 ms).
  That's fine for a sandbox; an action game would predict locally.
- Every page must run the same version. A republish reloads everyone,
  which keeps that true.

## Hot-swapping: data mods vs. code

| | Data mods (hot-swappable) | Code changes |
|---|---|---|
| What | New or changed reactions, terrain, covers, gases, spells, critters, things, machine thresholds, sim numbers, new element names | New effect verbs, new physics, new UI, new kinds of machines |
| Who | Anyone, in the page, live | A PR to this repo, then a republish |
| Safety | JSON only, validated against the engine before it can switch on; nobody runs anyone else's code | Reviewed in the repo |
| Speed | Seconds, mid-game | Minutes; everyone's page reloads (the "disconnect and reconnect") |

Running friends' *code* live in everyone's browser would mean executing
untrusted JavaScript on each other's machines, so the sandbox
deliberately doesn't. The data layer turns out to cover most game-design
experiments ("what if ice cracked under weight?", "what if steam
scalded?", "a new critter that…"). When an idea needs a new verb, that
is a signal it's worth a real PR.

## Experiment records

All experiment data lives in the artifact's database. Claude Code can
read it directly, so "bake the kept ones in" is a one-line request.

| Path | Holds | Who writes |
|---|---|---|
| `mods/<id>` | `{title, idea, patch, author, created, status}` | Anyone at Contributor or above. Mods are never edited in place; "Remix" makes a new one, so an id always means the same patch on every page |
| `votes/<userId>` | `{v: {modId: keep/meh/drop}, notes: {modId: text}}` | Each person writes only their own |
| `rooms/<code>` | The latest snapshot for that room | The host |

**Promotion:**
1. The owner marks a mod **keep**.
2. Claude reads `mods` and `votes`, folds the patch into `rules/*.json`
   (with a test for the new behaviour), opens a PR, republishes, and
   marks the mod **merged**.

## In the Unity game today

The running Unity yard switches rules the same way, solo and online.

**Controls.** **M** (keyboard) or **Start / Options** (gamepad) opens the
mods panel over the game. It lists every `rules/mods/*.json` with its title
and idea, ticks the ones in force, and shows the rules' fingerprint and,
in red, why the last switch was refused. Switch a mod with **1–9**, a mouse
click, or **d-pad up/down then X / Square**. While the panel is open the
wizard's own controls (spells, items, reset) and mouse-look are paused.

**When a switch happens.** Always between ticks:
- *Solo:* the new rules are checked at once and put in force before the
  next tick (`ChemGrid.QueueRules`).
- *Online:* the host checks them and stamps a rule switch into the next
  `NetChem` tick batch, next to that tick's casts, with the full mod list
  and each mod's JSON. Every machine switches at exactly that tick. A
  client's click asks the host, which checks the mod against *its own*
  copy of the file and stamps it, or sends back why not.
- *Late join:* the host's history carries the rule switches with their
  ticks, so the replay switches at the same ticks as the live game.
- *Checksum:* every 30 ticks the host sends `cellHash/fingerprint`. A
  machine on different rules logs "Desync at tick N: this machine runs
  different rules from the host's (host …, here …)".

**Editing files while it runs** (Editor and development builds, solo or
host). `RulesWatcher` watches `rules/*.json` and `rules/mods/*.json`. On
save (debounced 300 ms, handled on the main thread) the file is reloaded
and checked:
- a mod that is on is replaced in place ("Rules reloaded: mods/x.json")
  and the new numbers apply at the next tick;
- a mod that is off is only checked ("Mod checked: … switch it on in the
  mods panel"), so a broken one shows up as soon as it is saved;
- a base file reloads the base rules (solo only; online, other machines
  have their own files, so the host refuses it);
- a deleted mod that is on is switched off;
- anything that doesn't check out is refused with its errors, in the log
  and the panel, and the old rules stay.

**Scene tuning is part of the base.** The valley's longer fire fade and
lightning reach (`ChemGrid.fireFadeCells`, `conductLimit`) are applied as a
mod over `rules/sim.json` when the world is built, so they are in the
fingerprint, and a mod that sets `fadeCells` (Wildfire, Damp world)
overrides them while it is on.

**Props and items** take the new numbers too: a crate, barrel or carried
item re-reads its row in `entities.json` / `items.json` when the rules
change (hit points, keeping the damage already taken, and mass; an item's
light is reconfigured). Behaviours added when an item spawned (a "use" it
didn't have, breaking) are not added or removed by a mod; that needs a
restart.

**Measured** (yard meadow, fire lit at tick 0, burning cells per tick,
mod switched on at tick 10): identical to the base run up to tick 10;
base reaches 66 burning cells by tick 40, Wildfire 190, and with Damp world
the fire dies out by tick 21. Online (two Multiplayer Play Mode players)
the host's and the client's checksums matched at every 30th tick through
two switches and a late rejoin.

## Carrying this into Unity

The same loop, one level up:

- **Rules:** `rules/*.json` and the **same mod patch format**, loaded by
  the C# `Game.Core` (D3). A mod proven in the sandbox switches on in
  Unity unchanged, as far as the verbs exist on both sides. Keep the
  verb vocabulary shared.
- **Server:** a small authoritative server, either a headless Unity
  build or a plain .NET service running `Game.Core`. Unity clients
  connect to it. It sequences inputs exactly like the sandbox host. For
  real-time 3D the grid stays lockstep and only PhysX props travel as
  authoritative state (D4: PhysX isn't deterministic across machines;
  `MULTIPLAYER.md` §1). Today the host is one of the players (D7).
- **Hot swap** (built for the lockstep yard; see "In the Unity game
  today" above): the server applies mod patches at a tick boundary and
  broadcasts them. Clients rebuild their rule index, as the sandbox does
  today. The C# core already has the pieces: `ModPatch.Parse`,
  `Rules.WithMods` (same merge and checks as the sandbox),
  `World.ApplyRules` and `Rules.Fingerprint` for the checksum
  (`ARCHITECTURE.md`).
- **Builders with their own LLMs:**
  - *Data:* the same brief (`docs/MODDING.md`, plus the current tables).
  - *Code:* PRs, and the Unity CLI's MCP bridge (`RESEARCH.md` §9) for
    people working in the Editor on their own machines.
  - The experiments database can stay where it is: the sandbox page
    becomes the lab notebook for both versions.

Order: prove the loop in the sandbox with friends first; it costs
nothing to change. Build the Unity server only once the P2/P5/P6
prototypes give it a game worth serving.
