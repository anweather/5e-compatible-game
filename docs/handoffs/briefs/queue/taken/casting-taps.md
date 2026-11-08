Needs: core
Area: rules/hold.json (hands), rules/combos.json, unity/Assets/Prototype/Chemistry/World.Hands.cs and its tests (the
hand goldens); no view code (the PC does the view half and the two land together, per the 2026-11-07 call)
Branch: casting-taps

# Taps fire at once, holding is the charge (J-078 step 1; given to u2)

Andrew picked option C in `docs/design/casting-pace-options.md` (J-078): free taps, and mana paying for charges and
merges, **built as A first**. This is A.

- A tap fires on the next tick or two (`hands.tapTicks`, 2), with no wind-up.
- Keeping the button down past about 0.3 s (2 ticks) starts the charge, as now, with its levels.
- A tapped single gets a **tiny** cooldown (`hands.tapCooldown`, about 3 ticks): just enough that one button can't
  machine-gun. Andrew picked "tiny" over none.
- The long `castTicks` stays only on charged and merged casts. `handCooldown` and `bothCooldown` stay on those.
- Upgrades that trim cooldowns (`quick-hands`, `calm-hands`, `earth-focus`) keep working on the new numbers.

Rules through the rules MCP where it can; `World.Hands.cs` gives a tap its own meter. Lockstep (D4): integer ticks in
the hands state that's already saved and hashed; the hand goldens move on purpose: list each. Say in the report what
the view must change (CastFeel's tap beat, the hand icons' meter) so the PC can do that half.

Before landing: `dotnet build core -c Release -warnaserror`, `dotnet test core` twice in full, `dotnet format`, and the
Hold lockstep tests. Push the branch only, remove this claim on the branch in the last commit. The PC does the view half
on this branch; the lead lands both together.

After (2026-11-08, try 1): the watcher stopped u2 for low memory on the PC; the work in progress is on
`wip/u2-casting-taps` (380f3684). Continue from it on branch `casting-taps`, tests in the foreground.
