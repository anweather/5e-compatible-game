Needs: unity
Area: unity/Assets/Prototype/Scripts/ (the HUD's top-right panel, the play log, input, a dev travel mode), Esc > Settings
Branch: playtest-tools
After: mac-landing-frame (same HUD; the Mac takes both)

# Playtest tools Andrew asked for (#220)

From Andrew's playtest of the nine (2026-11-10, issue #220):
1. **Mark a spot:** F8 already marks a moment in the play log; make it also record the wizard's position (cells and
   world metres), the chunk, the biome and the camera's heading, show a short "marked" toast, and write the marks to
   a `marks-<time>.txt` beside the play log, so the lead can go to each one.
2. **Where am I:** the HUD's top right (beside day, weather and time) shows the cell coordinates, the chunk and the
   biome; toggleable, on in playtest builds.
3. **Move fast:** a dev fly mode (a key, off by default, never in shared state: a peer's view only, or host-only and
   refused online) at several speeds, so he can cross the nine in a minute; terrains and the far ring keep up (the
   drawn tier streams ahead). If a travel spell fits the rules better, say so instead.

EditMode, smoke on the valley and the nine, `online`. Self-land with `--compiled`.
