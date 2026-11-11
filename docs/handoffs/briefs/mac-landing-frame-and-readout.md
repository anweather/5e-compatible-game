Needs: unity
Area: unity/Assets/Prototype/Scripts/ (ChemGrid.Visuals.cs's redraw after a cast, the core's World.Cast only to measure; a new frame readout in the HUD and Esc > Settings), docs/perf/2026-11-09-profile.md (a new heading only); not CellWaterMesh, FolkJobs, MapLand, ValleyChemistry (the PC's view-map-arrays), not the core's World storage
Branch: mac-landing-frame

# The frame after a Fireball lands (#212), then an on-screen frame readout (#217)

The 60 fps pass (16.7 ms a frame, mean and p95). Two parts, each its own commit.

1. **#212.** On the nine, the frame after a Fireball lands is 58-69 ms (the `ChemGrid.Render` redraw), and the landing
   frame has about 8 ms of the core's `World.Cast`. Spread the redraw over frames, the drawn chunks nearest first
   within the draw budget (J-086: cost follows the drawn chunks); profile `World.Cast`'s 8 ms and name it (report it if
   it's the core's, don't change the core). Measure in a development player on the nine and the valley: the cast,
   the landing and the 5 s after (worst, mean, p95).
2. **#217.** A small toggleable frame readout for playtests: mean, p95 and worst of the last second against 16.7 ms,
   off by default, on a key and in Esc > Settings, remembered as a player setting (not shared state). It must cost
   under 0.1 ms. Also: the play log's header said 640 x 480 when the player was asked for 1280 x 720; make it say the
   real size.

Since 2026-11-10 the core holds inactive chunks packed (D17); an unpack from any thread but the World's throws, so
read cells on the main thread. Rebuild the yard before every full EditMode run so no Untitled scene is open (a save
dialog stalled the PC's Editor for an hour).

## Done

EditMode passes in full; smoke passes on a fresh valley yard and on the nine; `online` on the valley; a "Fix:
landing frame" heading in the profile doc. Self-land with `land.sh auto mac-landing-frame --compiled`; the lead
closes the issues from the report.
