# Height changes in Punchy (Andrew, 2026-11-13: overnight job)

Many spells change the ground's height: craters (Fireball, Meteor), spires and walls (Earth Spires), digging, and
lava's leavings. The core already does all of it, in half-metre **height steps** per cell (`World.Terraform`,
`World.Land`, `LandStamp`), deterministically, so every peer agrees (D19).

The question is how Punchy shows it and walks on it while touching Punchy as little as possible.

## Punchy's shape

Punchy's ground is a pure function: `TerrainSource.SampleHeight(float2 xz)` (TerrainSource.cs:658). Every height in
the game goes through it: 347 call sites (movement, camera, grass, flora, water, audio, chunk meshes). Chunk meshes
are cached per 64 m chunk; `TerrainStreamingSystem` can already flag chunks for a rebuild (nearest first, :324). The
grass is rebuilt per 16 m tile from the same samples.

## The design: one height-edit layer in `SampleHeight`

1. **The layer (Punchy-side data, filled by the bridge).** `TerrainEdits` is a small window of height deltas in
   metres: origin, size, 1 m spacing, `NativeArray<float>`, and a version. It's held in one new field on
   `TerrainSource`. With no edits it's empty, and `SampleHeight` returns exactly what it does today.
2. **The hook (the one real change to Punchy).** At the end of `SampleHeight`, inside the window, add the delta
   sampled bilinearly. That's about 5 lines, Burst-safe. Everything that asks for a height follows at once:
   - you walk on the crater,
   - the grass sits in it,
   - the camera won't clip,
   - water and audio see the new ground.

   `SampleNormal` follows too, if it's computed from heights; check it, and add the same delta where it isn't.
3. **Rebuilds (two small additions).** Each edit has a rectangle:
   - a public `FlagRebuild(rect)` on `TerrainStreamingSystem` re-meshes the chunks it touches;
   - the same on `GroundDetailSystem` re-scatters the grass tiles it touches.

   Flora placed on cells that moved by more than ~1 m is hidden, or re-sat, through the flora system's existing
   placement (a mask, like burnt grass). The design must say which.
4. **Concurrency.** Jobs read the window while the bridge writes. So the bridge builds a new array per change (at most
   a few a second), swaps it in, and disposes the old one only after the frame's terrain jobs complete (a job
   dependency or a fence). No locks in Burst code.
5. **The source of truth stays the core (the bridge).**
   - At seed time the bridge records each cell's Punchy height. It sets the core's land to round(height / 0.5 m),
     relative to the window's lowest point, within the core's land range (`World.LandLowest`/`LandHighest`).
   - On a change (the core's `LandMoved` event, or comparing the land layer for the window), the delta is
     (core steps − seeded steps) × 0.5 m.

   Punchy's fine detail is kept, because only the difference is added. The deltas come from the lockstep World, so
   every peer gets the same ground.
6. **Water.** The core already sends water into a crater; the bridge's water overlay shows it. Punchy's own sea, lake
   and river sheets don't move.

What changes in Punchy outside the bridge folder: `TerrainSource.cs` (one field, ~5 lines in `SampleHeight`,
`SampleNormal` if needed), `TerrainStreamingSystem` (`FlagRebuild(rect)`, ~10 lines), `GroundDetailSystem` (the same,
~10 lines), and maybe the flora mask. All of it is inert with no edits.

## Done when

- A Fireball on open ground leaves a crater you can walk down into, with grass on its floor, and no holes or seams at
  the chunk edges.
- An earth spell (Earth Spires, or a wall) raises ground you can climb or that blocks you.
- Water poured into a crater stays there.
- With the bridge off, Punchy is bit-identical: every `SampleHeight` returns as before.
- The rebuild costs under 2 ms a frame spread over frames.
- Screenshots and a new exe are delivered.
