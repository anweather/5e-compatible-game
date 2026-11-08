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
    [Tooltip("The creature material (Punchy/Creature), the valley animals' own.")]
    public Material creatureMaterial;

    sealed class Shown
    {
        public string kind;
        public Vector2Int cell;
        public Vector3 from, to, at;
        public float movedAt, stepSeconds, lift, lunge;
        public Vector3 smooth;
        public bool smoothed;
        public int waitWas;
        public CreatureState state;
        public int hp, maxHp;
        public Raider raider;
        public bool cracked;
    }

    readonly Dictionary<int, Shown> shown = new();
    readonly List<int> gone = new();
    readonly CreatureAnimator animator = new();
    readonly CreatureBodies bodies = new();
    CreatureDrawer drawer;
    SegmentInstance[] instances = new SegmentInstance[256];
    static Material puffMaterial;

    /// <summary>Raiders drawn this frame, and death puffs shown since the start (for the probe).</summary>
    public int DrawnRaiders { get; private set; }
    public int Puffs { get; private set; }

    void Start()
    {
        if (creatureMaterial != null) drawer = new CreatureDrawer(creatureMaterial);
        if (grid != null) grid.RulesChanged += ReadRows;
        ReadRows();
    }

    void ReadRows()
    {
        if (grid != null && grid.CurrentRules != null) bodies.Refresh(grid.CurrentRules.Effective);
    }

    void OnDestroy()
    {
        if (grid != null) grid.RulesChanged -= ReadRows;
        drawer?.Dispose();
    }

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
                s = new Shown { kind = Rules.PlainKind(r.Kind), cell = cell, from = at, to = at, at = at, state = NewState(at) };
                s.raider = r;
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
        // One a spire killed rides it up all the same, and puffs when it comes down.
        foreach (var kv in shown)
            if (kv.Value.hp < 0 && !grid.SpireRide(kv.Key).HasValue) gone.Add(kv.Key);
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
            Lift(s, World);
            // The cells make a staircase of a diagonal: the body takes the corners off it.
            if (!s.smoothed) s.smooth = at;
            s.smoothed = true;
            var before = s.smooth;
            s.smooth = Vector3.Lerp(s.smooth, at, 1f - Mathf.Exp(-Rounding * Time.deltaTime));
            at = s.smooth;
            var way = s.smooth - before;
            way.y = 0f;
            float speed = Time.deltaTime > 0f ? way.magnitude / Time.deltaTime : 0f;
            if (way.sqrMagnitude > 1e-7f) s.state.Yaw = Mathf.LerpAngle(s.state.Yaw * Mathf.Rad2Deg, Mathf.Atan2(way.x, way.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-Turning * Time.deltaTime)) * Mathf.Deg2Rad;
            Struck(s);
            at += new Vector3(Mathf.Sin(s.state.Yaw), 0f, Mathf.Cos(s.state.Yaw)) * (Mathf.Sin(s.lunge * Mathf.PI) * LungeReach);
            at.y = grid.GroundAt(at) + s.lift;
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
        DrawnRaiders = BossesDrawn = 0;
        if (drawer == null || bodies == null) return;
        int used = 0;
        Vector3 centre = Vector3.zero;
        var cam = Camera.main;
        foreach (var s in shown.Values)
        {
            var b = Body(s.kind);
            string route = s.raider != null ? World.Rules.Entity(s.raider.Kind)?.Raider?.Route : null;
            bool flyer = route == Chem.World.FlyRoute;
            if (b == null && !flyer) continue;
            int need = used + (b?.Segments.Length ?? 0);
            if (instances.Length < need) System.Array.Resize(ref instances, Mathf.NextPowerOfTwo(need));
            int end = flyer ? Wings(s, used) : used + animator.Evaluate(b, s.state, 0, this, instances, used);
            if (route == Chem.World.ClimbRoute && s.kind == RaiderIds.CliffStalker) OntoTheSlope(s, used, end);
            Shadow(s);
            if (Boss(World, s.raider)) BossesDrawn++;
            Adjective(s, used, end);
            Blow(s, used, end);
            Frost(s, used, end);
            CreatureDrawer.HideAtLens(instances, used, end - used, cam);
            used = end;
            centre = s.at;
            DrawnRaiders++;
        }
        drawer.Draw(instances, used, centre);
    }

    // A raider with an adjective shows it: its segments lean to the adjective's colour (Hold.cs).
    void Adjective(Shown s, int from, int to)
    {
        var color = s.raider != null ? World.Rules.AdjectiveOf(s.raider.Kind)?.Color : null;
        if (color == null || color.Length != 3) return;
        var tint = new float3((float)color[0], (float)color[1], (float)color[2]);
        for (int i = from; i < to; i++)
        {
            var c = instances[i].Color;
            instances[i].Color = new float4(math.lerp(c.xyz, tint, 0.6f), c.w);
        }
    }

    // A frozen raider's segments in ice; cracked, a spiderweb over the ice (Punchy/Creature draws it
    // where an instance's Pad is set), with a spit of shards as it happens. GPU-first: only what the
    // instances are drawn with.
    void Frost(Shown s, int from, int to)
    {
        var sim = World.Rules.Sim;
        if (s.raider == null || sim.ShatterWhen == null || s.raider.StatusOf(sim.ShatterWhen) <= 0)
        {
            s.cracked = false;
            return;
        }
        bool cracked = s.raider.StatusOf(World.CrackedStatus) > 0;
        if (cracked && !s.cracked) ShardBurst.At(s.at + Vector3.up * 0.5f, new Color(0.7f, 0.88f, 1f), 16);
        s.cracked = cracked;
        var ice = new float3(0.42f, 0.66f, 0.95f);
        for (int i = from; i < to; i++)
        {
            var c = instances[i].Color;
            instances[i].Color = new float4(math.lerp(c.xyz, ice, 0.7f), c.w);
            instances[i].Pad = cracked ? 1f : 0f;
        }
    }

    // The species its rules row draws it as (raider.body), or null for none (the ash wing has wings of its own).
    CreatureBody Body(string kind)
    {
        if (bodies.Count == 0) ReadRows();
        var species = kind != null ? World.Rules.Entity(kind)?.Raider?.Body : null;
        int at = string.IsNullOrEmpty(species) ? -1 : bodies.IndexOf(species);
        return at >= 0 ? bodies[at] : null;
    }

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

    // Sizes the creature from its kind's body: the species' own size, times the kind's (raider.size, a percent).
    void SizeUp(Shown s)
    {
        var b = Body(s.kind);
        float size = (World.Rules.Entity(s.kind)?.Raider?.Size ?? 100) / 100f;
        s.state.Scale = (b != null ? (b.Size.x + b.Size.y) * 0.5f : 1f) * size * (Boss(World, s.raider) ? BossSize : 1f);
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
            var body = Body(s.kind);
            var screen = cam.WorldToScreenPoint(s.at + Vector3.up * (0.5f + (body != null ? body.StandHeight * 2f * s.state.Scale : 0.4f)));
            if (screen.z <= 0f || screen.z > hudRange) continue;
            bool boss = Boss(World, s.raider);
            float width = boss ? 150f : 46f, height = boss ? 13f : 6f;
            // The HUD's scale is on: a screen point is in pixels, the drawing in HUD units.
            float sx = screen.x / HudScale.Factor, sy = (Screen.height - screen.y) / HudScale.Factor;
            var back = new Rect(sx - width / 2f, sy, width, height);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(back, bar);
            float f = Mathf.Clamp01(s.hp / (float)s.maxHp);
            GUI.color = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.3f, 0.9f, 0.3f), f);
            GUI.DrawTexture(new Rect(back.x + 1f, back.y + 1f, (width - 2f) * f, height - 2f), bar);
            if (s.raider != null && World.Rules.AdjectiveOf(s.raider.Kind) != null)
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(sx - 90f, back.y - 22f, 180f, 22f), World.Rules.Entity(s.raider.Kind).Name);
            }
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
