using System.Collections.Generic;
using Chem;
using UnityEngine;

// Whirlwinds and gas flashes, drawn from what the core says (World.Whirls,
// World.FlashesSince). A whirlwind is one reused particle column that eases
// after its cell, tinted by what it carries; a flash is a pooled burst (BurstFx)
// over the cloud that went up, and a shove from the middle of it. GPU-first:
// a handful of particle systems however long the game runs.
public partial class ChemGrid
{
    const float ColumnEase = 4f; // m/s: a whirlwind's cell steps; its column glides between them
    const float ColumnHeight = 5f;

    readonly List<ParticleSystem> columns = new();
    readonly List<(int x, int y, int push, int tick)> flashing = new();
    Material columnMaterial;
    int flashSeen = -1;

    // What a whirlwind looks like carrying each thing (spells.json "carries"); plain air otherwise.
    static Color ColumnTint(string carrying) => carrying switch
    {
        "fire" => new Color(1f, 0.5f, 0.12f, 0.8f),
        "water" => new Color(0.4f, 0.65f, 0.95f, 0.65f),
        "sand" => new Color(0.82f, 0.68f, 0.42f, 0.8f),
        "smoke" => new Color(0.16f, 0.16f, 0.17f, 0.8f),
        "snow" => new Color(0.95f, 0.97f, 1f, 0.75f),
        _ => new Color(0.85f, 0.88f, 0.9f, 0.3f),
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
        main.startColor = ColumnTint(ww.Carrying);
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
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 160;
        var emission = ps.emission;
        emission.rateOverTime = 90f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.7f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        var spin = ps.velocityOverLifetime;
        spin.enabled = true;
        spin.space = ParticleSystemSimulationSpace.Local;
        spin.orbitalY = new ParticleSystem.MinMaxCurve(5f, 8f);
        spin.y = new ParticleSystem.MinMaxCurve(ColumnHeight / 1.5f);
        spin.radial = new ParticleSystem.MinMaxCurve(-0.15f);
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) },
        });
        var look = go.GetComponent<ParticleSystemRenderer>();
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (columnMaterial == null)
        {
            columnMaterial = UrpMaterials.NewUnlitParticles("Whirlwind", Color.white, additive: false);
            var soft = ProceduralTextures.SoftPuff(64);
            if (columnMaterial.HasProperty("_BaseMap")) columnMaterial.SetTexture("_BaseMap", soft);
            columnMaterial.mainTexture = soft;
        }
        look.sharedMaterial = columnMaterial;
        return ps;
    }

    // Each tick's flash since the last frame, as one burst and one shove over the cells that went up.
    void RenderFlashes()
    {
        flashing.Clear();
        flashing.AddRange(World.FlashesSince(flashSeen));
        flashSeen = World.Tick;
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
        var first = flashing[from];
        middle.y = GroundTop(first.x, first.y, World.At(first.x, first.y));
        BurstFx.Play(BurstFx.Kind.Flash, middle, reach);
        // Props are the host's (D4): a client's are puppets that follow it.
        bool hosting = !TryGetComponent<NetChem>(out var net) || !net.Online || net.IsServer;
        if (hosting && first.push > 0) Forces.Radial(middle, reach, first.push * Forces.ImpulsePerPush, null);
    }
}
