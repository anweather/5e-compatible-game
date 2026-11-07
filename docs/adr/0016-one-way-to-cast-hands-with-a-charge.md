# D16. One way to cast: two hands, with a charge (2026-11-07)

**Status:** Accepted (Andrew, 2026-11-07: "Lgtm. Let's do it")

- **Context:** Free roam and the Hold cast in two different ways.
  - **Free roam:** four element buttons, ordered pairs of elements, and a charge with rumble (`docs/SPELL_COMBOS.md`).
  - **The Hold:** two hands, one element each, where the button picks the range and there's no charge
    (`docs/design/the-hold.md` section 3).

  In the 2026-11-06 playtest Andrew liked the world's feel and the Hold's hands, and asked to keep both
  (`docs/playtests/2026-11-06-human-results.md`).
- **Decision:** The world and the Hold cast the same way, as set out in `docs/design/unified-casting.md`:
  - **Hands:** each hand holds one attuned element, and one element attuned is on both hands.
  - **Gestures:** a trigger is short range, a bumper long range, and both on one hand an area spell.
  - **Charge:** holding a gesture charges it with the world's charge and rumble, and releasing fires it.
  - **Merge:** both hands charging the same gesture, with a row for their two elements, merge into the pair's spell,
    and their icons slam together.
  - **Attuning:** free roam attunes anywhere from the spell list. The Hold keeps its attuning place.
  - **One spell table:** the hand table, cut to spells that each do a different job.

  Free roam's four element buttons, its ordered pairs and its held variants (spells that change at full charge) go.
- **Why:**
  - It's one thing to learn, and it has the feel Andrew called "clear what's happening".
  - Two elements at a time makes picking what to attune matter everywhere.
  - Charging both hands on their own lets one hand rapid-fire while the other charges.
  - A smaller table drops duplicates and the confusing dust spells.
- **Reopen if:** a sweep (trigger and bumper on one hand) is too awkward on a pad while charging, or two-element limits
  make free roam's chemistry too thin to explore.
- **Sources:** Andrew's playtest notes and his answers of 2026-11-06; `docs/design/unified-casting.md`.
