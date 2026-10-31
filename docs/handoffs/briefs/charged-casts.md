Brief from the PM: charged and held casts for casting by elements (GitHub #7; Andrew: "we definitely need to figure out how to make the spells stronger and do different effects based on holding it down, since we already implemented that for a lot of these").

You're a worker in a VM, running headless. This is your only task. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task or wait loop you started, and don't send anything after it.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../charge origin/dev -b charged-casts` and work there. Copy in the asset packs that a fresh worktree lacks (Starter Assets and the others under unity/Assets that the main checkout has and git ignores). Read COORDINATION.md (lean), docs/SPELL_COMBOS.md (its "Not yet: tap, hold and ground shapes (every cast goes off at charge 1)" line is this task) and `gh issue view 7`.

What exists:
- The old cast path charges: holding Cast builds a level every `ChargeSeconds` (0.45 s) up to `World.MaxCharge`, and release casts at that charge (`SpellCaster.cs`: `Charge`, `Chargeable`).
- Spells already scale with charge in the rules (Fireball's charges 1 and 2, Plant Growth at charge 2 regrows, shapes grow with charge).
- Combos can name a held variant (`rules/combos.json`: Wall of Stone `"held": "stone-ramp"`).
- Casting by elements (ComboCommand, the cast meter) always goes off at charge 1, so Plant Growth does nothing useful and the ramp can't be reached.

Do:
1. **Charge by holding the last element.** Pressing the final element's button starts the cast. Holding it charges a level every ChargeSeconds, up to each spell's max (only spells that are `Chargeable`), and releasing casts at the charge reached. A tap casts at charge 1, so quick casting is unchanged. Same on keys (Z X C V) and on the pad's bumpers and triggers.
2. **Held variants.** A combo with `held` casts its held spell when the hold reaches max charge. Wall of Stone held is Stone Ramp. Add `held` entries where SPELL_COMBOS.md already describes a held or strong form, and list which you added.
3. **The meter shows the charge:** the levels as steps on the cast meter, and the held variant's name when you reach it. Keep it simple; the host does the polish in UX pass 4, along with #9's smooth fill.
4. **Online:** the charge travels in the command (ComboCommand / ComboIntent), so every player's world casts it at the same strength, and it's in the checksum.
5. **The bot:** its element-cast goals sometimes hold to charge, and one chain goal casts Plant Growth charged on burnt ground.

Tests: the charge from hold time and the choice of the held variant go in plain C# with tests (no Play mode), plus a core test that Plant Growth at full charge does what charge 1 doesn't. If the rules change, both engines get shared goldens.

Checks: `dotnet test core/Game.Core.sln`, `cd sandbox && npm test`, and a Unity batch compile that's clean, with no new warnings.

Deliver: commit and push the branch `charged-casts`, not merged. Final message, 6 lines or fewer: the commit; how holding works on keys and pad; the held variants added; the tests; the time; what the host should feel-test.
