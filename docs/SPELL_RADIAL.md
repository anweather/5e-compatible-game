# Spell radial

The spell bar has outgrown its pages (Elements, Earth, Arcane): "Elements" is
a grab-bag and every new spell makes it longer. It becomes a two-ring radial:
element first, then that element's spells. Decided with Andrew, 2026-10-25;
ships in alpha 0.1.1.

## How it plays

- **Hold Tab** (gamepad: hold **LB**) to open it. The game keeps running
  (it must, online); mouse look pauses and the cursor is free, as in the Esc
  menu.
- **Ring 1, elements:** the mouse or right stick highlights an element wedge.
- **Ring 2, spells:** resting on an element, or pushing further out, opens its
  spells as an arc around that wedge: name, level, a one-line effect.
- **Release** selects the highlighted spell. Releasing in the centre cancels.
  A quick **tap** of Tab (no movement) swaps back to the previous spell.
- Click still casts. Q / E (LB / RB) cycle within the current element.
  Number keys 1–5 jump to an element and open its arc.
- The HUD's long bar becomes one chip: element colour, spell name, key hints.

## Groups

| Element | Spells |
|---|---|
| Fire | Fire Bolt |
| Water & Ice | Create Water, Ray of Frost |
| Storm | Gust of Wind, Thunderwave, Shocking Grasp |
| Earth | Mold Earth, Mold Earth (heap), Earthen Trench, Wall of Stone, Move Earth |
| Arcane | Misty Step, Levitate, Mage Hand, Grease |

The grouping is data: each spell in `rules/spells.json` gets a `"group"`, and
the groups (order, name, colour) live in rules too, so mods can regroup or
add elements.

## How it's built

- Selection logic is engine-free and tested in the core: angle and distance to
  a wedge, dead zone, the two rings, release / cancel / quick-swap.
- Drawn with IMGUI like the rest of the HUD; wedges are generated textures.
- Selection still goes through `SpellCaster.Select(id)`, so online play and
  the probe are unchanged. The pages are removed once the radial is verified.

Order: data and logic with tests → mouse and keyboard → gamepad → HUD chip →
retire the pages → docs.
