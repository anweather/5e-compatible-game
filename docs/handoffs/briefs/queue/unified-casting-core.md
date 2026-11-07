Needs: core
Area: unity/Assets/Prototype/Chemistry/World.Hands.cs, unity/Assets/Prototype/Chemistry/World.Combos.cs, unity/Assets/Prototype/Chemistry/Hands.cs, unity/Assets/Prototype/Chemistry/Combos.cs, rules/hold.json `hands`, rules/combos.json
Branch: unified-casting-core

# Unified casting, the core half (D16)

The design is `docs/design/unified-casting.md` and the decision is D16. This half is the core and the rules, with no HUD.
Rules change through the rules MCP (D13).

- **A charge on hands:**
  - Holding a gesture charges it with `combos.json`'s `chargeTicks` and `MaxCharge` (World.Combos.cs `HeldCharge`).
  - A release fires it, and a tap fires at charge 1.
  - Charge only scales a spell's own numbers, never swaps it for another spell.
  - A hand's cooldown starts when it fires.
- **The merge:**
  - Both hands holding the same gesture, with a pair row for their elements, become one cast.
  - A release of either hand fires it, at the lower of the two charges.
  - Otherwise each hand fires on its own release.
  - Put the merge on `PlayerCast`, so the view can show it while the hands are held.
- **One element on both hands:** if one hand is empty and attuned elements exist, it uses the other's element.
- **Attuning outside the Hold:** an `Attune` command is allowed anywhere when the Hold isn't running. It takes a short
  moment and is refused mid-cast. Inside the Hold, the room rule stays.
- **The table** (the doc's two tables):
  - Cut Dust Storm and Sand Blast.
  - Swap fire+air so Lightning is the throw.
  - Drop the `held` variants.
  - Tone Fireball down from about 56 a hit, and say in the report what you set it to.
  - Leave Whirlwind as it is. The view brief decides whether it stays.
- **Free roam on hands:** `HandsOn` covers the world too. The ordered `pairs` path (`LoadElement`) stays only until the
  view half lands, then goes in that landing or one after.
- **Golden tests:**
  - A charged hand cast.
  - A merge at the lower charge.
  - One hand firing while the other charges.
  - Attuning refused mid-cast.
  - Two peers with different hands staying in lockstep.

Gates: build `-warnaserror`, `dotnet test core`, `dotnet format`, jscpd. Land with `land.sh here`.
