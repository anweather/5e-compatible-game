using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Dresses the robot as a wizard (docs/WORLD_LIFE.md, "Wizards"): a pointed hat
// with a brim and a band, a beard, a robe that flares to the shins, bell
// sleeves and sometimes a staff in the left hand, all from a WizardLook. The
// parts are flat-shaded MeshKit meshes drawn with VillageLit (colours in their
// vertices; the occluder dither cuts them like the village), each hung on one
// of the Starter Assets rig's bones, so the animations, the casting gestures
// and every IK pose carry them with no skinning. Shapeshift hides them with the
// wizard. The local player's seed is kept in PlayerPrefs ("New look" in the
// Esc menu draws another); online the owner sends it and every peer dresses
// that wizard the same (NetWizard).
// After the poses (SitPose and the rest) and the robe's cloth: the staff and hat are set by pose last.
[DefaultExecutionOrder(310)]
[RequireComponent(typeof(Animator))]
public class WizardOutfit : MonoBehaviour
{
    const string SeedKey = "wizard.look";
    const int SectionOrder = 480; // the Esc menu: before Controls
    const int Sides = 12;

    [Tooltip("What the parts are drawn with (VillageLit: colours in the vertices).")]
    public Material material;

    /// <summary>The look worn now (null until dressed).</summary>
    public WizardLook Look { get; private set; }

    readonly List<GameObject> parts = new();
    Animator animator;
    int pending = int.MinValue;

    /// <summary>The seed this machine's player keeps (a new one the first time).</summary>
    public static int SavedSeed
    {
        get
        {
            if (!PlayerPrefs.HasKey(SeedKey)) PlayerPrefs.SetInt(SeedKey, WizardLook.NewSeed());
            return PlayerPrefs.GetInt(SeedKey);
        }
    }

    void Awake() => animator = GetComponent<Animator>();

    void OnEnable() => PauseMenu.AddSection(DrawSection, SectionOrder);

    void OnDisable() => PauseMenu.RemoveSection(DrawSection);

    // "New look" in the Esc menu, for the local player's own wizard only.
    void DrawSection()
    {
        if (WizardRig.FindLocal() != gameObject) return;
        if (PauseMenu.Button("New look", PauseMenu.ButtonStyle)) Reroll();
    }

    IEnumerator Start()
    {
        // A frame in, the rig stands in its idle pose: the staff is set upright in the hand from it.
        yield return null;
        if (pending == int.MinValue && Look == null && GetComponent<NetWizard>() == null) pending = SavedSeed;
        if (pending != int.MinValue) Dress(pending);
    }

    /// <summary>Dresses the wizard in the look of this seed (again, if it changed).</summary>
    public void Wear(int seed)
    {
        if (Look != null && Look.Seed == seed) return;
        if (animator == null || !isActiveAndEnabled || Time.frameCount < 2) pending = seed;
        else Dress(seed);
    }

    /// <summary>The local player draws a new look: saved, worn, and sent to everyone online.</summary>
    public void Reroll()
    {
        int seed = WizardLook.NewSeed();
        PlayerPrefs.SetInt(SeedKey, seed);
        PlayerPrefs.Save();
        Wear(seed);
        if (TryGetComponent<NetWizard>(out var net)) net.SetLook(seed);
    }

    void Dress(int seed)
    {
        pending = int.MinValue;
        foreach (var p in parts) Destroy(p);
        staff = hat = null;
        parts.Clear();
        Look = WizardLook.From(seed);
        if (animator == null || material == null) return;
        var kit = new MeshKit { Occlusion = 0f };
        Hat(kit, Bone(HumanBodyBones.Head));
        Beard(kit, Bone(HumanBodyBones.Head));
        var chest = Bone(HumanBodyBones.Chest);
        if (RigidRobe) Bodice(kit, chest != null ? chest : Bone(HumanBodyBones.Spine));
        else SkinnedBodice();
        Skirt(kit, Bone(HumanBodyBones.Hips));
        Sleeve(kit, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        Sleeve(kit, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        Staff(kit, Bone(HumanBodyBones.LeftHand));
    }

    Transform Bone(HumanBodyBones b) => animator.isHuman ? animator.GetBoneTransform(b) : null;

    // --- the staff and hat by pose (Andrew: no staff through the body, no crushed hat) --------------

    Transform staff, hat;
    // The staff's length and its grip, in the hand's space as built.
    Vector3 staffAxis, staffGrip;
    Sitter sitter;
    // How far a moving staff leans out from the body (so its foot clears the leg), and how far the
    // hat tips forward over the face when lying back.
    const float StaffLeanOut = 0.2f, HatTip = 45f;

    void LateUpdate()
    {
        if (staff == null && hat == null) return;
        if (sitter == null) TryGetComponent(out sitter);
        var seat = sitter != null ? sitter.Seated : null;
        if (staff != null)
        {
            staff.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (seat == null) HoldStaff((Vector3.up - transform.right * StaffLeanOut).normalized, null);
            else if (seat.lounge) HoldStaff(seat.Facing * Vector3.forward, Bone(HumanBodyBones.Hips).position - transform.right * 0.32f + Vector3.up * 0.05f);
        }
        if (hat != null)
        {
            hat.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            var head = Bone(HumanBodyBones.Head);
            if (seat != null && seat.lounge && head != null) hat.RotateAround(head.position, transform.right, HatTip);
        }
    }

    // Turns the staff to lie along <paramref name="along"/> (its top that way), its grip in the hand
    // or at <paramref name="grip"/>.
    void HoldStaff(Vector3 along, Vector3? grip)
    {
        var axis = staff.TransformDirection(staffAxis);
        staff.rotation = Quaternion.FromToRotation(axis, along) * staff.rotation;
        var at = grip ?? staff.parent.TransformPoint(staffGrip);
        staff.position += at - staff.TransformPoint(staffGrip);
    }

    // The part just hung, if it's the one named (Hang skips a part with no bone).
    Transform Last(string name) => parts.Count > 0 && parts[^1].name == name ? parts[^1].transform : null;

    static readonly int SteadyGlow = Shader.PropertyToID("_SteadyGlow");

    // Builds what the kit holds into a part on a bone, then empties the kit.
    void Hang(MeshKit kit, Transform bone, string name, bool steadyGlow = false)
    {
        if (bone == null || kit.VertexCount == 0)
        {
            kit.Clear();
            return;
        }
        var go = new GameObject("Outfit " + name);
        go.transform.SetParent(bone, false);
        go.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh("Wizard " + name);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        if (steadyGlow)
        {
            // A glow carried about: steady, no window flicker (VillageLit's _SteadyGlow).
            var block = new MaterialPropertyBlock();
            block.SetFloat(SteadyGlow, 1f);
            r.SetPropertyBlock(block);
        }
        parts.Add(go);
        kit.Clear();
    }

    // --- the parts, each in its bone's own space (metres; the rig's head, chest and hips stand upright) ---

    void Hat(MeshKit kit, Transform head)
    {
        if (head == null) return;
        var look = Look;
        // Down over the head, the brim at the brow: the rig's head rises 0.15 m above its bone, 0.11 m round.
        float crown = 0.09f;
        // The brim: a thin disc, a touch down at the back.
        Tube(kit, new[] { new Vector3(0f, crown - 0.012f, -0.01f), new Vector3(0f, crown + 0.012f, -0.01f) }, new[] { look.Brim, look.Brim }, look.Hat, Ends.Both);
        // The band round the cone's foot.
        Tube(kit, new[] { new Vector3(0f, crown + 0.01f, -0.01f), new Vector3(0f, crown + 0.055f, -0.01f) }, new[] { 0.145f, 0.14f }, look.Band, Ends.None);
        // The cone, rising and then drooping back (and leaning to one side) along a curve.
        const int Rings = 6;
        var spine = new Vector3[Rings + 1];
        var radii = new float[Rings + 1];
        for (int i = 0; i <= Rings; i++)
        {
            float t = i / (float)Rings;
            float fall = look.Droop * t * t;
            spine[i] = new Vector3(look.Bend * 0.12f * t * t, crown + look.HatHeight * t * (1f - 0.55f * fall), -0.01f - look.HatHeight * 0.8f * fall);
            radii[i] = Mathf.Lerp(0.14f, 0.005f, Mathf.Pow(t, 0.85f));
        }
        Tube(kit, spine, radii, look.Hat, Ends.End);
        Hang(kit, head, "hat");
        hat = Last("Outfit hat");
    }

    void Beard(MeshKit kit, Transform head)
    {
        if (head == null || Look.BeardLength <= 0f) return;
        // From the chin, down and a little forward, to a point.
        var chin = new Vector3(0f, -0.04f, 0.075f);
        Tube(kit, new[] { chin, chin + new Vector3(0f, -Look.BeardLength * 0.6f, 0.03f), chin + new Vector3(0f, -Look.BeardLength, 0.02f) },
            new[] { 0.07f, 0.055f, 0.004f }, Look.Beard, Ends.Both);
        Hang(kit, head, "beard");
    }

    // The bodice as clothing, not a shell (Andrew: "rough"): slim elliptical rings from the waist up
    // the spine to the shoulders and a collar up the neck, skinned to the rig's hips, spine, chest,
    // upper chest and neck (and, at the shoulders' sides, the upper arms), so it bends with the
    // torso: a wizard lying back has it lying along the backrest. Built in the idle pose, in the
    // body's own space; each vertex blends the bones of the two rings it lies between.
    void SkinnedBodice()
    {
        var hips = Bone(HumanBodyBones.Hips);
        var spine = Bone(HumanBodyBones.Spine);
        var chest = Bone(HumanBodyBones.Chest);
        var upper = Bone(HumanBodyBones.UpperChest);
        var neck = Bone(HumanBodyBones.Neck);
        var leftArm = Bone(HumanBodyBones.LeftUpperArm);
        var rightArm = Bone(HumanBodyBones.RightUpperArm);
        if (hips == null || chest == null || neck == null || leftArm == null || rightArm == null) return;
        if (spine == null) spine = hips;
        if (upper == null) upper = chest;
        var root = transform;
        Vector3 At(Transform t) => root.InverseTransformPoint(t.position);
        float shoulderY = (At(leftArm).y + At(rightArm).y) / 2f;
        // Each ring: its centre on the spine, half its width, its depth as a share of that, its bone.
        var rings = new (Vector3 at, float half, float depth, Transform bone)[]
        {
            (At(hips) + Vector3.up * 0.06f, 0.175f, 1f, hips),
            (At(chest), 0.165f, 0.8f, chest),
            (At(upper), 0.17f, 0.72f, upper),
            (Vector3.Lerp(At(upper), new Vector3(At(upper).x, shoulderY, At(upper).z), 0.55f), 0.18f, 0.7f, upper),
            (new Vector3(At(neck).x, shoulderY, At(upper).z), 0.19f, 0.62f, upper),
            (At(neck) + Vector3.down * 0.01f, 0.085f, 0.9f, neck),
            (At(neck) + Vector3.up * 0.05f, 0.065f, 1f, neck),
        };
        const int sides = 14;
        var points = new Vector3[rings.Length][];
        for (int i = 0; i < rings.Length; i++)
        {
            points[i] = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float a = 2f * Mathf.PI * k / sides;
                var (at, half, depth, _) = rings[i];
                points[i][k] = at + new Vector3(Mathf.Cos(a) * half, 0f, Mathf.Sin(a) * half * depth);
            }
        }
        var kit = new MeshKit { Occlusion = 0f };
        for (int i = 0; i + 1 < rings.Length; i++)
        {
            var colour = i >= rings.Length - 2 ? Look.Trim : Look.Robe;
            for (int k = 0; k < sides; k++)
            {
                int n = (k + 1) % sides;
                Vector3 a = points[i][k], b = points[i][n], c = points[i + 1][n], d = points[i + 1][k];
                kit.Quad(a, b, c, d, colour, (a + c) / 2f - (rings[i].at + rings[i + 1].at) / 2f);
            }
        }
        var mesh = kit.ToMesh("Wizard bodice");
        var bones = new[] { hips, spine, chest, upper, neck, leftArm, rightArm };
        int Index(Transform t) => System.Array.IndexOf(bones, t);
        var verts = mesh.vertices;
        var weights = new BoneWeight[verts.Length];
        var heights = System.Array.ConvertAll(rings, r => r.at.y);
        var ringBones = System.Array.ConvertAll(rings, r => Index(r.bone));
        var shoulders = (centre: rings[4].at.x, half: rings[4].half, y: shoulderY, left: Index(leftArm), right: Index(rightArm));
        for (int v = 0; v < verts.Length; v++) weights[v] = Weigh(verts[v], heights, ringBones, shoulders);
        mesh.boneWeights = weights;
        var binds = new Matrix4x4[bones.Length];
        for (int j = 0; j < bones.Length; j++) binds[j] = bones[j].worldToLocalMatrix * root.localToWorldMatrix;
        mesh.bindposes = binds;
        var go = new GameObject("Outfit bodice");
        go.transform.SetParent(root, false);
        var skin = go.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = mesh;
        skin.bones = bones;
        skin.rootBone = hips;
        skin.sharedMaterial = material;
        parts.Add(go);
    }

    // A bodice vertex's bones: the two rings it lies between, by height; at the shoulders' sides, a
    // share to that side's upper arm.
    static BoneWeight Weigh(Vector3 p, float[] heights, int[] ringBones, (float centre, float half, float y, int left, int right) shoulders)
    {
        int below = 0;
        while (below + 2 < heights.Length && p.y > heights[below + 1]) below++;
        float t = Mathf.InverseLerp(heights[below], heights[below + 1], p.y);
        var w = new BoneWeight { boneIndex0 = ringBones[below], weight0 = 1f - t, boneIndex1 = ringBones[below + 1], weight1 = t };
        float side = (p.x - shoulders.centre) / shoulders.half;
        float arm = Mathf.Clamp01((Mathf.Abs(side) - 0.55f) / 0.45f) * Mathf.Clamp01(1f - Mathf.Abs(p.y - shoulders.y) / 0.12f) * 0.4f;
        if (arm <= 0f) return w;
        w.weight0 *= 1f - arm;
        w.weight1 *= 1f - arm;
        w.boneIndex2 = side < 0f ? shoulders.left : shoulders.right;
        w.weight2 = arm;
        return w;
    }

    void Bodice(MeshKit kit, Transform chest)
    {
        if (chest == null) return;
        // Chest bone to the shoulders, with a trim collar.
        // Wide at the shoulders (the rig's shoulder joints stand 0.16 m out), so they stay under the cloth.
        Tube(kit, new[] { new Vector3(0f, -0.17f, 0f), new Vector3(0f, 0.12f, 0f), new Vector3(0f, 0.26f, -0.02f), new Vector3(0f, 0.31f, -0.02f) },
            new[] { 0.165f, 0.205f, 0.25f, 0.14f }, Look.Robe, Ends.End);
        Tube(kit, new[] { new Vector3(0f, 0.29f, -0.01f), new Vector3(0f, 0.34f, -0.01f) }, new[] { 0.10f, 0.075f }, Look.Trim, Ends.None);
        Hang(kit, chest, "bodice");
    }

    /// <summary>
    /// Off: the skirt is the old rigid shell instead of cloth (RobeCloth). For before-and-after
    /// comparisons only (the cloth probe); not reset between Play sessions on purpose.
    /// </summary>
    public static bool RigidRobe;

    // The cloth's rings: 14 round (a little finer than the rigid parts), from the waist to the hem.
    const int ClothSides = 14;

    void Skirt(MeshKit kit, Transform hips)
    {
        if (hips == null) return;
        if (!RigidRobe)
        {
            ClothSkirt();
            return;
        }
        float hem = -Look.RobeLength;
        Tube(kit, new[] { new Vector3(0f, 0.10f, 0f), new Vector3(0f, -0.10f, 0f), new Vector3(0f, hem * 0.6f, 0f), new Vector3(0f, hem, 0f) },
            new[] { 0.165f, 0.19f, Mathf.Lerp(0.19f, Look.RobeFlare, 0.6f), Look.RobeFlare }, Look.Robe, Ends.None, inside: true);
        // A trim band at the hem.
        Tube(kit, new[] { new Vector3(0f, hem + 0.05f, 0f), new Vector3(0f, hem, 0f) }, new[] { Look.RobeFlare * 0.985f + 0.004f, Look.RobeFlare + 0.006f }, Look.Trim, Ends.None, inside: true);
        Hang(kit, hips, "robe");
    }

    // The skirt as cloth: rings from the waist (two pinned to the hips) down to a trim band at the
    // hem, flaring as the rigid robe does.
    void ClothSkirt()
    {
        float hem = -Look.RobeLength;
        var heights = new List<float> { 0.10f, -0.10f };
        var radii = new List<float> { 0.165f, 0.19f };
        const int Middle = 4;
        float trimTop = hem + 0.05f;
        for (int i = 1; i <= Middle; i++)
        {
            float t = i / (float)(Middle + 1);
            heights.Add(Mathf.Lerp(-0.10f, trimTop, t));
            radii.Add(Mathf.Lerp(0.19f, Look.RobeFlare, Mathf.Sqrt(t)));
        }
        heights.Add(trimTop);
        radii.Add(Look.RobeFlare * 0.985f);
        heights.Add(hem);
        radii.Add(Look.RobeFlare);

        var go = new GameObject("Outfit robe");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.AddComponent<RobeCloth>().Init(animator, heights.ToArray(), radii.ToArray(), ClothSides, Look.Robe, Look.Trim);
        parts.Add(go);
    }

    // A sleeve down the upper arm, then a bell from the elbow, open at the wrist.
    void Sleeve(MeshKit kit, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand)
    {
        Transform u = Bone(upper), l = Bone(lower), h = Bone(hand);
        if (u == null || l == null || h == null) return;
        var elbow = u.InverseTransformPoint(l.position);
        Tube(kit, new[] { Vector3.zero, elbow * 1.02f }, new[] { 0.075f, 0.07f }, Look.Robe, Ends.None);
        Hang(kit, u, "sleeve " + upper);
        var wrist = l.InverseTransformPoint(h.position);
        Tube(kit, new[] { Vector3.zero, wrist * 0.85f, wrist * 1.05f }, new[] { 0.07f, 0.13f, 0.14f }, Look.Robe, Ends.None, inside: true);
        Tube(kit, new[] { wrist * 0.98f, wrist * 1.06f }, new[] { 0.139f, 0.142f }, Look.Trim, Ends.None, inside: true);
        Hang(kit, l, "bell " + lower);
    }

    // Upright in the left hand in the idle pose: a gnarled branch, a crystal-topped rod or an orb-topped rod.
    void Staff(MeshKit kit, Transform hand)
    {
        if (hand == null || Look.Staff == WizardLook.StaffKind.None) return;
        var up = hand.InverseTransformDirection(Vector3.up);
        var grip = hand.InverseTransformPoint(hand.position + transform.forward * 0.03f);
        var wood = new Color(0.36f, 0.24f, 0.14f, 0f); // alpha 0: VillageLit's glow off
        var foot = grip - up * 0.95f;
        var top = grip + up * 0.75f;
        if (Look.Staff == WizardLook.StaffKind.Gnarled)
        {
            var side = Vector3.Cross(up, hand.InverseTransformDirection(transform.right)).normalized;
            var knot = top + side * 0.06f;
            Tube(kit, new[] { foot, grip, grip + up * 0.4f + side * 0.02f, knot, knot + up * 0.08f - side * 0.1f }, new[] { 0.022f, 0.024f, 0.026f, 0.034f, 0.012f }, wood, Ends.Both);
        }
        else
        {
            Tube(kit, new[] { foot, top }, new[] { 0.02f, 0.022f }, wood, Ends.Both);
            Tube(kit, new[] { top - up * 0.02f, top + up * 0.04f }, new[] { 0.03f, 0.03f }, Look.Trim, Ends.None);
            var glow = new Color(Look.StaffGlow.r, Look.StaffGlow.g, Look.StaffGlow.b, 0.85f); // alpha: VillageLit's glow
            if (Look.Staff == WizardLook.StaffKind.Crystal)
                Tube(kit, new[] { top + up * 0.03f, top + up * 0.11f, top + up * 0.24f }, new[] { 0.01f, 0.05f, 0.002f }, glow, Ends.Both, sides: 6);
            else
                Tube(kit, Arc(top + up * 0.1f, up, 0.07f), OrbRadii(0.07f), glow, Ends.Both);
        }
        staffAxis = up;
        staffGrip = grip;
        Hang(kit, hand, "staff", steadyGlow: true);
        staff = Last("Outfit staff");
    }

    static Vector3[] Arc(Vector3 centre, Vector3 up, float r)
    {
        var pts = new Vector3[7];
        for (int i = 0; i < pts.Length; i++) pts[i] = centre + up * (-r * Mathf.Cos(Mathf.PI * i / (pts.Length - 1)));
        return pts;
    }

    static float[] OrbRadii(float r)
    {
        var radii = new float[7];
        for (int i = 0; i < radii.Length; i++) radii[i] = Mathf.Max(0.002f, r * Mathf.Sin(Mathf.PI * i / (radii.Length - 1)));
        return radii;
    }

    [System.Flags]
    enum Ends { None = 0, Start = 1, End = 2, Both = Start | End }

    // A tube along a curve: a ring of Sides corners at each point, faces between rings (outward,
    // and inward too for a skirt or a sleeve you can see into), flat caps on the ends asked for.
    static void Tube(MeshKit kit, Vector3[] spine, float[] radii, Color color, Ends ends, bool inside = false, int sides = Sides)
    {
        var rings = Rings(spine, radii, sides);
        for (int i = 0; i + 1 < spine.Length; i++)
            for (int k = 0; k < sides; k++)
            {
                int n = (k + 1) % sides;
                Vector3 a = rings[i][k], b = rings[i][n], c = rings[i + 1][n], d = rings[i + 1][k];
                var outward = (a + b + c + d) / 4f - (spine[i] + spine[i + 1]) / 2f;
                kit.Quad(a, b, c, d, color, outward);
                if (inside) kit.Quad(a, b, c, d, color * 0.6f, -outward);
            }
        int last = spine.Length - 1;
        if ((ends & Ends.End) != 0) Cap(kit, spine[last], rings[last], spine[last] - spine[last - 1], color);
        if ((ends & Ends.Start) != 0) Cap(kit, spine[0], rings[0], spine[0] - spine[1], color);
    }

    // The rings round each point of a curve, each turned as little as it can from the one before.
    static Vector3[][] Rings(Vector3[] spine, float[] radii, int sides)
    {
        var rings = new Vector3[spine.Length][];
        var side = Vector3.zero;
        for (int i = 0; i < spine.Length; i++)
        {
            var along = (spine[Mathf.Min(i + 1, spine.Length - 1)] - spine[Mathf.Max(i - 1, 0)]).normalized;
            if (side == Vector3.zero)
            {
                var across = Mathf.Abs(along.z) < 0.9f ? Vector3.forward : Vector3.right;
                side = Vector3.Cross(along, across).normalized;
            }
            else side = Vector3.ProjectOnPlane(side, along).normalized;
            var other = Vector3.Cross(along, side);
            rings[i] = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float a = 2f * Mathf.PI * k / sides;
                rings[i][k] = spine[i] + (side * Mathf.Cos(a) + other * Mathf.Sin(a)) * radii[i];
            }
        }
        return rings;
    }

    static void Cap(MeshKit kit, Vector3 centre, Vector3[] ring, Vector3 outward, Color color)
    {
        for (int k = 0; k < ring.Length; k++) kit.Tri(centre, ring[k], ring[(k + 1) % ring.Length], color, outward);
    }
}
