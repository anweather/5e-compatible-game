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

## Carrying this into Unity

The same loop, one level up:

- **Rules:** `rules/*.json` and the **same mod patch format**, loaded by
  the C# `Game.Core` (D3). A mod proven in the sandbox switches on in
  Unity unchanged, as far as the verbs exist on both sides. Keep the
  verb vocabulary shared.
- **Server:** a small authoritative server, either a headless Unity
  build or a plain .NET service running `Game.Core`. Unity clients
  connect to it. It sequences inputs exactly like the sandbox host. For
  real-time 3D it sends authoritative state rather than lockstep (D4:
  PhysX isn't deterministic across machines).
- **Hot swap:** the server applies mod patches at a tick boundary and
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
