Brief from the PM: finish two half-built features (docs/BACKLOG.md priority 9).

You're a worker in a VM, running headless. This is your only task. Your final message is your report, and it goes straight back to the PM. Before that final message, stop any background task or log watcher you started.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../halfbuilt origin/dev -b half-built` and work there. Copy in the asset packs that a fresh worktree lacks: Starter Assets and the others under unity/Assets that the main checkout has and git ignores. Read COORDINATION.md (lean) and docs/BACKLOG.md items 2 (Weather) and 17-24 for context.

1. **Snow and fog as weather** (item 2, "Still open"). Both enter the weather stretches alongside clear, cloudy, windy, rain and storm, with their own weights in sim.json.
   - Snow falls on open cells as a snow cover that builds up slowly. It melts in heat and fire through the existing melt verb, slows walkers a little, and makes Frost and the ice slide matter.
   - Fog weather lowers sight the way Fog Cloud does, but valley-wide, and lifts when the stretch ends.
   - All of it goes through the material verbs: no reaction written for one weather.
   - Both engines (C# core and TS sandbox), with shared goldens.
   - The looks of snow falling and of fog are for the host later. Keep Unity changes to what the rules need to compile.
2. **A coil and a gate in the valley.** Place one charge puzzle in the world data: a puddle or pond edge, a coil, and a gate on a path. Lightning into the water opens the gate, through the machines that already exist (rules/machines.json, World.Machines.cs). Put it on open ground near the spell-golf course on the north meadow, without overlapping the course (world/golf.json). Add a test that the placed gate opens from a bolt into that water.

Checks: `dotnet test core/Game.Core.sln`, `cd sandbox && npm test`, and a Unity batch compile that's clean, with no new warnings.

Deliver: commit and push the branch `half-built`, not merged. Your final message is the report, 5 lines or fewer: the commit, what changed, the tests, the time, and what the host should look at.
