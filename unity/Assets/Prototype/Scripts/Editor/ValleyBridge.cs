using UnityEngine;

// The wooden bridge over the river (ValleyShape.Bridge), where the way from
// the yard and the ball court to the cabin on the river's east bank crosses
// it: a plank deck on two beams standing clear over the water on legs set in
// the banks, a gentle ramp down to each bank, and posts with a handrail along
// both sides. Built from primitives like the cabins and the court's rails,
// around unseen boxes the wizard walks on and is kept on the bridge by.
// Nothing stands in the water, so a swimmer passes under it. It isn't in the
// chemistry: the squares under it stay river, so the water runs on beneath.
public static class ValleyBridge
{
    // The deck's width, how high its top stands over the water, how thick the unseen walking boxes
    // are, a plank's depth along the bridge, the handrail's height and the posts' spacing (metres).
    const float DeckWidth = 2.6f, Clearance = 1.7f, Thickness = 0.3f, PlankDepth = 0.34f, RailHeight = 1.05f, PostSpacing = 1.8f;
    // How far a ramp's walking box runs on into the ground past its foot, so it meets the ground without a lip.
    const float Buried = 0.8f;

    // A straight stretch of the walking surface, in the bridge's own space (z across the river, y up,
    // the deck's top at 0): where it starts and ends, and its turn (its z along it, its y square to it).
    readonly struct Section
    {
        public readonly Vector3 From, To, Up;
        public readonly float Length;
        public readonly Quaternion Turn;

        public Section(Vector3 from, Vector3 to)
        {
            From = from;
            To = to;
            Length = Vector3.Distance(from, to);
            Turn = Quaternion.LookRotation(to - from, Vector3.up);
            Up = Turn * Vector3.up;
        }

        public Vector3 Middle => (From + To) / 2f;

        public Vector3 At(float t) => Vector3.Lerp(From, To, t);
    }

    /// <summary>
    /// Builds the bridge, named "Bridge": its root at the middle of the deck's top, its forward from the
    /// west bank to the east, with a "West landing" and an "East landing" on the ground past each ramp.
    /// </summary>
    public static Transform Build(Material wood, Material planks)
    {
        var mid = ValleyShape.Bridge;
        var across = new Vector3(ValleyShape.BridgeAcross.x, 0f, ValleyShape.BridgeAcross.y);
        var centre = new Vector3(mid.x, 0f, mid.y);
        float span = ValleyShape.BridgeSpan, reach = span + ValleyShape.BridgeRamp;
        var westFoot = Grounded(centre - across * reach);
        var eastFoot = Grounded(centre + across * reach);
        // Clear over the river's water (it stands at the banks' level), and a step over the higher foot.
        float deck = Mathf.Max(ValleyShape.BankHeightAt(mid) + Clearance, Mathf.Max(westFoot.y, eastFoot.y) + 0.4f);

        var root = new GameObject("Bridge").transform;
        root.SetPositionAndRotation(new Vector3(mid.x, deck, mid.y), Quaternion.LookRotation(across));
        // The walking surface along the bridge's middle: up the west ramp, over the deck, down the east ramp.
        var line = new[]
        {
            new Vector3(0f, westFoot.y - deck, -reach), new Vector3(0f, 0f, -span),
            new Vector3(0f, 0f, span), new Vector3(0f, eastFoot.y - deck, reach),
        };
        var rng = new System.Random(41);
        for (int k = 0; k + 1 < line.Length; k++)
        {
            var section = new Section(line[k], line[k + 1]);
            Walkway(root, section, k == 0 ? Buried : 0f, k + 2 == line.Length ? Buried : 0f);
            Planks(root, section, planks, rng);
            Sides(root, section, wood, k == 0);
        }
        float rampMiddle = span + ValleyShape.BridgeRamp / 2f;
        foreach (float z in new[] { -rampMiddle, -span, span, rampMiddle }) Legs(root, z, SurfaceAt(line, z), wood);
        Landing(root, "West landing", westFoot - across * 1.5f);
        Landing(root, "East landing", eastFoot + across * 1.5f);
        return root;
    }

    // An unseen box whose top is the section's walking surface, run on past its ends by the given lengths.
    static void Walkway(Transform root, Section s, float before, float after)
    {
        var go = new GameObject("Bridge walkway");
        go.transform.SetParent(root, false);
        var along = s.Turn * Vector3.forward;
        var a = s.From - along * before;
        var b = s.To + along * after;
        go.transform.localPosition = (a + b) / 2f - s.Up * (Thickness / 2f);
        go.transform.localRotation = s.Turn;
        go.AddComponent<BoxCollider>().size = new Vector3(DeckWidth, Thickness, Vector3.Distance(a, b) + 0.02f);
    }

    // The section's planks, laid across it side by side, each a little off true.
    static void Planks(Transform root, Section s, Material mat, System.Random rng)
    {
        int count = Mathf.Max(1, Mathf.RoundToInt(s.Length / PlankDepth));
        float depth = s.Length / count;
        for (int i = 0; i < count; i++)
        {
            var at = s.At((i + 0.5f) / count) - s.Up * 0.05f;
            var turn = s.Turn * Quaternion.Euler(0f, Jitter(rng, 1.5f), 0f);
            Piece(root, PrimitiveType.Cube, "Bridge plank", at, turn, new Vector3(DeckWidth + Jitter(rng, 0.1f), 0.1f, depth * 0.88f), mat);
        }
    }

    // Along both sides of a section: a beam under the planks, posts (the first at its start only on the
    // first section, so no two stand in one place), a handrail and a middle rail, and an unseen wall to
    // the handrail's height that keeps the wizard on the bridge.
    static void Sides(Transform root, Section s, Material wood, bool first)
    {
        int posts = Mathf.Max(1, Mathf.CeilToInt(s.Length / PostSpacing));
        foreach (float side in new[] { -1f, 1f })
        {
            var edge = s.Turn * new Vector3(side * (DeckWidth / 2f - 0.08f), 0f, 0f);
            var beam = s.Turn * new Vector3(side * (DeckWidth / 2f - 0.4f), -0.25f, 0f);
            Piece(root, PrimitiveType.Cube, "Bridge beam", s.Middle + beam, s.Turn, new Vector3(0.22f, 0.3f, s.Length + 0.1f), wood);
            for (int i = first ? 0 : 1; i <= posts; i++)
            {
                var foot = s.At(i / (float)posts) + edge;
                Piece(root, PrimitiveType.Cube, "Bridge post", foot + Vector3.up * (RailHeight / 2f - 0.15f), Quaternion.identity, new Vector3(0.14f, RailHeight + 0.3f, 0.14f), wood);
            }
            foreach (float height in new[] { RailHeight, RailHeight / 2f })
                Piece(root, PrimitiveType.Cube, "Bridge rail", s.Middle + edge + Vector3.up * height, s.Turn, new Vector3(0.1f, 0.1f, s.Length + 0.14f), wood);
            var guard = new GameObject("Bridge side");
            guard.transform.SetParent(root, false);
            guard.transform.localPosition = s.Middle + edge + Vector3.up * (RailHeight / 2f);
            guard.transform.localRotation = s.Turn;
            guard.AddComponent<BoxCollider>().size = new Vector3(0.14f, RailHeight, s.Length);
        }
    }

    // Two legs at a point along the bridge (in its space), from under the beams down into the ground.
    static void Legs(Transform root, float z, float surface, Material wood)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            var top = new Vector3(side * (DeckWidth / 2f - 0.4f), surface - 0.4f, z);
            var world = root.TransformPoint(top);
            float length = world.y - (ValleyTerrain.GroundAt(world) - 0.3f);
            if (length < 0.2f) continue;
            Piece(root, PrimitiveType.Cylinder, "Bridge leg", top + Vector3.down * (length / 2f), Quaternion.identity, new Vector3(0.28f, length / 2f, 0.28f), wood);
        }
    }

    // The walking surface's height at a point along the bridge (its space), between the line's points.
    static float SurfaceAt(Vector3[] line, float z)
    {
        for (int k = 0; k + 1 < line.Length; k++)
        {
            if (z > line[k + 1].z) continue;
            return Mathf.Lerp(line[k].y, line[k + 1].y, Mathf.InverseLerp(line[k].z, line[k + 1].z, z));
        }
        return line[line.Length - 1].y;
    }

    // A marker on the ground where the way onto the bridge starts (the probe walks from one to the other).
    static void Landing(Transform root, string name, Vector3 at)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.position = Grounded(at);
    }

    static Vector3 Grounded(Vector3 p)
    {
        p.y = ValleyTerrain.GroundAt(p);
        return p;
    }

    static void Piece(Transform root, PrimitiveType type, string name, Vector3 at, Quaternion turn, Vector3 scale, Material mat)
    {
        var go = Primitives.Visual(type, name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = at;
        go.transform.localRotation = turn;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static float Jitter(System.Random rng, float r) => ((float)rng.NextDouble() * 2f - 1f) * r;
}
