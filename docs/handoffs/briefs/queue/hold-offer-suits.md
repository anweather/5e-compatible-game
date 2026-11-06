Needs: core
Area: unity/Assets/Prototype/Chemistry/Hold.cs, unity/Assets/Prototype/Chemistry/HoldProgress.cs, unity/Assets/Prototype/Chemistry/Tests/HoldTests.cs, rules/hold.json, tools/workers/gates.sh
Branch: hold-offer-suits

# The Hold's offer only draws upgrades the wizard can use (#165)

1. **First, the gates on a 5 GB VM.** jscpd runs out of Node memory on u2 unless
   `NODE_OPTIONS=--max-old-space-size=3072` is set. Set it in `gates.sh` for that gate, so every
   worker's gates and self-landing pass without it in the shell.
2. **#165:** "Calm hands" (two-handed spells) was offered to a fresh wizard whose left hand is locked.
   `Suits` treats an upgrade row with no holds and no gesture as suiting everyone. Make the offer draw only
   upgrades usable with what's unlocked; a row that needs both hands says so in the rules (a tag or field in
   `rules/hold.json`, through the rules MCP if it's a rules change, D13). Golden or unit tests: a one-handed
   wizard is never offered a two-handed upgrade; a two-handed one still can be; the offer is the same on every peer.
3. Comment on #165 with the commit and a `Discovery-Cost:` line; don't close it (the PM closes on ship).

Work in a new worktree off origin/dev (u2's main checkout holds uncommitted liquids-2 work: leave it). Run `tools/workers/gates.sh`, then land with `land.sh here` (VM_BRIEF "Landing your work"), deleting your claim in `queue/taken/`. A STOP is a refusal: report it as one.
