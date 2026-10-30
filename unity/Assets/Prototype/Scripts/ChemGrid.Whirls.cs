using System.Collections.Generic;
using Chem;
using UnityEngine;

// Whirlwinds, falling stars and gas flashes, drawn from what the core says
// (World.Whirls, World.Stars, World.FlashesSince). A whirlwind is one reused
// particle funnel that eases after its cell, tinted by what it carries; a star
// is a small MeteorFall that lands when the core does; a flash is a pooled burst
// (BurstFx) over the cloud that went up, and a shove from the middle of it.
// GPU-first: a handful of particle systems however long the game runs.
public partial class ChemGrid
{
    const float ColumnEase = 4f; // m/s: a whirlwind's cell steps; its column glides between them
    const float ColumnHeight = 7f;
    // A falling star's size, as a share of a meteor's.
    const float StarSize = 0.3f;
    // The biggest a flash is drawn: past this its sparks fly off before they're seen.
    const float FlashMost = 2.6f;

    readonly List<ParticleSystem> columns = new();
    readonly List<(int x, int y, int push, int tick)> flashing = new();
    Material columnMaterial;
    int flashSeen = -1;
    readonly HashSet<(int x, int y, int tick)> flashesShown = new();
    readonly HashSet<World.Star> starsFalling = new();
    readonly List<World.Star> starsGone = new();
    SpellCaster showman;

    // What a whirlwind looks like carrying each thing (spells.json "carries"); plain air otherwise.
    static Color ColumnTint(string carrying) => carrying switch
    {
        "fire" => new Color(1f, 0.42f, 0.06f, 0.95f),
        "water" => new Color(0.3f, 0.56f, 0.92f, 0.85f),
        "sand" => new Color(0.76f, 0.6f, 0.34f, 0.95f),
        "smoke" => new Color(0.1f, 0.1f, 0.11f, 0.95f),
        "snow" => new Color(0.95f, 0.97f, 1f, 0.9f),
        _ => new Color(0.85f, 0.88f, 0.9f, 0.4f),
    };

    void RenderWhirls()
    {
        var whirls = World.Whirls;
        for (int i = 0; i < whirls.Count; i++) DrawWhirl(i, whirls[i]);
        for (int i = whirls.Count; i < columns.Count; i++)
            if (columns[i].isEmitting) columns[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void DrawWhirl(int i, World.Whirl ww)
    {
        if (i == columns.Count) columns.Add(MakeColumn());
        var ps = columns[i];
        var at = CellCenter(ww.X, ww.Y);
        at.y = GroundTop(ww.X, ww.Y, World.At(ww.X, ww.Y));
        bool starting = !ps.isEmitting;
        ps.transform.position = starting ? at : Vector3.MoveTowards(ps.transform.position, at, ColumnEase * Time.deltaTime);
        ps.transform.localScale = new Vector3(ww.Radius, 1f, ww.Radius) * cellSize;
        var main = ps.main;
        var tint = ColumnTint(ww.Carrying);
        main.startColor = new ParticleSystem.MinMaxGradient(tint, Color.Lerp(tint, Color.white, 0.3f));
        if (starting) ps.Play(true);
    }

    ParticleSystem MakeColumn()
    {
        var go = new GameObject("Whirlwind");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.8f);
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 420;
        var emission = ps.emission;
        emission.rateOverTime = 280f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.28f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        var spin = ps.velocityOverLifetime;
        spin.enabled = true;
        spin.space = ParticleSystemSimulationSpace.Local;
        spin.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
        spin.orbitalY = new ParticleSystem.MinMaxCurve(5f, 8f);
        spin.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
        spin.y = new ParticleSystem.MinMaxCurve(ColumnHeight / 1.2f);
        spin.radial = new ParticleSystem.MinMaxCurve(0.75f);
        var grow = ps.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.4f));
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) },
        });
        var look = go.GetComponent<ParticleSystemRenderer>();
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        look.sharedMaterial = SoftParticles();
        return ps;
    }

    // Soft puffs tinted by their particles' colour: a whirlwind's column, a wave's foam.
    Material SoftParticles()
    {
        if (columnMaterial != null) return columnMaterial;
        columnMaterial = UrpMaterials.NewUnlitParticles("Whirlwind", Color.white, additive: false);
        var soft = ProceduralTextures.SoftPuff(64);
        if (columnMaterial.HasProperty("_BaseMap")) columnMaterial.SetTexture("_BaseMap", soft);
        columnMaterial.mainTexture = soft;
        return columnMaterial;
    }

    // A star the core has aimed starts falling, to land the moment the core does.
    void RenderStars()
    {
        foreach (var star in World.Stars)
        {
            if (!starsFalling.Add(star)) continue;
            if (showman == null) showman = FindAnyObjectByType<SpellCaster>();
            var look = showman != null && showman.visuals != null ? showman.visuals.For(SpellIds.Meteor) : null;
            var at = CellCenter(star.X, star.Y);
            at.y = GroundTop(star.X, star.Y, World.At(star.X, star.Y));
            float seconds = (star.Lands - World.Tick) / (float)Mathf.Max(1, World.Rules.Sim.TicksPerSecond);
            var viewer = Camera.main != null ? Camera.main.transform.position : at + Vector3.back;
            MeteorFall.Launch(at, viewer, look, null, null, seconds, StarSize);
        }
        if (starsFalling.Count == 0) return;
        starsGone.Clear();
        foreach (var star in starsFalling)
            if (star.Lands < World.Tick) starsGone.Add(star);
        foreach (var star in starsGone) starsFalling.Remove(star);
    }

    // Each tick's flash since the last frame, as one burst and one shove over the cells that went up.
    void RenderFlashes()
    {
        flashing.Clear();
        // From the tick before too: a cast lands between ticks, stamped with the tick already seen.
        foreach (var f in World.FlashesSince(flashSeen - 1))
            if (flashesShown.Add((f.x, f.y, f.tick))) flashing.Add(f);
        flashSeen = World.Tick;
        flashesShown.RemoveWhere(f => f.tick < flashSeen - 1);
        for (int from = 0; from < flashing.Count;)
        {
            int to = from;
            while (to < flashing.Count && flashing[to].tick == flashing[from].tick) to++;
            Flash(from, to);
            from = to;
        }
    }

    void Flash(int from, int to)
    {
        var middle = Vector3.zero;
        for (int i = from; i < to; i++) middle += CellCenter(flashing[i].x, flashing[i].y);
        middle /= to - from;
        float reach = 0f;
        for (int i = from; i < to; i++) reach = Mathf.Max(reach, Vector3.Distance(middle, CellCenter(flashing[i].x, flashing[i].y)));
        reach += cellSize;
        for (int i = from; i < to; i++) Fog.Vanish(World.Idx(flashing[i].x, flashing[i].y));
        var first = flashing[from];
        middle.y = GroundTop(first.x, first.y, World.At(first.x, first.y));
        BurstFx.Play(BurstFx.Kind.Flash, middle, Mathf.Min(reach + cellSize, FlashMost));
        // Props are the host's (D4): a client's are puppets that follow it.
        bool hosting = !TryGetComponent<NetChem>(out var net) || !net.Online || net.IsServer;
        if (hosting && first.push > 0) Forces.Radial(middle, reach, first.push * Forces.ImpulsePerPush, null);
    }
}
