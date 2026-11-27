using System.Collections.Generic;
using System.Linq;
using Chem;
using Punchy.Fauna;
using Unity.Mathematics;
using UnityEngine;

// The bodies of a mod's new kinds of thing (docs/RULES_MCP.md "Things"). Such a
// kind has no prefab of its own: ThingSpawner spawns a generic one and this
// dresses it from the look on its rules row, family "creature": the body of a
// fauna species, in the look's colour in place of its coat, with its glow, at its
// scale. The prop is a box the size of that body, so it rests, gets pushed and
// sits in a hand like any thing; its own renderer draws nothing.
//
// It stands still, so a body is posed once, on flat ground, and only moved to
// where its box is each frame: all of them in one call, after the hands have
// moved (CreatureDrawer, with ValleyFauna's creature material). Each peer dresses
// its own copy from the same rules, and that includes the box's size, which
// NetworkTransform doesn't send.
[DefaultExecutionOrder(130)]
public class ThingBodies : MonoBehaviour, ICreatureGround
{
    const string Family = "creature";
    const float MinSide = 0.1f;

    sealed class Dressed
    {
        public PhysicsProp prop;
        public SegmentInstance[] pose;
        public Vector3 centre;
    }

    readonly List<Dressed> dressed = new();
    readonly Dictionary<string, CreatureBody> bodies = new();
    readonly CreatureAnimator animator = new();
    SegmentInstance[] instances = new SegmentInstance[64];
    CreatureDrawer drawer;

    public int Count => dressed.Count;

    public static LookDef LookOf(Rules rules, string kind)
    {
        var look = rules.Entity(kind)?.Look ?? rules.Item(kind)?.Look;
        return look?.Family == Family ? look : null;
    }

    public void ReadRules(Rules rules)
    {
        bodies.Clear();
        dressed.RemoveAll(d => d.prop == null);
        foreach (var d in dressed) Fit(d, rules);
    }

    public void Dress(PhysicsProp prop)
    {
        var d = new Dressed { prop = prop };
        dressed.Add(d);
        Fit(d, prop.grid.World.Rules);
    }

    void Fit(Dressed d, Rules rules)
    {
        var look = LookOf(rules, d.prop.kind);
        var body = look != null ? BodyOf(look.Species, rules) : null;
        if (body == null) return;
        var size = body.Size;
        var state = new CreatureState { Scale = (size.x + size.y) * 0.5f * look.Scale, Mode = CreatureMode.Puppet, Seed = 1u };
        var scratch = new SegmentInstance[body.Segments.Length];
        int shown = animator.Evaluate(body, state, Coat(body, look), this, scratch, 0);
        d.pose = scratch.Take(shown).ToArray();
        for (int i = 0; i < shown; i++) d.pose[i].Color.w = math.saturate(d.pose[i].Color.w + look.Glow);
        var box = BoxOf(d.pose);
        d.centre = box.center;
        d.prop.transform.localScale = Vector3.Max(box.size, Vector3.one * MinSide);
    }

    CreatureBody BodyOf(string species, Rules rules)
    {
        if (bodies.TryGetValue(species ?? "", out var body)) return body;
        if (!rules.Fauna.SpeciesIds.Contains(species)) return null;
        return bodies[species] = CreatureBody.Build(rules.Fauna, species);
    }

    static CreatureTraits Coat(CreatureBody body, LookDef look)
    {
        var traits = body.TraitsOf(0, 0u);
        if (look.R == 1f && look.G == 1f && look.B == 1f) return traits;
        var colour = (Vector4)new Color(look.R, look.G, look.B).linear;
        traits.Worn.Base = traits.Worn.Accent = colour;
        for (int i = 0; traits.OwnWorn != null && i < traits.OwnWorn.Length; i++) traits.OwnWorn[i].Base = traits.OwnWorn[i].Accent = colour;
        return traits;
    }

    static Bounds BoxOf(SegmentInstance[] pose)
    {
        var box = new Bounds(pose[0].Position, Vector3.zero);
        foreach (var s in pose)
        {
            float round = Mathf.Max(s.RadiusStart, s.RadiusEnd) * Mathf.Max(s.Flatten, 1f);
            var side = Vector3.one * (2f * round);
            box.Encapsulate(new Bounds(s.Position, side));
            box.Encapsulate(new Bounds(s.Position + math.mul(s.Rotation, new float3(0f, 0f, s.Length)), side));
        }
        return box;
    }

    void LateUpdate()
    {
        dressed.RemoveAll(d => d.prop == null);
        int total = dressed.Sum(d => d.pose?.Length ?? 0);
        if (instances.Length < total) instances = new SegmentInstance[Mathf.NextPowerOfTwo(total)];
        var cam = Camera.main;
        int count = 0;
        foreach (var d in dressed)
        {
            if (d.pose == null || !d.prop.gameObject.activeInHierarchy) continue;
            var turn = d.prop.transform.rotation;
            var at = d.prop.transform.position - turn * d.centre;
            int first = count;
            foreach (var s in d.pose)
            {
                var placed = s;
                placed.Position = at + turn * (Vector3)s.Position;
                placed.Rotation = math.mul(turn, s.Rotation);
                instances[count++] = placed;
            }
            CreatureDrawer.HideAtLens(instances, first, count - first, cam);
        }
        if (count == 0) return;
        drawer ??= ValleyFauna.InPlay is { } fauna ? new CreatureDrawer(fauna.material) : null;
        drawer?.Draw(instances, count, cam != null ? cam.transform.position : Vector3.zero);
    }

    void OnDestroy() => drawer?.Dispose();

    public float Height(float2 xz) => 0f;
    public bool Walkable(float2 xz) => true;
    public bool Water(float2 xz) => false;
}
