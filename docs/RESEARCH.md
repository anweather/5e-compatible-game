# Research (sourced)

Gathered 2026-10-13. **[mem]** means general knowledge that wasn't
re-verified this session. **[unverified]** means the claim was
second-hand. Spot-check the SRD claims against the official PDF before
relying on them.

---

## 1. Licence

- **SRD 5.1** (2014 rules) is available under OGL 1.0a *or* CC-BY-4.0,
  your choice. CC was added Jan 2023 after the OGL 1.1 revolt.
- **SRD 5.2** (2025-04-22) and **5.2.1** (errata, 2025-05-01) are
  **CC-BY-4.0 only**. Wizards says all future SRDs will be CC only. A CC
  grant can't be revoked for released content. No 2026 change was found.
  <https://www.dndbeyond.com/srd>
- **Attribution** follows the preamble pattern below. Copy the exact
  text from the 5.2.1 PDF preamble.
  > This work includes material from the System Reference Document 5.2.1
  > ("SRD 5.2.1") by Wizards of the Coast LLC, available at
  > https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the
  > Creative Commons Attribution 4.0 International License …
- **Not licensed:**
  - the names "Dungeons & Dragons"/"D&D", the logos and the ampersand;
  - Beholder, Mind Flayer, Displacer Beast, Strahd, Tiamat and similar;
  - every setting (Forgotten Realms, Greyhawk, Eberron).
  - Some SRD items are renamed to avoid trademarks: Bigby's Hand is
    "Arcane Hand", Otiluke's Freezing Sphere is "Freezing Sphere".
  - Say "5E-compatible".
  - <https://www.tribality.com/2025/04/23/dd-system-reference-document-v5-2/>
- **Mechanics aren't copyrightable.** Non-SRD spells (Shape Water, Mold
  Earth, Catapult, Thorn Whip, Tidal Wave, …) can be reimplemented as
  mechanics under your own names and text.

## 2. What SRD 5.2.1 contains

Counted from <https://github.com/downfallx/dnd-5e-srd-markdown>.

| Thing | Count |
|---|---|
| Classes | 12, one subclass each (Berserker, Lore, Life, Land, Champion, Open Hand, Devotion, Hunter, Thief, Draconic, Fiend, Evoker) |
| Species | 9 (Dragonborn, Dwarf, Elf, Gnome, Goliath, Halfling, Human, Orc, Tiefling) |
| Backgrounds | 4 |
| Spells | ~339 |
| Monsters/animals | ~330 |
| Feats | ~17 |
| Magic items | ~250 [approx] |

Missing: other subclasses, the Artificer, most backgrounds, all
Xanathar's/Tasha's content, and setting lore. For a learning project
this is more than enough.

## 3. Machine-readable data

| Source | 5.2 data? | Licence | Use? |
|---|---|---|---|
| downfallx/dnd-5e-srd-markdown | Full 5.2.1 as Markdown | CC-BY-4.0 | **Yes**, parse to JSON |
| foundryvtt/dnd5e `packs/_source` | 5.2 and 5.1, structured | Code MIT, SRD data CC-BY | **Yes**, richest structure, Foundry schema |
| Open5e API | "srd-2024" document [unverified] | MIT code; content keeps its own licence | Filter by document |
| 5e-bits / dnd5eapi.co | Mostly 2014; `/api/2024` is a stub [unverified] | OGL-tagged | Skip unless using 5.1 |

Plan: a build script that parses a **subset** (the spells and monsters
you implement) into typed JSON. Validate it against a second statement
in the source, the way triple-triad's `build-cards.mjs` does: for
example, check that a spell's level and school match the class spell
lists.

## 4. Prior 5E video games: lessons

- **Solasta** (SRD 5.1, turn-based) made verticality the selling point:
  - flying, climbing, Levitate and Spider Climb;
  - shoving enemies off ledges, with RAW fall damage of 1d6 per 10 ft up to 20d6;
  - Feather Fall as a reaction.
  - Proof that 5E plus 3D terrain is fun.
  - <https://gamebanshee.com/3hy9y>
- **Baldur's Gate 3** (turn-based) added DOS-style surfaces: water that
  can be electrified, frozen or turned to steam; grease that ignites; ice
  that knocks you prone.
  - Shove, Jump and Disengage became **bonus actions**.
  - High ground gives advantage.
  - **Throwing** objects and creatures is a core verb.
  - Lesson: 5E's action economy is too stingy for physical play, so make
    physical verbs cheap.
  - <https://bg3.wiki/wiki/Water_(surface)>
- **Dark Alliance (2021)** (real-time) failed on action fundamentals:
  mushy lock-on, enemies ragdolling into the sky, poor aiming.
  - Lesson: real-time needs character-action craft first, and d20
    to-hit rolls feel arbitrary when the player is aiming.
  - <https://destructoid.com/reviews/review-dungeons-and-dragons-dark-alliance>
- **Pathfinder: WotR** offered real-time-with-pause *and* turn-based.
  The turn-based mode felt like a slog because encounters were balanced
  for real time. Lesson: **balance for one mode.**
- **Real-time translations of 5E** (<https://thealexandrian.net/?p=51165>):

  | 5E concept | Real-time adaptation |
  |---|---|
  | 6-second round | Per-ability cooldowns or an action meter; spell slots stay as charges |
  | Reactions | Timed parry/counter windows |
  | Opportunity attacks | Free swing on leaving reach (already spatial) |
  | AC | Hit chance, or damage reduction/graze with hitboxes deciding contact |
  | Saving throws | Physically dodge the area; the save is a fallback |
  | Concentration | Works unchanged (one spell; damage triggers a check) |
  | Advantage | Flanking, height, prone, blind |

## 5. SRD spells that are *physical* (verified in 5.2.1)

| Physics primitive | SRD spells |
|---|---|
| Impulse/push | Thunderwave, Gust of Wind, Arcane Hand, Shatter |
| Friction | Grease, Ice Knife, Sleet Storm |
| Gravity | Levitate, Feather Fall, Jump, Fly, **Reverse Gravity** |
| Kinematic grab | Mage Hand, Telekinesis |
| Terrain authoring | Wall of Stone/Ice/Force/Fire/Thorns, Move Earth, Earthquake |
| Fluids | Create or Destroy Water, Control Water, Freezing Sphere |
| Volumes | Fog Cloud, Web (flammable!), Entangle, Wind Wall, Spike Growth |
| Teleport | Misty Step, Dimension Door |
| Combustion | Fireball ignites unattended flammables (RAW) |
| Element sculpting | **Elementalism** (cantrip): beckon air, earth, fire or water; sculpt 1 ft³. One cantrip covers four non-SRD ones |

Not in the SRD (rename if wanted): Shape Water, Mold Earth, Gust,
Control Flames, Catapult, Thorn Whip, Tidal Wave, Earth Tremor, Dust
Devil.

## 6. Systemic-interaction prior art: the patterns

- **BotW chemistry engine** (GDC 2017) is a rules layer *beside*
  physics. It has three rules:
  1. Elements change a material's state.
  2. Elements change each other.
  3. **Materials never change materials directly.**

  Rule 3 turns N×N interactions into elements×materials plus
  elements×elements.
  <https://gamesbeat.com/the-legend-of-zelda-breath-of-the-wild-makes-chemistry-just-as-important-as-physics/>
- **TotK** (GDC 2024): Ultrahand broke on objects that weren't
  physics-driven, so nearly everything became a body. Lesson: if players
  can grab or glue things, every object must be a real body.
  <https://www.shacknews.com/article/139210/zelda-totk-gdc-2024-panel-recap>
- **Divinity OS2** surfaces have three independent parts:
  - a base element: water, oil, blood, fire, poison;
  - a state: electrified or frozen;
  - a blessing: blessed or cursed.

  Three small axes multiply, so you never enumerate the combinations.
  <https://dos2.wiki/wiki/Cursed_(Status)>
- **Magicka**: 8 elements, up to 5 per cast, opposite pairs cancel, and
  a *precedence table* picks the spell form. A huge spell space comes
  from a tiny rule set. <https://en.wikipedia.org/wiki/Magicka>
- **Noita**: a per-pixel falling-sand cellular automaton, with rigid
  bodies rebuilt from pixel regions and handed to Box2D.
  <https://80.lv/articles/noita-a-game-based-on-falling-sand-simulation/>
- **Immersive sims** [mem]: "verbs over items", where every object
  answers a shared set of verbs.

**Synthesis for this game:**

1. **Tags, not types.** Tags include `flammable`, `conductive`, `wet`,
   `frozen`, `metal`, `light`, `anchored`, `organic`.
2. **Rules as data.** `(element, tag) → effect` and
   `(element, element) → result`, in JSON, with priorities.
3. **Two substrates:**
   - a coarse 3D/2.5D **field grid** for spreading things (fire, water
     depth, oil, ice, gas, electricity), using cellular automata that are
     cheap, deterministic and easy to read;
   - **Rapier rigid bodies** for things that move.

   Bodies read and write the cell they occupy.
4. **5E supplies the numbers.** Dice, DCs, ranges (5 ft ≈ 1.5 m), areas
   and durations parameterise the physics. "Thunderwave pushes 10 ft"
   becomes an impulse tuned to move a Medium creature about 3 m.

## 7. Physics in turn-based games, and determinism

- **Patterns:**
  - Worms/Angry Birds: commit, simulate, wait for the scene to settle.
  - Into the Breach: discrete, deterministic, *previewed* pushes.
  - Phoenix Point: simulated projectiles.
  - Frozen Synapse: simultaneous planning, then a deterministic resolve.
  - The tension is predictability against chaos. The fixes are
    **previews** (BG3's throw arc, ITB's push arrows) or making chaos the
    joke (Worms).
- **Deterministic engines:**
  - **Rapier**: the official JS/WASM builds are cross-platform
    deterministic. Hash snapshots to compare. Avoid `Math.sin/cos` in
    initial conditions. Insertion order matters.
    <https://rapier.rs/docs/user_guides/javascript/determinism>
  - **Jolt**: needs a compile flag that the prebuilt JS packages lack.
  - **Box2D v3**: deterministic across platforms, but 2D.
    <https://box2d.org/posts/2024/08/determinism/>
  - Havok (Babylon): no determinism guarantee.
- **For a plugin**, `ApplyMove` can do the following and stay a pure
  function of (state, move):
  1. Rebuild the Rapier world from `pluginState` in canonical order.
  2. Apply the spell.
  3. Step a fixed dt until bodies sleep or N ticks pass.
  4. Write back the settled transforms.

  Pin the Rapier version: a bump re-simulates differently.
  `engineContract`'s purity checks would catch drift. The client either
  replays the same steps with the same WASM for animation, or tweens.

## 8. Engines (2026)

| Option | For | Against |
|---|---|---|
| **Three.js / R3F + Rapier** | TypeScript and React like your boards; ~1–3 MB [mem]; same physics on server and client, deterministically; drops straight into ShadowMount | A library, not an engine: you build your own tooling (leva, theatre.js, a level editor) |
| **Babylon.js + Havok** | Most "engine-like" on the web (inspector, GUI, animation); TS-first; free Havok WASM | Havok isn't deterministic, so a turn-based server would need Rapier anyway |
| **Godot 4.6** | Unity-style editor; Jolt is the default physics since 4.6; free | C# can't export to the web, so web means GDScript; multi-MB WASM; needs COOP/COEP headers; awkward to embed |
| **Unity 6** | Most tutorials; runtime fee cancelled (Sept 2024) | C#; heavy WebGL builds; doesn't fit ShadowMount |
| Unreal 5 / Bevy | — | No web target / immature tooling plus Rust |

Character controllers: Rapier `KinematicCharacterController`
(shape-cast, slopes, snap-to-ground, autostep off by default).
<https://rapier.rs/docs/user_guides/javascript/character_controller>

Feel:
- coyote time of ~0.05–0.1 s;
- jump buffer;
- variable jump height;
- faster fall.

Magic movement works as **overrides on the kinematic controller**:

| Spell | Override |
|---|---|
| Jump | Higher jump velocity for a duration |
| Levitate | Gravity scale set to 0 |
| Feather Fall | Caps fall speed |
| Misty Step | Shape-cast to the target, land at the last free spot |

The player stays kinematic; the world stays dynamic so spells move props.
