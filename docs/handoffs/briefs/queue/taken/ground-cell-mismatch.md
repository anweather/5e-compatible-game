Needs: unity
Area: world/maps/valley/ (land.json, ground/), the ground export; no rules
Branch: ground-cell-mismatch

# One drawn cell disagrees with the land (the lead, 2026-11-08; given to the Mac)

On dev after `hold-east-path` (63adb82d re-exported `ground/` and patched `land.json` along the old west-brook and 42
cells north of the castle cliff), the PC's `hitch` and `online` probes fail `no_console_errors` on host and client:

"The drawn ground isn't this map's land on 1 of 64 cells: (56, 366): land 46.63 m, drawn 46.43 m"
(`ValleyChemistry.cs:342`, `CheckGround`). It shows with rebuild-threading on or off, so it's the data.

Find why cell (56, 366) differs: a `land.json` patch that missed the ground re-export, or the reverse. Fix the data
so they agree, re-exporting with the LF fix in place (`GroundPack.Save`, d1d142c7), and add a check that would catch
it in core (`GroundLandTests` should cover every cell; say why it didn't). Before landing: `dotnet test core`, EditMode
N/N, `hitch`, `online`, `smoke` with 0 console errors. Push the branch only, remove this claim on the branch in the last
commit. The lead lands it.
