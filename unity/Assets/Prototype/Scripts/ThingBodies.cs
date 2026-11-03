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
[DefaultExecutionOrder(130)] // after ItemHolder has put what's held in the hand
public class ThingBodies : MonoBehaviour, ICreatureGround
{
    const string Family = "creature";
    // A box no thinner than this, so a snake still has something to stand on.
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

    /// <summary>How many things wear a body now.</summary>
    public int Count => dressed.Count;

    /// <summary>The look a kind is dressed by, or null if it has none (a kind with its own model).</summary>
    public static LookDef LookOf(Rules rules, string kind)
    {
        var look = rules.Entity(kind)?.Look ?? rules.Item(kind)?.Look;
        return look?.Family == Family ? look : null;
    }

    /// <summary>The rules changed: bodies are built afresh, and every dressed thing reads its look again.</summary>
    public void ReadRules(Rules rules)
    {
        bodies.Clear();
        dressed.RemoveAll(d => d.prop == null);
        foreach (var d in dressed) Fit(d, rules);
    }

    /// <summary>Dresses a generic thing whose kind is set, by the rules in force.</summary>
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

    // The species' first coat as the row has it, the look's colour over it. White is the look's
    // "no colour given", so that keeps the coat.
    static CreatureTraits Coat(CreatureBody body, LookDef look)
    {
        var traits = body.TraitsOf(0, 0u);
        if (look.R == 1f && look.G == 1f && look.B == 1f) return traits;
        var colour = (Vector4)new Color(look.R, look.G, look.B).linear;
        traits.Worn.Base = traits.Worn.Accent = colour;
        // A graft's donors' own coats take the look's colour too: one colour for the whole thing.
        for (int i = 0; traits.OwnWorn != null && i < traits.OwnWorn.Length; i++) traits.OwnWorn[i].Base = traits.OwnWorn[i].Accent = colour;
        return traits;
    }

    // The box round a pose: every segment's two ends, with their radii.
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
        drawer ??= FindAnyObjectByType<ValleyFauna>() is { } fauna ? new CreatureDrawer(fauna.material) : null;
        drawer?.Draw(instances, count, cam != null ? cam.transform.position : Vector3.zero);
    }

    void OnDestroy() => drawer?.Dispose();

    // Posing is on flat ground at the origin, whatever is under the box.
    public float Height(float2 xz) => 0f;
    public bool Walkable(float2 xz) => true;
    public bool Water(float2 xz) => false;
}
