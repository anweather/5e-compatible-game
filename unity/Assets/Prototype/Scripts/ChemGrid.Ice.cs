using System.Collections.Generic;
using Chem;
using UnityEngine;

// A cold wave (one whose spell lays cold on its front, spells.json then: the
// Iceberg), drawn cold: chunks of ice and shards of it tumbling up its back and
// over its crest, frost glittering off the lip, sleet driving down round it and a
// freezing mist that it leaves lying behind. The water it freezes is drawn as
// broken pack ice, slabs tilted every way and a ridge pushed up here and there,
// until it thaws. All of it from what the core says (World.Waves, a cell's
// Frozen); none of it is solid (the ice's own surface is, CellWaterMesh).
public partial class ChemGrid
{
    const string ColdElement = "cold";
    const int IceChunks = 12, IceShards = 6, MostFloes = 900;
    const float SleetHeight = 9f, IceRollSeconds = 1.6f, FloeCheckEvery = 0.5f, FloeAfter = 0.2f;
    static readonly Color IceColor = new(0.8f, 0.93f, 1f), PackIceColor = new(0.86f, 0.94f, 1f);

    sealed class IceRide
    {
        public GameObject root;
        public Transform[] pieces;
        public float[] across, phase, spin, size;
        public Vector3[] axis;
        public ParticleSystem sleet, mist, frost;
    }

    readonly Dictionary<int, GameObject> floes = new();
    readonly List<(int x, int y, float at)> floesDue = new();
    readonly List<int> floesThawed = new();
    MaterialPropertyBlock iceShade; // made on first use: Unity makes none in a MonoBehaviour's field initializer
    float floesCheckedAt;

    /// <summary>How many cells are drawn as pack ice now, for the probe.</summary>
    public int PackIceShown => floes.Count;

    bool Cold(World.Wave wave) => World.Rules.Spell(wave.Spell)?.Then == ColdElement;

    // A wave's show turned cold or back: a spare show may have been the other's.
    void IceLook(WaveShow show, World.Wave wave, float tall, float deep)
    {
        bool cold = Cold(wave);
        if (cold && show.ice == null) show.ice = MakeIce(show.root.transform);
        if (show.ice == null) return;
        show.ice.root.SetActive(cold);
        if (!cold) return;
        float wide = (wave.Width * 2 + 1) * cellSize;
        var rng = new System.Random(wave.X * 131 + wave.Y * 71);
        for (int i = 0; i < show.ice.pieces.Length; i++)
        {
            show.ice.across[i] = ((float)rng.NextDouble() - 0.5f) * wide * 0.9f;
            show.ice.phase[i] = (float)rng.NextDouble();
            show.ice.spin[i] = 140f + 260f * (float)rng.NextDouble();
            show.ice.axis[i] = (Vector3.right + new Vector3(0f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f)).normalized;
            bool shard = i >= IceChunks;
            show.ice.size[i] = (shard ? 0.9f : 0.55f) + 0.5f * (float)rng.NextDouble();
        }
        var sleet = show.ice.sleet.shape;
        sleet.scale = new Vector3(wide + 6f, 0.5f, deep * 2.5f + 6f);
        show.ice.sleet.transform.localPosition = new Vector3(0f, SleetHeight, -deep * 0.4f);
        var mist = show.ice.mist.shape;
        mist.scale = new Vector3(wide, 0.3f, deep);
        var frost = show.ice.frost.shape;
        frost.scale = new Vector3(wide * 0.9f, 0.1f, 0.4f);
        show.ice.frost.transform.localPosition = new Vector3(0f, tall, deep * 0.3f);
        foreach (var ps in new[] { show.ice.sleet, show.ice.mist, show.ice.frost }) ps.Play(true);
        // It heaves up out of the water in a burst of broken ice.
        BurstFx.Play(BurstFx.Kind.Chips, show.root.transform.position + Vector3.up * (tall * 0.5f), 1.2f + wave.Width * 0.5f, look: IceColor);
    }

    // Each piece rolls up the wave's back, over its crest and down out of sight, tumbling as it goes, round and round.
    static void RollIce(WaveShow show, float tall, float deep)
    {
        var ice = show.ice;
        if (ice == null || !ice.root.activeSelf) return;
        for (int i = 0; i < ice.pieces.Length; i++)
        {
            float k = Mathf.Repeat(Time.time / IceRollSeconds + ice.phase[i], 1f);
            float z = Mathf.Lerp(-1.25f, 0.5f, k);
            float shown = Mathf.Sin(k * Mathf.PI);
            var piece = ice.pieces[i];
            piece.localPosition = new Vector3(ice.across[i], CrestHeight(z) * tall + 0.15f, z * deep);
            piece.localScale = Vector3.one * (ice.size[i] * Mathf.Clamp01(shown * 2.5f));
            piece.localRotation = Quaternion.AngleAxis(Time.time * ice.spin[i] + ice.phase[i] * 360f, ice.axis[i]);
        }
    }

    // How high the crest stands at a point along it from behind (z -1.5) to its lip (0.3), as WaveCrest draws it.
    static float CrestHeight(float z)
    {
        if (z <= -1.5f) return 0f;
        if (z < -0.95f) return Mathf.Lerp(0f, 0.22f, (z + 1.5f) / 0.55f);
        if (z < -0.45f) return Mathf.Lerp(0.22f, 0.62f, (z + 0.95f) / 0.5f);
        if (z < -0.05f) return Mathf.Lerp(0.62f, 0.95f, (z + 0.45f) / 0.4f);
        if (z < 0.3f) return Mathf.Lerp(0.95f, 1.05f, (z + 0.05f) / 0.35f);
        return Mathf.Lerp(1.05f, 0.8f, (z - 0.3f) / 0.3f);
    }

    static void BreakIce(WaveShow show)
    {
        if (show.ice == null || !show.ice.root.activeSelf) return;
        foreach (var ps in new[] { show.ice.sleet, show.ice.mist, show.ice.frost }) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        foreach (var piece in show.ice.pieces) piece.localScale = Vector3.zero;
        GameSounds.Play(SoundIds.Shatter, show.root.transform.position);
    }

    IceRide MakeIce(Transform wave)
    {
        var root = new GameObject("Ice");
        root.transform.SetParent(wave, false);
        int n = IceChunks + IceShards;
        var ice = new IceRide
        {
            root = root, pieces = new Transform[n], across = new float[n], phase = new float[n], spin = new float[n], size = new float[n], axis = new Vector3[n],
        };
        var look = materials.Lit("Ice chunk", IceColor, 0.9f, new Color(0.04f, 0.1f, 0.16f));
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject(i < IceChunks ? "Ice chunk" : "Ice shard");
            go.transform.SetParent(root.transform, false);
            var holder = new GameObject("Shape").transform;
            holder.SetParent(go.transform, false);
            // A shard is a long splinter, laid along its spin; a chunk a broken block.
            if (i >= IceChunks)
            {
                holder.localScale = new Vector3(0.28f, 1.1f, 0.22f);
                holder.localPosition = Vector3.down * 0.55f;
            }
            holder.gameObject.AddComponent<MeshFilter>().sharedMesh = i < IceChunks ? RockSpire.Chunk(i) : RockSpire.Spire(i + 3);
            var r = holder.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = look;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ice.pieces[i] = go.transform;
        }
        ice.sleet = Sleet(root.transform);
        ice.mist = FreezingMist(root.transform);
        ice.frost = Frost(root.transform);
        return ice;
    }

    ParticleSystem IceParticles(Transform parent, string name, bool world, float rate, int most)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.maxParticles = most;
        main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        var look = go.GetComponent<ParticleSystemRenderer>();
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        look.sharedMaterial = SoftParticles();
        return ps;
    }

    // Sleet driving down round it: fast streaks slanting the way it rolls.
    ParticleSystem Sleet(Transform parent)
    {
        var ps = IceParticles(parent, "Sleet", true, 1600f, 3000);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.75f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.9f, 0.96f, 1f, 0.75f), new Color(0.7f, 0.85f, 1f, 0.5f));
        var fall = ps.velocityOverLifetime;
        fall.enabled = true;
        fall.space = ParticleSystemSimulationSpace.Local;
        fall.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        fall.y = new ParticleSystem.MinMaxCurve(-17f, -13f);
        fall.z = new ParticleSystem.MinMaxCurve(3f, 5f);
        var look = ps.GetComponent<ParticleSystemRenderer>();
        look.renderMode = ParticleSystemRenderMode.Stretch;
        look.velocityScale = 0.05f;
        look.lengthScale = 2f;
        return ps;
    }

    // A freezing mist off the ice: low, slow and spreading, left lying in its wake.
    ParticleSystem FreezingMist(Transform parent)
    {
        var ps = IceParticles(parent, "Freezing mist", true, 55f, 400);
        ps.transform.localPosition = Vector3.up * 0.4f;
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.6f, 3.6f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.93f, 1f, 0.4f), new Color(0.7f, 0.85f, 1f, 0.28f));
        main.gravityModifier = -0.01f;
        var grow = ps.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) },
        });
        return ps;
    }

    // Frost glittering off its lip.
    ParticleSystem Frost(Transform parent)
    {
        var ps = IceParticles(parent, "Frost", true, 160f, 400);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(0.75f, 0.9f, 1f, 1f));
        main.gravityModifier = 0.25f;
        return ps;
    }

    // Each cell a cold wave's front rolls over turns to pack ice once the core has frozen it.
    void PackIceBehind(World.Wave wave)
    {
        if (!Cold(wave)) return;
        foreach (var (x, y) in frontCells) floesDue.Add((x, y, Time.time + FloeAfter));
    }

    void RenderPackIce()
    {
        for (int i = floesDue.Count - 1; i >= 0; i--)
        {
            var (x, y, at) = floesDue[i];
            if (Time.time < at) continue;
            floesDue.RemoveAt(i);
            int key = World.Idx(x, y);
            var c = World.At(x, y);
            if (c != null && c.Frozen && c.Water > 0 && !floes.ContainsKey(key) && floes.Count < MostFloes) floes[key] = PackIce(x, y);
        }
        if (floes.Count == 0 || Time.time - floesCheckedAt < FloeCheckEvery) return;
        floesCheckedAt = Time.time;
        floesThawed.Clear();
        foreach (var kv in floes)
        {
            var c = World.Cells[kv.Key];
            if (!c.Frozen || c.Water <= 0) floesThawed.Add(kv.Key);
        }
        foreach (int key in floesThawed)
        {
            Destroy(floes[key]);
            floes.Remove(key);
        }
    }

    // Broken slabs of ice over the cell, tilted every way, now and then one pushed up on edge, and loose lumps.
    GameObject PackIce(int x, int y)
    {
        var rng = new System.Random(x * 92821 + y * 68917);
        var root = new GameObject($"Pack ice {x},{y}");
        root.transform.SetParent(transform, false);
        var at = CellCenter(x, y);
        at.y = WaterSurfaceY(x, y);
        root.transform.position = at;
        float R() => (float)rng.NextDouble();
        int slabs = 2 + rng.Next(2);
        for (int i = 0; i < slabs; i++)
        {
            bool ridge = i == 0 && R() < 0.3f;
            float wide = cellSize * (ridge ? 0.5f + 0.3f * R() : 0.6f + 0.35f * R());
            var off = new Vector3(R() - 0.5f, ridge ? -0.1f : -0.06f - 0.06f * R(), R() - 0.5f) * (cellSize * 0.6f);
            var turn = Quaternion.Euler(0f, R() * 360f, 0f) * Quaternion.Euler(ridge ? 30f + 25f * R() : 3f + 12f * R(), 0f, (R() - 0.5f) * 10f);
            Floe(root.transform, RockSpire.Slab(rng.Next()), off, turn, new Vector3(wide, 1f + 0.8f * R(), wide * (0.7f + 0.4f * R())), x + i, y);
        }
        for (int i = 0; i < 2; i++)
        {
            var off = new Vector3(R() - 0.5f, 0.05f, R() - 0.5f) * (cellSize * 0.8f);
            Floe(root.transform, RockSpire.Chunk(rng.Next()), off, Quaternion.Euler(R() * 360f, R() * 360f, R() * 360f), Vector3.one * (0.18f + 0.22f * R()), x, y + i);
        }
        return root;
    }

    Material PackIceLook => materials.Lit("Pack ice", Color.white, 0.8f, new Color(0.03f, 0.07f, 0.1f));

    void Floe(Transform root, Mesh mesh, Vector3 at, Quaternion turn, Vector3 size, int x, int y)
    {
        var go = new GameObject("Floe");
        go.transform.SetParent(root, false);
        go.transform.SetLocalPositionAndRotation(at, turn);
        go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = PackIceLook;
        float shade = 0.9f + 0.1f * Mathf.PerlinNoise(x * 0.41f + 0.3f, y * 0.41f + 0.7f);
        iceShade ??= new MaterialPropertyBlock();
        UrpMaterials.SetColor(r, iceShade, PackIceColor * shade);
    }

    void DestroyIce()
    {
        foreach (var floe in floes.Values) Destroy(floe);
        floes.Clear();
        floesDue.Clear();
    }
}
