Needs: unity
Area: tools/probe.sh, unity/Assets/Prototype/Scripts/Probe*.cs, unity/Assets/Prototype/Scripts/Editor/Probe*.cs,
ProjectSettings EditorSettings (Enter Play Mode options); docs/FAST_LOOP.md
Branch: probe-speed

# Faster probes (docs/TESTING_LAYERS.md layers 2 and 4; the lead, 2026-11-08)

153 probes; tonight's 30 timed ones averaged about a minute each (hold 269 s, hands 172, landshapes 164, castfeel 106,
stamplooks 102, triggerview 94, holdbodies 89). Each also pays Play mode entry and a scene load. A full sweep is
about 2.4 h.

1. **Enter Play Mode options:** no domain reload (and no scene reload where safe), so entry takes about a second. Fix
   any static state that relied on the reload (reset it on entering Play mode) and prove the probes still pass.
2. **Several scenarios per Play session:** `tools/probe.sh a b c` runs them back to back in one session, resetting the
   world between them, and prints one JSON per scenario.
3. **The slow probes:** run their simulation faster than real time where the check allows (sim ticks per frame up, or
   time scale), and cut `hold` to the waves its checks need.
4. A `sweep` mode: every probe, split into two halves so the PC and the Mac can each run one.
Measure before and after (the 7 slowest and a full sweep). Before landing: EditMode Unity-side tests, every probe once
(that is the sweep), smoke. Push the branch only; the lead lands it.
