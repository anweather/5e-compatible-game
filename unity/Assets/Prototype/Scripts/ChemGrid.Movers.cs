using System.Collections.Generic;
using Chem;
using UnityEngine;

// What rolls across the cells, drawn from what the core says (World.Waves,
// World.Boulders). A wave is one curled crest of water with foam blowing off it,
// easing after its front; a boulder is a rock that turns as it goes, and stands
// where it comes to rest (the core's cell there is an earth pillar: CellDressing
// draws that one as the boulder). Both are reused, not made per cell. As a wave's
// front rolls a cell it brings what's light there up to its own speed, so props
// ride it to where it breaks (the host's, D4), and it knocks the local wizard back.
public partial class ChemGrid
{
    const float WaveTall = 1.15f, WaveDeep = 1.4f, BoulderSize = 1.5f;
    // Eases a bit faster than a cell a step, so it never falls behind its front.
    const float MoverCatchUp = 1.25f;
    // What a wave's push is, in impulses, against the push of a cast (Forces.ImpulsePerPush).
    const float WaveShove = 1.5f;
    const float WaveFoamRate = 90f;

    sealed class WaveShow
    {
        public GameObject root;
        public ParticleSystem foam;
        public int x = int.MinValue, y;
    }

    sealed class BoulderShow
    {
        public Transform root, spin;
        public int x, y;
    }

    readonly Dictionary<World.Wave, WaveShow> waveShows = new();
    readonly Dictionary<World.Boulder, BoulderShow> boulderShows = new();
    readonly Stack<WaveShow> spareWaves = new();
    readonly Stack<BoulderShow> spareBoulders = new();
    readonly List<World.Wave> wavesGone = new();
    readonly List<World.Boulder> bouldersGone = new();
    readonly HashSet<int> boulderRests = new();
    readonly List<(int x, int y)> frontCells = new();
    GolfCourse golf;
    Mesh crestMesh;
    Material crestMaterial;

    /// <summary>Whether a boulder came to rest on this cell (its pillar is drawn as the boulder).</summary>
    public bool BoulderRestsAt(int x, int y) => boulderRests.Contains(World.Idx(x, y));

    /// <summary>How many waves and boulders are drawn now, for the probe.</summary>
    public int MoversShown => waveShows.Count + boulderShows.Count;

    // The way a wave rolls, along its aim (which may be off the grid's four ways), and how far one of its
    // steps goes against a cell (a step along a slant is longer).
    Vector3 WaveStep(World.Wave wave) => (CellCenter(wave.AX, wave.AY) - CellCenter(0, 0)).normalized;

    static float WaveStride(World.Wave wave) =>
        Mathf.Sqrt(wave.AX * wave.AX + wave.AY * wave.AY) / Mathf.Max(1, Mathf.Max(Mathf.Abs(wave.AX), Mathf.Abs(wave.AY)));

    float MoverSpeed(int every) => cellSize * World.Rules.Sim.TicksPerSecond / Mathf.Max(1, every) * MoverCatchUp;

    Vector3 MoverGround(int x, int y)
    {
        var at = CellCenter(x, y);
        at.y = Mathf.Max(GroundTop(x, y, World.At(x, y)), WaterSurfaceY(x, y));
        return at;
    }

    void RenderMovers()
    {
        if (golf == null && World.Golf != null) golf = GolfCourse.On(this);
        RenderWaves();
        RenderBoulders();
    }

    void RenderWaves()
    {
        foreach (var wave in World.Waves) DrawWave(wave);
        wavesGone.Clear();
        foreach (var kv in waveShows)
            if (kv.Key.Left <= 0) wavesGone.Add(kv.Key);
        foreach (var wave in wavesGone) BreakWave(wave);
    }

    void DrawWave(World.Wave wave)
    {
        if (!waveShows.TryGetValue(wave, out var show))
        {
            show = spareWaves.Count > 0 ? spareWaves.Pop() : MakeWave();
            waveShows[wave] = show;
            show.root.SetActive(true);
            show.root.transform.position = MoverGround(wave.X, wave.Y);
            var shape = show.foam.shape;
            shape.scale = new Vector3((wave.Width * 2 + 1) * cellSize, 0.2f, 0.3f);
            show.root.transform.GetChild(0).localScale = new Vector3((wave.Width * 2 + 1) * cellSize, WaveTall, WaveDeep);
            show.root.transform.rotation = Quaternion.LookRotation(WaveStep(wave));
            show.foam.Play(true);
        }
        var root = show.root.transform;
        root.position = Vector3.MoveTowards(root.position, MoverGround(wave.X, wave.Y), MoverSpeed(wave.Every) * WaveStride(wave) * Time.deltaTime);
        if (show.x == wave.X && show.y == wave.Y) return;
        show.x = wave.X;
        show.y = wave.Y;
        ShoveOnFront(wave);
    }

    void ShoveOnFront(World.Wave wave)
    {
        var spell = World.Rules.Spell(wave.Spell);
        if (spell == null) return;
        frontCells.Clear();
        frontCells.AddRange(World.WaveFront(wave));
        bool hosting = !TryGetComponent<NetChem>(out var net) || !net.Online || net.IsServer;
        var wizard = WizardRig.FindLocal();
        var way = WaveStep(wave);
        foreach (var rider in Forces.OnCells(this, frontCells, null, p => p is PhysicsProp prop && hosting && Light(prop, wave.MaxMass)))
            ((PhysicsProp)rider).Ride(way * (MoverSpeed(wave.Every) * WaveStride(wave)));
        Forces.Along(this, frontCells, way, spell.ForcePush * Forces.ImpulsePerPush * WaveShove, null, p => p is Knockback k && k.gameObject == wizard);
    }

    bool Light(PhysicsProp prop, int most) => World.Rules.Entities.TryGetValue(prop.kind, out var def) && def.Mass <= most;

    // Where it stops it breaks: one splash the width of its front.
    void BreakWave(World.Wave wave)
    {
        var show = waveShows[wave];
        waveShows.Remove(wave);
        BurstFx.Play(BurstFx.Kind.Splash, MoverGround(wave.X, wave.Y), 0.7f + wave.Width * 0.5f);
        GameSounds.Play(SoundIds.Splash, show.root.transform.position);
        show.foam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        show.x = int.MinValue;
        show.root.SetActive(false);
        spareWaves.Push(show);
    }

    WaveShow MakeWave()
    {
        var root = new GameObject("Wave");
        root.transform.SetParent(transform, false);
        var crest = new GameObject("Crest");
        crest.transform.SetParent(root.transform, false);
        if (crestMesh == null) crestMesh = WaveCrest.Make();
        crest.AddComponent<MeshFilter>().sharedMesh = crestMesh;
        var look = crest.AddComponent<MeshRenderer>();
        if (crestMaterial == null) crestMaterial = WaveCrest.Material();
        look.sharedMaterial = crestMaterial;
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return new WaveShow { root = root, foam = MakeFoam(root.transform) };
    }

    ParticleSystem MakeFoam(Transform parent)
    {
        var go = new GameObject("Foam");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, WaveTall * 0.95f, WaveDeep * 0.3f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.42f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(0.8f, 0.9f, 1f, 0.8f));
        main.gravityModifier = 0.6f;
        var emission = ps.emission;
        emission.rateOverTime = WaveFoamRate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) },
        });
        var look = go.GetComponent<ParticleSystemRenderer>();
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        look.sharedMaterial = SoftParticles();
        return ps;
    }

    void RenderBoulders()
    {
        foreach (var boulder in World.Boulders) DrawBoulder(boulder);
        bouldersGone.Clear();
        foreach (var kv in boulderShows)
            if (kv.Key.Left <= 0) bouldersGone.Add(kv.Key);
        foreach (var boulder in bouldersGone) RestBoulder(boulder);
    }

    void DrawBoulder(World.Boulder boulder)
    {
        float reach = BoulderSize / 2f;
        var to = MoverGround(boulder.X, boulder.Y) + Vector3.up * reach;
        if (!boulderShows.TryGetValue(boulder, out var show))
        {
            show = spareBoulders.Count > 0 ? spareBoulders.Pop() : MakeBoulder();
            boulderShows[boulder] = show;
            show.root.gameObject.SetActive(true);
            show.root.position = to;
        }
        var was = show.root.position;
        show.root.position = Vector3.MoveTowards(was, to, MoverSpeed(boulder.Every) * Time.deltaTime);
        // It turns as far as it rolled, about the axis across its way.
        var moved = show.root.position - was;
        moved.y = 0f;
        if (moved.sqrMagnitude > 1e-6f) show.spin.Rotate(Vector3.Cross(Vector3.up, moved.normalized), moved.magnitude / reach * Mathf.Rad2Deg, Space.World);
        show.x = boulder.X;
        show.y = boulder.Y;
    }

    // Gone from the core: where its earth stands it stays, as a boulder on that cell; in water it sinks with a splash.
    void RestBoulder(World.Boulder boulder)
    {
        var show = boulderShows[boulder];
        boulderShows.Remove(boulder);
        show.root.gameObject.SetActive(false);
        spareBoulders.Push(show);
        int index = World.Idx(boulder.X, boulder.Y);
        var c = World.At(boulder.X, boulder.Y);
        if (c.T == TerrainIds.Pillar)
        {
            boulderRests.Add(index);
            Redress(views[index], c, boulder.X, boulder.Y, false);
        }
        else BurstFx.Play(BurstFx.Kind.Splash, MoverGround(boulder.X, boulder.Y));
        Dust(MoverGround(boulder.X, boulder.Y));
    }

    BoulderShow MakeBoulder()
    {
        var root = new GameObject("Boulder").transform;
        root.SetParent(transform, false);
        var spin = new GameObject("Turning").transform;
        spin.SetParent(root, false);
        dressings.Boulder(spin, BoulderSize);
        foreach (var solid in spin.GetComponentsInChildren<Collider>()) Destroy(solid);
        return new BoulderShow { root = root, spin = spin };
    }

    void DestroyMovers()
    {
        if (crestMesh != null) Destroy(crestMesh);
        if (crestMaterial != null)
        {
            Destroy(crestMaterial.GetTexture("_BaseMap"));
            Destroy(crestMaterial);
        }
    }
}
