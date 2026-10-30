using System.Collections.Generic;
using Chem;
using Punchy.Fauna;
using Unity.Mathematics;
using UnityEngine;

// The raiders' look, on every machine (placeholders until Andrew's models):
// each raider kind is drawn as a Punchy creature (CreatureAnimator, the
// valley's animal renderer) puppeted from the core's squares, sliding from
// square to square over the time a step takes so they walk rather than hop.
// Over each, a health bar (IMGUI); where one falls, a puff of dark dust. A
// raider that leaves the board on the flag's square reached it, any other was
// beaten. Only the show: the core decides everything.
public partial class DefenseField : ICreatureGround
{
    [Tooltip("The raider kinds (rules/entities.json ids) and the Punchy creature drawn for each, in the same order.")]
    public string[] kinds = { RaiderIds.BoneHound, RaiderIds.AshCrab, RaiderIds.GiantSpider };
    public CreatureSpecies[] species = System.Array.Empty<CreatureSpecies>();
    [Tooltip("How big each kind is drawn, times its species' size.")]
    public float[] sizes = { 1.2f, 1.6f, 1.4f };
    [Tooltip("The creature material (Punchy/Creature), the valley animals' own.")]
    public Material creatureMaterial;

    sealed class Shown
    {
        public int kind;
        public Vector2Int cell;
        public Vector3 from, to, at;
        public float movedAt, stepSeconds;
        public CreatureState state;
        public int hp, maxHp;
        public Raider raider;
    }

    readonly Dictionary<int, Shown> shown = new();
    readonly List<int> gone = new();
    readonly CreatureAnimator animator = new();
    CreatureBody[] bodies;
    CreatureDrawer drawer;
    SegmentInstance[] instances = new SegmentInstance[256];
    static Material puffMaterial;

    /// <summary>Raiders drawn this frame, and death puffs shown since the start (for the probe).</summary>
    public int DrawnRaiders { get; private set; }
    public int Puffs { get; private set; }

    void Start()
    {
        bodies = new CreatureBody[species.Length];
        for (int i = 0; i < species.Length; i++) bodies[i] = species[i] != null ? CreatureBody.Build(species[i]) : null;
        if (creatureMaterial != null) drawer = new CreatureDrawer(creatureMaterial);
    }

    void OnDestroy() => drawer?.Dispose();

    // Follows the core's raiders: new ones appear, moved ones start sliding, gone ones puff.
    void UpdateRaiders()
    {
        var world = World;
        int tps = Mathf.Max(1, world.Rules.Sim.TicksPerSecond);
        foreach (var s in shown.Values) s.hp = -1;
        foreach (var r in world.Raiders)
        {
            var cell = new Vector2Int(r.X, r.Y);
            if (!shown.TryGetValue(r.Id, out var s))
            {
                var at = Ground(cell);
                s = new Shown { kind = System.Array.IndexOf(kinds, r.Kind), cell = cell, from = at, to = at, at = at, state = NewState(at) };
                SizeUp(s);
                shown[r.Id] = s;
            }
            else if (s.cell != cell)
            {
                s.from = s.at;
                s.to = Ground(cell);
                s.cell = cell;
                s.movedAt = Time.time;
                var def = world.Rules.Entity(r.Kind);
                s.stepSeconds = (def?.Raider != null ? def.Raider.TicksPerSquare : 4) / (float)tps;
            }
            s.raider = r;
            s.hp = Mathf.Max(0, r.Hp);
            s.maxHp = Mathf.Max(1, r.MaxHp);
        }
        Forget(FlagCell);
        Slide();
    }

    // Raiders the core no longer has: a puff where one was beaten. One last seen on the flag's square
    // or next to it got through (it leaves the board on the tick it steps onto the flag).
    void Forget(Vector2Int flagCell)
    {
        gone.Clear();
        foreach (var kv in shown)
            if (kv.Value.hp < 0) gone.Add(kv.Key);
        foreach (int id in gone)
        {
            var s = shown[id];
            shown.Remove(id);
            if (s.raider != null && s.raider.Shattered) Shatter(s.at);
            else if (Mathf.Abs(s.cell.x - flagCell.x) + Mathf.Abs(s.cell.y - flagCell.y) > 1) Puff(s.at);
        }
    }

    // Each raider along its step, facing the way it goes, its gait from how fast.
    void Slide()
    {
        foreach (var s in shown.Values)
        {
            float t = s.stepSeconds > 0f ? Mathf.Clamp01((Time.time - s.movedAt) / s.stepSeconds) : 1f;
            var at = Vector3.Lerp(s.from, s.to, t);
            at.y = grid.GroundAt(at);
            var way = s.to - s.from;
            way.y = 0f;
            float speed = t < 1f && s.stepSeconds > 0f ? way.magnitude / s.stepSeconds : 0f;
            if (way.sqrMagnitude > 1e-4f) s.state.Yaw = Mathf.Atan2(way.x, way.z);
            s.state.Speed = Mathf.Lerp(s.state.Speed, speed, 1f - Mathf.Exp(-8f * Time.deltaTime));
            s.state.Position = at;
            s.state.Clock += Time.deltaTime;
            var b = Body(s.kind);
            if (b != null)
            {
                float stride = Mathf.Max(b.WalkStride * s.state.Scale, 0.05f);
                s.state.Phase = math.frac(s.state.Phase + s.state.Speed * Time.deltaTime / stride);
            }
            s.at = at;
        }
    }

    // After everything has moved: pose every raider and draw them all in one go.
    void LateUpdate()
    {
        DrawnRaiders = 0;
        if (drawer == null || bodies == null) return;
        int used = 0;
        Vector3 centre = Vector3.zero;
        foreach (var s in shown.Values)
        {
            var b = Body(s.kind);
            if (b == null) continue;
            int need = used + b.Segments.Length;
            if (instances.Length < need) System.Array.Resize(ref instances, Mathf.NextPowerOfTwo(need));
            animator.Evaluate(b, s.state, 0, this, instances, used);
            used = need;
            centre = s.at;
            DrawnRaiders++;
        }
        drawer.Draw(instances, used, centre);
    }

    CreatureBody Body(int kind) => bodies != null && kind >= 0 && kind < bodies.Length ? bodies[kind] : null;

    static CreatureState NewState(Vector3 at) => new CreatureState
    {
        Position = at,
        Yaw = Mathf.PI / 2f,
        Scale = 1f,
        Mode = CreatureMode.Puppet,
        Seed = 1u,
    };

    Vector3 Ground(Vector2Int cell)
    {
        var at = grid.CellCenter(cell.x, cell.y);
        at.y = grid.GroundAt(at);
        return at;
    }

    // Sizes the creature from its kind's body: the species' own size, times the kind's.
    void SizeUp(Shown s)
    {
        var b = Body(s.kind);
        if (b == null) return;
        float size = s.kind >= 0 && s.kind < sizes.Length ? sizes[s.kind] : 1f;
        s.state.Scale = (b.Size.x + b.Size.y) * 0.5f * size;
    }

    // --- health bars and puffs ----------------------------------------------------------

    void DrawHealthBars()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var bar = Texture2D.whiteTexture;
        var before = GUI.color;
        foreach (var s in shown.Values)
        {
            var screen = cam.WorldToScreenPoint(s.at + Vector3.up * (1.2f + s.state.Scale * 0.6f));
            if (screen.z <= 0f || screen.z > hudRange) continue;
            const float width = 46f, height = 6f;
            var back = new Rect(screen.x - width / 2f, Screen.height - screen.y, width, height);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(back, bar);
            float f = Mathf.Clamp01(s.hp / (float)s.maxHp);
            GUI.color = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.3f, 0.9f, 0.3f), f);
            GUI.DrawTexture(new Rect(back.x + 1f, back.y + 1f, (width - 2f) * f, height - 2f), bar);
        }
        GUI.color = before;
    }

    // A frozen raider struck: it bursts into ice shards with a crack, instead of the dust of a defeat.
    void Shatter(Vector3 at)
    {
        Puffs++;
        ShardBurst.At(at + Vector3.up * 0.5f, new Color(0.7f, 0.88f, 1f), 40);
        GameSounds.Play(SoundIds.Shatter, at);
    }

    // A short burst of dark dust where a raider fell.
    void Puff(Vector3 at)
    {
        Puffs++;
        var go = new GameObject("Raider puff");
        go.transform.SetParent(transform, false);
        go.transform.position = at + Vector3.up * 0.4f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.2f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.18f, 0.16f, 0.15f, 0.9f), new Color(0.42f, 0.38f, 0.34f, 0.8f));
        main.gravityModifier = -0.05f;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)24) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.4f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) },
        });
        if (puffMaterial == null)
        {
            puffMaterial = UrpMaterials.NewUnlitParticles("Raider puff", Color.white, additive: false);
            var soft = ProceduralTextures.SoftPuff(64);
            if (puffMaterial.HasProperty("_BaseMap")) puffMaterial.SetTexture("_BaseMap", soft);
            puffMaterial.mainTexture = soft;
        }
        var look = go.GetComponent<ParticleSystemRenderer>();
        look.sharedMaterial = puffMaterial;
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        Destroy(go, 2f);
    }

    // --- the ground the animator stands the bodies on (ICreatureGround) ----------------------

    public float Height(float2 xz) => grid != null ? grid.GroundAt(new Vector3(xz.x, 0f, xz.y)) : 0f;

    public bool Walkable(float2 xz) => true;

    public bool Water(float2 xz) => false;
}
