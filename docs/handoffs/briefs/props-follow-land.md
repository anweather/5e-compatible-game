Brief for the Mac: props follow a land stamp (#142), branch `props-follow-land` off `dev`.

Trees follow a stamp, 0.00 off the ground. Props don't: crates end about 14 m buried under a 15 m dome and hang about 14 m in the air over a -15 canyon. They go back on undo.

**Do:** a prop on stamped land rides the ground as it eases, up and down, and back on undo. Start from how trees do it and share that path if it fits. Items lying on the ground and the dragon's reset should behave the same: check, and say what you found.

**Keep:** physics decides nothing shared (D4). If a prop's height or place is in the core or the hash, keep it integer and deterministic, and a late joiner gets the same answer. If a golden moves, stop and say why before going on.

**Stay out of** the stamp's shape and noise (`World.Land.cs` height maths, `CellHeightfield`): the PC is reworking them. If the fix needs them, stop and say so. Also out of bounds: the terrain shaders, which the PC is on.

**Decide and say:** what happens to a prop on a slope too steep to rest on, and to one pushed while the ground moves. Pick the simple answer.

**Tests:** core tests for whatever the core owns, EditMode in a batch clone for the view side. Nothing here will have been seen drawn, so list exactly what the PC must look at.

**Gates:** core build, tests, format, jscpd, each by real exit code. The 8 `ApiKeyTests` are the known Mac failures.

**Report:** one short message, the commit and the gate results. Push the branch; don't merge it.
