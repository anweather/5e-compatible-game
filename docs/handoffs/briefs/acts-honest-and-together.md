Brief for the Mac: three fixes on named acts and stamps, branch `acts-honest` off `dev`, in this order. Core and MCP, no Editor needed. Read the issue for each: it has the PC's cases.

**#149. A firing says what landed.** `Workbench.CallAct` drops NoRoom rows before the command, so the reply says "It's done" when half is missing. The reply must name what landed and what didn't, row by row, including a short count within a row, so Claude can tell the player the truth. If nothing landed, it isn't done. Check `act` has no such gap.

**#146. The same canyon works on every seed.** Noise pushes the floor past -17 m, so a canyon is taken with one seed and refused with the next. Either keep noise off the floor's depth or check against the deepest case: pick the one that keeps the look, and say which. Whether a stamp is accepted must not depend on the seed. The PC's case: wall 36, radius 32 at 216,384. If stamp tests move, say why. The stand-in valley refused this same site as crossing water where the player took it: say why, it may be nothing.

**#150. Rows share a spot.** At a named place each row picks on its own, so a campsite scatters 67 cells though `within` is 3. Pick one spot for the firing, then place every row around it within `within`. Keep determinism: the spot comes from the world's seed and the firing, never from a peer. If no spot fits every row, say so under #149's rules rather than scatter.

**Tests:** one per fix, each failing before it. No golden should move outside stamp shapes: if one does, stop and say why.

**Gates:** core build, tests, format, jscpd by real exit code, EditMode in a batch clone. The 8 `ApiKeyTests` are the known Mac failures.

**Report:** one short message: the commit, the gates, what the PC must look at. Push the branch; don't merge it. Phase 5 comes after this lands.
