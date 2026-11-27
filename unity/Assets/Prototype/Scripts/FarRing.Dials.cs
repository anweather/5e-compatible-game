using System;
using Chem;
using UnityEngine;

// Two of the three tier dials (docs/design/world-12x8-scope.md, "The three tiers today"), this peer's own view: how far
// round the characters the chunk terrains draw (drawn, the known ring's sight: past it a chunk is a known mesh) and how far
// the known ring reaches (known, which sets the haze too on a map with no far land: ~98% at its edge). Unset, each is sim.json's streaming sight and
// know, as before the dials. The third, the active chunks, is lockstep state: streaming reach and keep, changed only as a
// rule switch (the host's), never here. A player takes them as -drawn N and -known N, for perf runs.
public sealed partial class FarRing
{
    public static int? ViewDrawn { get; private set; }

    public static int? ViewKnown { get; private set; }

    public (int drawn, int known) Dials => (sight, know);

    int IFarLandFace.KnownChunks => know;

    public bool Settled => ring != null && !ring.Busy && building == null && ring.Version == builtVersion;

    public static void SetView(int? drawn, int? known)
    {
        ViewKnown = known.HasValue ? Mathf.Max(1, known.Value) : null;
        ViewDrawn = drawn.HasValue ? Mathf.Max(0, drawn.Value) : null;
        var rules = ChemGrid.Current?.Rules;
        if (Instance != null && Instance.ring != null && rules != null) Instance.Dial(rules.Sim.Streaming);
    }

    static void DialsFromArgs()
    {
        var args = Environment.GetCommandLineArgs();
        int? Arg(string name)
        {
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int n) ? n : null;
        }
        int? drawn = Arg("-drawn"), known = Arg("-known");
        if (drawn.HasValue || known.HasValue) SetView(drawn, known);
    }

    void Dial(StreamingParams streaming)
    {
        int k = ViewKnown ?? streaming.Know, s = Mathf.Min(ViewDrawn ?? streaming.Sight, k);
        if (ring != null && k == know && s == sight) return;
        know = k;
        sight = s;
        ring = new KnownRing(map.Gen, s, k);
        builtVersion = -1;
        characters.Clear();
        HazeMetres = know * chunkMetres;
        if (terrains != null) terrains.ReachNow();
    }
}
