Needs: unity
Area: the wizard's body and clothes (the legs/pants mesh and its visibility), ProbeSpellLookFx.cs and the look rules
path for element colours; no core changes unless the colours bug is there
Branch: pants-and-colours

# Pants that vanish, and #167's two colour checks (the lead, 2026-11-08 overnight; given to the PC)

1. **B2, pants disappear** (`docs/playtests/2026-11-08-human-results.md`; seen in earlier playtests too). Andrew: minor,
   "we might just work around that ... don't wanna spend a ton of time". Timebox it to about an hour: find when the
   wizard's pants stop drawing (a material, a renderer turned off by a shapeshift, gear or swim, LOD, or the bounds
   sinking like peer-vanish's bones). Fix it if the cause is clear; otherwise make a workaround that keeps them drawn
   (e.g. re-enable the renderer each frame it should show) and say what you found. A probe check that the pants
   renderer is on and in view after the usual actions (swim, shapeshift and back, casting, a reset).
2. **#167, lookfx 101/103:** two element-colour checks (`ProbeSpellLookFx.cs:114, :121`, J-052) fail since `set_look`
   on an element also colours its spells. Decide from J-052 in `JUDGMENT_CALLS.md` whether the game or the probe is
   right, fix the wrong one, and close #167 with a comment if lookfx passes in full.

Before landing: `dotnet test core`, EditMode N/N in full, `lookfx`, your pants check, `smoke`, `swim`, `shapeshift`.
A short clip only if the pants change shows. Push the branch only, remove this claim on the branch in the last
commit. The lead lands it.
