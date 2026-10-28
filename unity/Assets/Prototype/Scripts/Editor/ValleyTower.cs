using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Kind = TowerPlatform.Kind;
using Motion = TowerPlatform.Motion;

// The climbing tower south-west of the yard (docs/BACKLOG.md 16; TowerCourse
// runs it): a stone column about 37 m tall, ringed by a spiral of platforms
// that climbs it anticlockwise, about four turns, from a start pad with a sign
// on the yard's side to a flag on the column's top. Static stone steps first,
// then moving (sliding, lifting, circling), collapsing and phasing platforms
// mixed in, with wider gaps that need the double jump (DoubleJump) higher up.
// Straw mats round the foot mark the soft landing (a fall does no harm).
//
// It stands on the open meadow about 80 m from the yard's middle: clear of
// the village to the north-west, the cabins, the lake to the south-east, the
// river and the court far to the east, and the coast further south. Its 13 m
// round is kept clear of trees, rocks and grass (ValleyShape.NearTower).
// Built from primitives and lit materials, like the bridge and the court.
public static class ValleyTower
{
    const string Generated = "Assets/Prototype/Generated";

    /// <summary>The middle of the column on the ground plane (x, z).</summary>
    public static readonly Vector2 Site = new Vector2(-60f, -20f);

    /// <summary>How far round the column everything reaches (m): the platforms, the mats and the start pad.</summary>
    public const float Reach = 13f;

    // The platforms' ring round the column's middle, the column's radius and the summit's (m); the
    // first platform's bearing (degrees anticlockwise from east, toward the yard), the start pad's
    // distance out, and how high the summit stands over the last platform.
    const float Ring = 6.5f, ColumnRadius = 3f, SummitRadius = 4.2f, StartBearing = 20f, PadOut = 10.5f, LastRise = 1f;
    const float Thickness = 0.5f, PadRadius = 1.6f;

    // One step of the climb: its kind (and motion, if it moves), how far round from the last (degrees)
    // and how far above it (m). Single jumps: a gap up to about 1.6 m and a rise under a metre; the
    // wide ones (50 degrees and more, 1.6 m and more up) need the double jump.
    static readonly (Kind kind, Motion motion, float turn, float rise)[] Steps =
    {
        (Kind.Static, Motion.Slide, 0f, 0.8f), (Kind.Static, Motion.Slide, 32f, 0.8f), (Kind.Static, Motion.Slide, 32f, 0.8f),
        (Kind.Static, Motion.Slide, 34f, 0.9f), (Kind.Moving, Motion.Slide, 36f, 0.9f), (Kind.Static, Motion.Slide, 36f, 0.9f),
        (Kind.Static, Motion.Slide, 32f, 0.9f), (Kind.Collapsing, Motion.Slide, 34f, 0.9f), (Kind.Static, Motion.Slide, 34f, 0.9f),
        (Kind.Moving, Motion.Lift, 36f, 0.9f), (Kind.Static, Motion.Slide, 36f, 0.9f), (Kind.Collapsing, Motion.Slide, 34f, 0.9f),
        (Kind.Collapsing, Motion.Slide, 34f, 0.9f), (Kind.Static, Motion.Slide, 34f, 0.9f), (Kind.Phasing, Motion.Slide, 34f, 0.9f),
        (Kind.Static, Motion.Slide, 34f, 0.9f), (Kind.Moving, Motion.Round, 38f, 0.9f), (Kind.Static, Motion.Slide, 38f, 0.9f),
        (Kind.Static, Motion.Slide, 50f, 1.6f), (Kind.Phasing, Motion.Slide, 36f, 0.9f), (Kind.Phasing, Motion.Slide, 36f, 0.9f),
        (Kind.Static, Motion.Slide, 34f, 0.9f), (Kind.Collapsing, Motion.Slide, 36f, 1f), (Kind.Moving, Motion.Slide, 38f, 0.9f),
        (Kind.Static, Motion.Slide, 38f, 0.9f), (Kind.Static, Motion.Slide, 52f, 1.7f), (Kind.Collapsing, Motion.Slide, 38f, 1f),
        (Kind.Collapsing, Motion.Slide, 38f, 1f), (Kind.Phasing, Motion.Slide, 38f, 1f), (Kind.Moving, Motion.Lift, 38f, 0.9f),
        (Kind.Static, Motion.Slide, 38f, 0.9f), (Kind.Phasing, Motion.Slide, 40f, 1f), (Kind.Collapsing, Motion.Slide, 40f, 1f),
        (Kind.Moving, Motion.Round, 40f, 0.9f), (Kind.Static, Motion.Slide, 54f, 1.8f), (Kind.Collapsing, Motion.Slide, 40f, 1f),
        (Kind.Phasing, Motion.Slide, 40f, 1f), (Kind.Static, Motion.Slide, 36f, 0.9f),
    };

    sealed class Looks
    {
        public Material stone, column, moving, crumbly, phase, ghost, straw, flag, start, wood;
    }

    /// <summary>Builds the tower, named "Tower", with its TowerCourse.</summary>
    public static TowerCourse Build(ChemGrid grid)
    {
        var looks = MakeMaterials();
        var root = new GameObject("Tower").transform;
        float ground = Ground(Site);
        root.position = new Vector3(Site.x, ground, Site.y);
        var course = root.gameObject.AddComponent<TowerCourse>();
        course.grid = grid;

        var platforms = new List<TowerPlatform>();
        float bearing = StartBearing, top = ground;
        int phasing = 0;
        for (int i = 0; i < Steps.Length; i++)
        {
            var (kind, motion, turn, rise) = Steps[i];
            bearing += turn;
            top += rise;
            var p = Platform(root, course, (kind, motion), Around(bearing, Ring, top), bearing, i, looks);
            // Each phasing platform a little behind the one before: it turns solid while that one still is and
            // stays solid a while after it fades, so the way on is always there for a moment.
            if (kind == Kind.Phasing)
            {
                p.phase = phasing * 0.7f % 1f;
                phasing++;
            }
            platforms.Add(p);
        }
        course.platforms = platforms.ToArray();

        float summit = top + LastRise;
        Column(root, ground, summit, looks);
        course.summit = new Vector3(Site.x, summit, Site.y);
        course.fireworksAt = course.summit + Vector3.up * 12f;
        Flag(root, course.summit, looks);
        course.startPad = StartPad(root, looks);
        course.padRadius = PadRadius;
        Mats(root, looks.straw);
        return course;
    }

    // A point at a bearing (degrees anticlockwise from east) and a distance from the column's middle, at a height.
    static Vector3 Around(float bearing, float distance, float y)
    {
        float a = bearing * Mathf.Deg2Rad;
        return new Vector3(Site.x + distance * Mathf.Cos(a), y, Site.y + distance * Mathf.Sin(a));
    }

    static float Ground(Vector2 p) => ValleyTerrain.GroundAt(new Vector3(p.x, 0f, p.y));

    // One platform, its top at the point: the box the wizard stands on, facing out from the column
    // (local z outward, x along the ring), and its drawn block.
    static TowerPlatform Platform(Transform root, TowerCourse course, (Kind kind, Motion motion) type, Vector3 topAt, float bearing, int index, Looks looks)
    {
        var (kind, motion) = type;
        float side = kind switch { Kind.Static => 2.6f, Kind.Phasing => 2.4f, _ => 2.2f };
        var size = new Vector3(side, Thickness, side);
        var outward = topAt - new Vector3(Site.x, topAt.y, Site.y);
        var go = new GameObject($"Tower platform {index + 1} ({kind})");
        go.transform.SetParent(root, true);
        go.transform.SetPositionAndRotation(topAt + Vector3.down * (Thickness / 2f), Quaternion.LookRotation(outward.normalized));
        go.AddComponent<BoxCollider>().size = size;

        var look = Primitives.Visual(PrimitiveType.Cube, "Tower platform look");
        look.transform.SetParent(go.transform, false);
        look.transform.localScale = size;
        var p = go.AddComponent<TowerPlatform>();
        p.course = course;
        p.kind = kind;
        p.motion = motion;
        p.look = look.transform;
        p.phase = index * 0.37f % 1f;
        p.solid = kind switch { Kind.Moving => looks.moving, Kind.Collapsing => looks.crumbly, Kind.Phasing => looks.phase, _ => looks.stone };
        p.ghost = looks.ghost;
        look.GetComponent<Renderer>().sharedMaterial = p.solid;
        float a = bearing * Mathf.Deg2Rad;
        p.axis = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
        (p.amplitude, p.period) = motion switch { Motion.Lift => (0.8f, 5f), Motion.Round => (1f, 6f), _ => (1.2f, 4f) };
        if (kind == Kind.Phasing) p.period = 5f;
        return p;
    }

    // The stone column from under the ground to the summit, with darker bands every few metres so its
    // height reads from afar, and the wider summit on top.
    static void Column(Transform root, float ground, float summit, Looks looks)
    {
        float bottom = ground - 1f, height = summit - 0.4f - bottom;
        Solid(root, "Tower column", new Vector3(Site.x, bottom + height / 2f, Site.y), new Vector3(ColumnRadius * 2f, height / 2f, ColumnRadius * 2f), looks.column);
        for (float y = ground + 6f; y < summit - 2f; y += 6f)
            Piece(root, PrimitiveType.Cylinder, "Tower band", new Vector3(Site.x, y, Site.y), new Vector3(ColumnRadius * 2f + 0.3f, 0.2f, ColumnRadius * 2f + 0.3f), looks.stone);
        Solid(root, "Tower summit", new Vector3(Site.x, summit - 0.2f, Site.y), new Vector3(SummitRadius * 2f, 0.2f, SummitRadius * 2f), looks.stone);
    }

    // The flag on the summit: a pole, a gold knob, and a red flag flying toward the yard.
    static void Flag(Transform root, Vector3 foot, Looks looks)
    {
        const float pole = 5f;
        Piece(root, PrimitiveType.Cylinder, "Tower flagpole", foot + Vector3.up * (pole / 2f), new Vector3(0.16f, pole / 2f, 0.16f), looks.wood);
        Piece(root, PrimitiveType.Sphere, "Tower flag knob", foot + Vector3.up * (pole + 0.1f), Vector3.one * 0.3f, looks.straw);
        var toYard = Around(StartBearing, 1f, 0f) - new Vector3(Site.x, 0f, Site.y);
        var flag = Piece(root, PrimitiveType.Cube, "Tower flag", foot + Vector3.up * (pole - 0.6f) + toYard * 1f, new Vector3(0.05f, 1.1f, 1.9f), looks.flag);
        flag.transform.rotation = Quaternion.LookRotation(toYard);
    }

    // The start pad on the yard's side, with a sign beside it; returns the middle of the pad's top.
    static Vector3 StartPad(Transform root, Looks looks)
    {
        var at = Around(StartBearing, PadOut, 0f);
        at.y = ValleyTerrain.GroundAt(at) + 0.25f;
        Solid(root, "Tower start pad", at + Vector3.down * 0.2f, new Vector3(PadRadius * 2f, 0.2f, PadRadius * 2f), looks.start);

        var sign = Around(StartBearing - 14f, PadOut + 0.4f, 0f);
        sign.y = ValleyTerrain.GroundAt(sign);
        var facing = Quaternion.LookRotation(new Vector3(Site.x - sign.x, 0f, Site.y - sign.z));
        var board = new GameObject("Tower sign").transform;
        board.SetParent(root, true);
        board.SetPositionAndRotation(sign, facing);
        foreach (float x in new[] { -0.8f, 0.8f })
            Local(board, PrimitiveType.Cylinder, new Vector3(x, 0.9f, 0f), new Vector3(0.12f, 0.9f, 0.12f), looks.wood);
        Local(board, PrimitiveType.Cube, new Vector3(0f, 1.55f, 0f), new Vector3(2f, 1f, 0.08f), looks.wood);
        Words(board, "THE TOWER\nClimb to the flag!\nJump again in the air\nfor a double jump");
        return at;
    }

    // Writing on the sign's face (toward whoever reads it, the board's -z side).
    static void Words(Transform board, string text)
    {
        var go = new GameObject("Tower sign words");
        go.transform.SetParent(board, false);
        go.transform.localPosition = new Vector3(0f, 1.55f, -0.06f);
        SignWords.Write(go, text, 0.035f, 1.8f, 0.85f);
    }

    // Straw mats in a ring round the column's foot: where a fall from the tower lands (only a look;
    // falls do no harm).
    static void Mats(Transform root, Material straw)
    {
        const int count = 22;
        const float middle = 7.5f, length = 5f;
        for (int i = 0; i < count; i++)
        {
            float bearing = i * 360f / count;
            var at = Around(bearing, middle, 0f);
            at.y = ValleyTerrain.GroundAt(at) + 0.04f;
            var outward = at - new Vector3(Site.x, at.y, Site.y);
            var mat = Piece(root, PrimitiveType.Cube, "Tower mat", at, new Vector3(2 * Mathf.PI * middle / count - 0.15f, 0.1f, length), straw);
            mat.transform.rotation = Quaternion.LookRotation(outward.normalized);
        }
    }

    // A drawn cylinder the wizard can stand on and bump into (its own mesh as the collider, flat on top).
    static void Solid(Transform root, string name, Vector3 at, Vector3 scale, Material mat)
    {
        var go = Piece(root, PrimitiveType.Cylinder, name, at, scale, mat);
        go.AddComponent<MeshCollider>();
    }

    static GameObject Piece(Transform root, PrimitiveType type, string name, Vector3 at, Vector3 scale, Material mat)
    {
        var go = Primitives.Visual(type, name);
        go.transform.SetParent(root, true);
        go.transform.position = at;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static void Local(Transform parent, PrimitiveType type, Vector3 at, Vector3 scale, Material mat)
    {
        var go = Piece(parent, type, "Tower sign part", parent.position, scale, mat);
        go.transform.SetLocalPositionAndRotation(at, Quaternion.identity);
    }

    static Looks MakeMaterials()
    {
        var violet = new Color(0.62f, 0.38f, 0.98f);
        var looks = new Looks
        {
            stone = Lit("TowerStone", new Color(0.62f, 0.6f, 0.56f), 0.15f),
            column = Lit("TowerColumn", new Color(0.46f, 0.45f, 0.43f), 0.1f),
            moving = Lit("TowerMoving", new Color(0.25f, 0.48f, 0.8f), 0.35f, new Color(0.05f, 0.12f, 0.25f)),
            crumbly = Lit("TowerCrumbly", new Color(0.72f, 0.5f, 0.28f), 0.05f),
            phase = Lit("TowerPhase", violet, 0.5f, violet * 0.6f),
            ghost = Lit("TowerPhaseGhost", new Color(violet.r, violet.g, violet.b, 0.22f), 0.5f, violet * 0.25f),
            straw = Lit("TowerStraw", new Color(0.8f, 0.68f, 0.36f), 0.05f),
            flag = Lit("TowerFlag", new Color(0.85f, 0.1f, 0.1f), 0.2f, new Color(0.3f, 0.02f, 0.02f)),
            start = Lit("TowerStart", new Color(0.32f, 0.72f, 0.45f), 0.3f, new Color(0.05f, 0.2f, 0.1f)),
            wood = Lit("TowerWood", new Color(0.42f, 0.29f, 0.18f), 0.1f),
        };
        UrpMaterials.MakeTransparent(looks.ghost);
        EditorUtility.SetDirty(looks.ghost);
        return looks;
    }

    static Material Lit(string name, Color color, float smoothness, Color? emission = null) =>
        EditorAssets.LitMaterial($"{Generated}/{name}.mat", color, smoothness, emission);
}
