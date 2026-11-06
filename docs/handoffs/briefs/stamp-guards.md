Brief for the Mac: two small land stamp fixes, branch `stamp-guards` off `dev`. Core only, no Editor needed.

The PC is reworking how canyons and domes look, so stay out of the stamp's shape and noise (`World.Land.cs` height maths, `CellHeightfield`). Touch only the refusal check and the words. If either fix needs the shape code, stop and say so.

**#143.** A stamp went over the ball court: fence and scoreboard swallowed. Refuse a stamp touching the court, in words, the same way village, course, puzzle and buildings are refused. Read the place from the map's data, never by name in code, and check no other placed feature has the same gap. Say which kinds you added.

**#144.** `act` says 100 m either way, but hollows stop at -17 m from height 0 (`LandLowest`). Make the description true. Don't change the limit.

**Tests:** a stamp on the court is refused and leaves the land as it was. No golden should move: if one does, stop and say so.

**Gates:** core build, tests, format, jscpd, each by real exit code. On the Mac, the 8 `ApiKeyTests` are the known failures.

**Report:** one short message, the commit and the gate results. Push the branch; don't merge it.
