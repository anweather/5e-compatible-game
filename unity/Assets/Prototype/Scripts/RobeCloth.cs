using System.Collections.Generic;
using Chem;
using Unity.Profiling;
using UnityEngine;

// The robe's skirt as cloth (docs/WORLD_LIFE.md, "Wizards"; J-026's prototype):
// a ring grid of particles from the waist to the hem, its top two rings pinned
// to the hips, moved by position-based (Verlet) steps at a fixed 60 a second
// with distance constraints round each ring, down each line and across, and a
// gentle pull back to the robe's flared shape. It collides with capsules on the
// legs and a sphere at the hips (so the legs no longer poke through it), the
// ground under the feet, and a seat's top when the wizard sits; in water it
// floats and drags; the weather's wind and the wizard's own moves sway it. The
// mesh is rebuilt from the particles every frame, smooth-shaded like the bodice
// (VillageLit). It snaps back to rest after a jump of more than 2 m
// (a teleport, Misty Step, the online reset), runs at a third of the rate far
// off and stops while off screen. View only: every peer runs its own, nothing
// is shared.
// After the poses (SitPose, SwimPose, RollPose, CastPose) have set the bones, so it hangs on them.
[DefaultExecutionOrder(300)]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RobeCloth : MonoBehaviour
{
    const float Step = 1f / 60f;
    const int MaxStepsPerFrame = 3, Iterations = 4;
    // Velocity kept per step (air drag), and in water.
    const float Damping = 0.985f, WaterDamping = 0.8f;
    const float Gravity = 9.81f, WaterLift = 7f;
    // How much of the way back to its rest shape a particle goes each step: keeps the robe's flare.
    const float ShapeKeep = 0.025f;
    // The cloth's half-thickness against what it touches, and how far it may stretch.
    const float Skin = 0.02f, MaxStretch = 1.15f;
    const float SnapDistance = 2f, FarDistance = 25f;
    // The share of the hips' move each step that the cloth takes on at once.
    const float Follow = 0.8f;
    // The legs and hips it falls round (radii in metres).
    // A little wider than the legs themselves: the cloth's faces sag between its particles.
    const float ThighRadius = 0.115f, ShinRadius = 0.09f, HipsRadius = 0.15f;
    // The feet among the legs' bones.
    static readonly int[] Feet = { 2, 5 };

    static readonly ProfilerMarker StepMarker = new("RobeCloth.Step");

    Transform hips;
    // Left thigh, knee, foot, then right.
    readonly Transform[] legs = new Transform[6];
    Sitter sitter;
    SurfaceEffects surface;
    ChemGrid grid;
    MeshRenderer look;
    Mesh mesh;

    int sides, rings;
    Vector3[] rest;          // in the hips' space
    // The rest shape from the hips in the body's own upright frame (its heading, not the hips' tilt):
    // what the cloth hangs toward, so a tilted pelvis (sitting, lying back) lets it fall, not stick out.
    Vector3[] hang;
    // The particles of the robe's front panel (within 60° of straight ahead), kept in front of the legs.
    bool[] frontPanel;
    Transform body;
    Vector3[] pos, prev;     // in the world
    struct Link { public int a, b; public float length; public bool vertical; }
    readonly List<Link> links = new();
    Vector3[] vertices, normals;
    bool[] flipped;
    float clock;
    Vector3 lastHips, lastStepHips;
    bool ready;

    /// <summary>How far the cloth's farthest particle is from a point (the probe checks it holds together).</summary>
    public float Farthest(Vector3 from)
    {
        float most = 0f;
        if (pos != null) foreach (var p in pos) most = Mathf.Max(most, Vector3.Distance(p, from));
        return most;
    }

    /// <summary>The steps run so far (the probe checks it's moving).</summary>
    public int Steps { get; private set; }

    /// <summary>
    /// Builds the cloth on the hips: rings at these heights (in the hips' space, the first two
    /// pinned), these radii, <paramref name="sides"/> particles round each; robe-coloured, the last
    /// row in the trim colour.
    /// </summary>
    public void Init(Animator animator, float[] heights, float[] radii, int sides, Color robe, Color trim)
    {
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var bones = new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };
        for (int i = 0; i < bones.Length; i++) legs[i] = animator.GetBoneTransform(bones[i]);
        animator.TryGetComponent(out sitter);
        animator.TryGetComponent(out surface);
        grid = FindAnyObjectByType<ChemGrid>();
        look = GetComponent<MeshRenderer>();
        this.sides = sides;
        rings = heights.Length;
        int n = rings * sides;
        rest = new Vector3[n];
        for (int i = 0; i < rings; i++)
            for (int k = 0; k < sides; k++)
            {
                float a = 2f * Mathf.PI * k / sides;
                rest[i * sides + k] = new Vector3(Mathf.Cos(a) * radii[i], heights[i], Mathf.Sin(a) * radii[i]);
            }
        pos = new Vector3[n];
        prev = new Vector3[n];
        body = animator.transform;
        hang = new Vector3[n];
        frontPanel = new bool[n];
        for (int i = 0; i < n; i++)
        {
            hang[i] = Quaternion.Inverse(body.rotation) * (hips.TransformPoint(rest[i]) - hips.position);
            frontPanel[i] = hang[i].z > 0.5f * new Vector2(hang[i].x, hang[i].z).magnitude;
        }
        Snap();
        MakeLinks();
        MakeMesh(robe, trim);
        ready = hips != null;
    }

    int At(int ring, int k) => ring * sides + (k % sides + sides) % sides;

    bool Pinned(int i) => i < 2 * sides;

    // Round each ring, down each line, across each quad both ways, and two rings down (bending).
    void MakeLinks()
    {
        links.Clear();
        for (int i = 0; i < rings; i++)
            for (int k = 0; k < sides; k++)
            {
                Add(At(i, k), At(i, k + 1), false);
                if (i + 1 < rings)
                {
                    Add(At(i, k), At(i + 1, k), true);
                    Add(At(i, k), At(i + 1, k + 1), false);
                    Add(At(i, k + 1), At(i + 1, k), false);
                }
                if (i + 2 < rings) Add(At(i, k), At(i + 2, k), true);
            }

        void Add(int a, int b, bool vertical)
        {
            if (Pinned(a) && Pinned(b)) return;
            links.Add(new Link { a = a, b = b, length = Vector3.Distance(pos[a], pos[b]), vertical = vertical });
        }
    }

    // Everything back to its rest shape where the hips are now.
    void Snap()
    {
        for (int i = 0; i < pos.Length; i++) pos[i] = prev[i] = hips.TransformPoint(rest[i]);
        lastHips = lastStepHips = hips.position;
    }

    void LateUpdate()
    {
        if (!ready) return;
        if (Vector3.Distance(hips.position, lastHips) > SnapDistance) Snap();
        lastHips = hips.position;
        if (!look.isVisible) return;
        var cam = Camera.main;
        bool far = cam != null && Vector3.Distance(cam.transform.position, hips.position) > FarDistance;
        clock += Mathf.Min(Time.deltaTime, Step * MaxStepsPerFrame * (far ? 3 : 1));
        float step = far ? Step * 3 : Step;
        using (StepMarker.Auto())
        {
            while (clock >= step)
            {
                clock -= step;
                Simulate(step);
                Steps++;
            }
        }
        Draw();
    }

    // The bones read once a step (a transform read per particle per pass cost most of the time).
    Matrix4x4 hipsMatrix;
    Vector3 hipsAt;
    Quaternion bodyTurn;
    readonly Vector3[] legAt = new Vector3[6];
    readonly bool[] legFound = new bool[2];

    void ReadBones()
    {
        hipsMatrix = hips.localToWorldMatrix;
        hipsAt = hips.position;
        bodyTurn = body.rotation;
        for (int s = 0; s < 2; s++)
        {
            legFound[s] = legs[s * 3] != null && legs[s * 3 + 1] != null && legs[s * 3 + 2] != null;
            if (!legFound[s]) continue;
            for (int j = 0; j < 3; j++) legAt[s * 3 + j] = legs[s * 3 + j].position;
        }
    }

    bool seated;
    // How long it has been seated: just sat down, the cloth is settled onto the lap (the legs swing
    // up through it as the wizard sits, which would leave it caught under the thighs).
    float seatedFor;
    const float Settle = 0.8f, SettleRate = 0.2f;

    // Seated, the lap: the hip joints, knees and feet between both legs, and how long the thighs and shins are.
    Vector3 lapHip, lapKnee, lapFoot, lapRight;
    float lapThigh, lapShin;
    bool lapFound;

    void ReadLap()
    {
        lapFound = seated && legFound[0] && legFound[1];
        if (!lapFound) return;
        lapHip = (legAt[0] + legAt[3]) / 2f;
        lapKnee = (legAt[1] + legAt[4]) / 2f;
        lapFoot = (legAt[2] + legAt[5]) / 2f;
        lapThigh = Vector3.Distance(lapHip, lapKnee);
        lapShin = Vector3.Distance(lapKnee, lapFoot);
        lapRight = bodyTurn * Vector3.right;
    }

    // Where a particle is pulled back to: the robe's shape hanging from the hips in the body's upright
    // frame (so a tilted pelvis, sitting or lying back, lets it fall onto the seat). Seated, the front
    // goes over the lap instead (Andrew: cover the thighs to the knee, then down the shins).
    Vector3 Target(int i)
    {
        var hangs = hipsAt + bodyTurn * hang[i];
        float front = Front(i);
        return front > 0f ? Vector3.Lerp(hangs, OverLap(i), front) : hangs;
    }

    // How much of a particle is the robe's front (none at its back), seated with the lap found.
    float Front(int i)
    {
        if (!lapFound) return 0f;
        var h = hang[i];
        float across = new Vector2(h.x, h.z).magnitude;
        return across < 1e-4f ? 0f : Mathf.Clamp01((h.z / across + 0.3f) / 0.8f);
    }

    // A front particle laid along the legs as far down the robe as it hangs: over the thighs to the
    // knees, then over the shins (down a bench's front, along a lounger), just clear of them.
    Vector3 OverLap(int i)
    {
        // From where its line leaves the waist (the pinned ring above it), so the lap doesn't fold back.
        float d = rest[sides].y - rest[i].y + Mathf.Max(0f, Vector3.Dot(pos[sides + i % sides] - lapHip, (lapKnee - lapHip).normalized));
        Vector3 from, way;
        float along;
        if (d < lapThigh) (from, way, along) = (lapHip, lapKnee - lapHip, d);
        else if (d < lapThigh + lapShin) (from, way, along) = (lapKnee, lapFoot - lapKnee, d - lapThigh);
        else (from, way, along) = (lapFoot, Vector3.down, d - lapThigh - lapShin);
        way.Normalize();
        var clear = Vector3.Cross(way, lapRight).normalized * (ThighRadius + Skin);
        return from + way * along + clear + lapRight * (hang[i].x * 0.8f);
    }

    // How hard a particle is pulled back: firm near the waist, loose at the hem (so the legs win); seated,
    // the back only gently (so it falls onto the seat and over its edge), the front over the lap firmly.
    float Keep(int i)
    {
        float k = Mathf.Lerp(1.5f, 0.35f, i / sides / (float)(rings - 1));
        return seated ? k * Mathf.Lerp(0.25f, 3f, Front(i)) : k;
    }

    void Simulate(float dt)
    {
        ReadBones();
        var accel = Vector3.down * Gravity + Wind();
        float waterY = WaterTop();
        var seat = sitter != null ? sitter.Seated : null;
        seated = seat != null;
        seatedFor = seated ? seatedFor + dt : 0f;
        bool settling = seated && seatedFor < Settle;
        ReadLap();
        // Most of the hips' own move goes straight to the cloth, so running it swings round the legs
        // instead of streaming behind like a cape.
        var carried = (hipsAt - lastStepHips) * Follow;
        lastStepHips = hipsAt;
        for (int i = 0; i < pos.Length; i++)
        {
            if (Pinned(i))
            {
                prev[i] = pos[i] = hipsMatrix.MultiplyPoint3x4(rest[i]);
                continue;
            }
            bool wet = pos[i].y < waterY;
            var v = (pos[i] - prev[i]) * (wet ? WaterDamping : Damping);
            pos[i] += carried;
            prev[i] = pos[i];
            pos[i] += v + (accel + (wet ? Vector3.up * WaterLift : Vector3.zero)) * (dt * dt);
            pos[i] += (Target(i) - pos[i]) * (ShapeKeep * Keep(i));
            if (settling) prev[i] = pos[i] = Vector3.Lerp(pos[i], Target(i), SettleRate);
        }
        float ground = Ground();
        for (int it = 0; it < Iterations; it++)
        {
            foreach (var link in links) Hold(link);
            Collide(ground);
        }
    }

    // Pulls a linked pair back to its length (a pinned end doesn't move); a line down the robe
    // stretched past MaxStretch is pulled all the way.
    void Hold(Link link)
    {
        var d = pos[link.b] - pos[link.a];
        float len = d.magnitude;
        if (len < 1e-6f) return;
        float k = link.vertical && len > link.length * MaxStretch ? 1f : 0.8f;
        var fix = d * ((len - link.length) / len * k);
        bool pa = Pinned(link.a), pb = Pinned(link.b);
        if (pa) pos[link.b] -= fix;
        else if (pb) pos[link.a] += fix;
        else
        {
            pos[link.a] += fix * 0.5f;
            pos[link.b] -= fix * 0.5f;
        }
    }

    void Collide(float ground)
    {
        var seat = sitter != null ? sitter.Seated : null;
        var ahead = bodyTurn * Vector3.forward;
        var right = bodyTurn * Vector3.right;
        for (int i = 2 * sides; i < pos.Length; i++)
        {
            var p = OutOfSphere(pos[i], hipsAt, HipsRadius);
            // Seated, the robe's front lies over the lap: caught by a thigh, it goes up onto it. Up, the
            // front panel always goes out in front of a leg, never back through it (Andrew: the front
            // leg came through the robe at a run).
            p = OffLegs(p, WayOffLegs(i, seat != null), ahead, right);
            if (seat != null) p = OffSeat(p, seat);
            if (p.y < ground) p.y = ground;
            pos[i] = p;
        }
    }

    // Which way cloth leaves a leg: the nearest way, up over it (a lap), or out in front of it.
    enum LegWay { Round, Over, Ahead }

    LegWay WayOffLegs(int i, bool sitting)
    {
        if (sitting) return hang[i].z > 0f ? LegWay.Over : LegWay.Round;
        return frontPanel[i] ? LegWay.Ahead : LegWay.Round;
    }

    // Out of both legs: thigh and shin capsules.
    Vector3 OffLegs(Vector3 p, LegWay way, Vector3 ahead, Vector3 right)
    {
        for (int s = 0; s < 2; s++)
        {
            if (!legFound[s]) continue;
            var thigh = legAt[s * 3];
            var knee = legAt[s * 3 + 1];
            var foot = legAt[s * 3 + 2];
            p = way switch
            {
                LegWay.Over => OntoCapsule(p, thigh, knee, ThighRadius),
                LegWay.Ahead => InFrontOfCapsule(p, thigh, knee, ThighRadius, ahead, right),
                _ => OutOfCapsule(p, thigh, knee, ThighRadius),
            };
            p = way == LegWay.Ahead ? InFrontOfCapsule(p, knee, foot, ShinRadius, ahead, right) : OutOfCapsule(p, knee, foot, ShinRadius);
        }
        return p;
    }

    // Out of a capsule toward the body's front, from wherever it is beside it (so cloth that slipped
    // behind a striding leg is brought back in front); the nearest way where the leg points ahead.
    static Vector3 InFrontOfCapsule(Vector3 p, Vector3 a, Vector3 b, float r, Vector3 ahead, Vector3 right)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        var c = a + ab * t;
        var d = p - c;
        float min = r + Skin;
        float side = Vector3.Dot(d, right);
        if (Mathf.Abs(side) >= min) return p;
        var axis = ab.normalized;
        var front = ahead - axis * Vector3.Dot(ahead, axis);
        if (front.sqrMagnitude < 0.04f) return OutOfSphere(p, c, r);
        front.Normalize();
        float need = Mathf.Sqrt(min * min - side * side), now = Vector3.Dot(d, front);
        return now >= need ? p : p + front * (need - now);
    }

    static Vector3 OutOfSphere(Vector3 p, Vector3 centre, float r)
    {
        var d = p - centre;
        float len = d.magnitude, min = r + Skin;
        return len < min && len > 1e-5f ? centre + d * (min / len) : p;
    }

    static Vector3 OutOfCapsule(Vector3 p, Vector3 a, Vector3 b, float r)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return OutOfSphere(p, a + ab * t, r);
    }

    // Out of a capsule by going up over it: to the top of the capsule above the nearest point. Past
    // its ends (in front of a knee) just out of it, so cloth there falls on past rather than up.
    static Vector3 OntoCapsule(Vector3 p, Vector3 a, Vector3 b, float r)
    {
        var ab = b - a;
        float t = Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f);
        if (t <= 0f || t >= 1f) return OutOfSphere(p, t <= 0f ? a : b, r);
        var c = a + ab * t;
        var d = p - c;
        float min = r + Skin;
        if (d.sqrMagnitude >= min * min) return p;
        float across = new Vector2(d.x, d.z).magnitude;
        if (across >= min) return OutOfSphere(p, c, r);
        p.y = c.y + Mathf.Sqrt(min * min - across * across);
        return p;
    }

    // A seat's top, as a box under its seat point (a bench's plank, or a lounger's whole length):
    // cloth inside it goes up onto the top.
    static Vector3 OffSeat(Vector3 p, Seat seat)
    {
        var t = seat.transform;
        var local = t.InverseTransformPoint(p);
        // Under the thighs (the seat point is where the hips go, level with them), so cloth caught
        // between leg and seat stays there instead of being squeezed up over the lap.
        var top = seat.seatAt.y - ThighRadius - 0.04f;
        float halfX = seat.lounge ? 0.35f : 0.7f, halfZ = seat.lounge ? 1.0f : 0.25f;
        if (Mathf.Abs(local.x - seat.seatAt.x) > halfX || Mathf.Abs(local.z - seat.seatAt.z) > halfZ) return p;
        if (local.y > top + Skin || local.y < top - 0.35f) return p;
        local.y = top + Skin;
        return t.TransformPoint(local);
    }

    // Just under the lower foot: the ground, read from the rig once a step (no terrain lookups).
    float Ground()
    {
        bool any = false;
        float y = 0f;
        foreach (int foot in Feet)
        {
            if (legs[foot] == null) continue;
            y = any ? Mathf.Min(y, legs[foot].position.y) : legs[foot].position.y;
            any = true;
        }
        return any ? y - 0.06f + Skin : float.MinValue;
    }

    // The water's top while the wizard swims (read once a step), else far below.
    float WaterTop()
    {
        if (surface == null || !surface.Swimming || grid == null || grid.World == null) return float.MinValue;
        var cell = grid.CellAt(hips.position);
        return grid.WaterSurfaceY(cell.x, cell.y);
    }

    // The weather's wind on the cloth: its way (World.WindDir: toward north, east, south, west),
    // strong in a gale or a storm.
    Vector3 Wind()
    {
        var world = grid != null ? grid.World : null;
        if (world == null || world.WindDir < 0) return Vector3.zero;
        var now = world.Weather;
        float strength = now.Kind == WeatherKind.Windy || now.Kind == WeatherKind.Storm ? now.Strength / 100f : 0.25f;
        var dir = world.WindDir switch { 0 => Vector3.forward, 1 => Vector3.right, 2 => Vector3.back, _ => Vector3.left };
        float gust = 0.7f + 0.3f * Mathf.Sin(Time.time * 1.7f + hips.position.x);
        return dir * (4f * strength * gust);
    }

    // --- the mesh: two faces per quad (outside, and a darker inside) --------------------------

    void MakeMesh(Color robe, Color trim)
    {
        int quads = (rings - 1) * sides;
        vertices = new Vector3[quads * 8];
        normals = new Vector3[quads * 8];
        flipped = new bool[quads];
        var colours = new Color[quads * 8];
        var tris = new int[quads * 12];
        for (int i = 0; i + 1 < rings; i++)
            for (int k = 0; k < sides; k++) SetUpQuad(i, k, i + 2 == rings ? trim : robe, colours, tris);
        mesh = new Mesh { name = "Wizard robe (cloth)" };
        mesh.MarkDynamic();
        Fill();
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.colors = colours;
        mesh.triangles = tris;
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    // One quad's colours (outside, and a darker inside) and its two faces' triangles, wound so the
    // outside faces out.
    void SetUpQuad(int i, int k, Color colour, Color[] colours, int[] tris)
    {
        int q = i * sides + k;
        Quad(i, k, out var a, out var b, out var c, out _);
        var outward = (pos[a] + pos[c]) / 2f - hips.TransformPoint(new Vector3(0f, rest[a].y, 0f));
        flipped[q] = Vector3.Dot(Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]), outward) < 0f;
        var outside = Linear(colour);
        var inside = Linear(colour * 0.6f);
        int o = q * 8, t = q * 12;
        for (int v = 0; v < 4; v++)
        {
            colours[o + v] = outside;
            colours[o + 4 + v] = inside;
        }
        var front = flipped[q] ? FlippedFace : Face;
        for (int j = 0; j < 6; j++)
        {
            tris[t + j] = o + front[j];
            tris[t + 6 + j] = o + 4 + front[5 - j];
        }
    }

    static readonly int[] Face = { 0, 1, 2, 0, 2, 3 }, FlippedFace = { 0, 3, 2, 0, 2, 1 };

    // Colours are picked in sRGB; the shader works in linear (as MeshKit does).
    static Color Linear(Color c) =>
        QualitySettings.activeColorSpace == ColorSpace.Linear ? new Color(c.linear.r, c.linear.g, c.linear.b, c.a) : c;

    void Quad(int i, int k, out int a, out int b, out int c, out int d)
    {
        a = At(i, k);
        b = At(i, k + 1);
        c = At(i + 1, k + 1);
        d = At(i + 1, k);
    }

    // The particles into the mesh, in this object's space, each corner with the normal of the quads
    // round its particle averaged (smooth shading, Andrew: not faceted).
    void Fill()
    {
        var space = transform;
        smooth ??= new Vector3[pos.Length];
        local ??= new Vector3[pos.Length];
        for (int p = 0; p < pos.Length; p++)
        {
            local[p] = space.InverseTransformPoint(pos[p]);
            smooth[p] = Vector3.zero;
        }
        for (int i = 0; i + 1 < rings; i++)
            for (int k = 0; k < sides; k++)
            {
                Quad(i, k, out var a, out var b, out var c, out var d);
                // Outward: the a-b-c face's normal, turned round where the quad's winding was flipped.
                var n = Vector3.Cross(local[b] - local[a], local[c] - local[a]);
                if (flipped[i * sides + k]) n = -n;
                smooth[a] += n;
                smooth[b] += n;
                smooth[c] += n;
                smooth[d] += n;
            }
        for (int p = 0; p < pos.Length; p++) smooth[p].Normalize();
        for (int i = 0; i + 1 < rings; i++)
            for (int k = 0; k < sides; k++)
            {
                int o = (i * sides + k) * 8;
                Quad(i, k, out var a, out var b, out var c, out var d);
                Corner(o, a);
                Corner(o + 1, b);
                Corner(o + 2, c);
                Corner(o + 3, d);
            }

        void Corner(int v, int p)
        {
            vertices[v] = vertices[v + 4] = local[p];
            normals[v] = smooth[p];
            normals[v + 4] = -smooth[p];
        }
    }

    Vector3[] smooth, local;

    void Draw()
    {
        Fill();
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.RecalculateBounds();
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }
}
