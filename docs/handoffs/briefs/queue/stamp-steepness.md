Needs: core
Area: unity/Assets/Prototype/Chemistry/World.Terraform.cs, unity/Assets/Prototype/Chemistry/CellHeightfield.cs, unity/Assets/Prototype/Chemistry/Tests/TerraformTests.cs
Branch: stamp-steepness

# Land stamps: limit steepness, and don't move a refused stamp far away (#156, the core half)

Andrew's playtest (2026-11-04): canyons at radius 10 and 6 came out with 30 m walls, and refusals moved them into
thin slots far from where he asked; a mountain was 100 m high and about 12 cells across.

1. Read the stamp code and say what limits height against width today, and how a refusal picks a new place.
2. Add a steepness limit (a rules value, D13) so a stamp's walls stay climbable-looking: scale height to width,
   or refuse. A refused stamp moves at most a few cells, or isn't placed and says why; never far away.
3. Golden tests (CellHash in `TerraformTests.cs`): a tall narrow stamp is capped; a refusal moves it no more
   than the limit. Update existing goldens only where this change explains them, and say which.
4. The camera half (`CameraClearance` sitting too high) is view work: leave it, and note in your report what you saw.
5. Comment on #156 with the commit and a `Discovery-Cost:` line; leave it open for the camera half.

Work in a new worktree off origin/dev (u2's main checkout holds uncommitted liquids-2 work: leave it). Run `tools/workers/gates.sh`, then land with `land.sh here` (VM_BRIEF "Landing your work"), deleting your claim in `queue/taken/`. A STOP is a refusal: report it as one.
