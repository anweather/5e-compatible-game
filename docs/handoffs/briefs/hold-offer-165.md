Needs: core
Area: unity/Assets/Prototype/Chemistry/Hold.cs, rules/hold.json, their tests
Branch: hold-offer-165

# The Hold's offer must only draw upgrades the wizard can use (#165)

Read issue #165. `Suits` treats an upgrade row with no holds and no gesture as suiting everyone, so "Calm hands"
(two-handed spells) is offered to a fresh player whose left hand is still locked. Expected: the offer only draws
upgrades usable with what is unlocked.

Do: make `Suits` (Hold.cs) take what an upgrade needs from the rules (a tag or field in rules/hold.json, not a
hard-coded name: tags in the rules, D9), so an upgrade that needs both hands is not offered with one. Change rules
through the rules MCP (docs/RULES_MCP.md) if you edit rules JSON. Tests: a fresh one-handed wizard is never offered
a two-handed upgrade; once the second hand unlocks it can be; the offer stays deterministic (seeded RNG, ordered).
Keep goldens unchanged unless the offer's draw itself changes: then say which and why.
Don't touch the Unity view (Scripts/). Done when `dotnet build core -c Release -warnaserror`, `dotnet test core`,
`dotnet format core --verify-no-changes` pass; push the branch; report commit and test counts. The PC compiles it.
