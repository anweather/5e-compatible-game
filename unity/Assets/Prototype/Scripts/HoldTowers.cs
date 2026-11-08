using System.Collections.Generic;
using Chem;
using UnityEngine;

// The Hold's attuning towers (hold-batch 4; WorldHold.Towers): on the ground beside each (World.TowerGround), where a wizard stands to
// attune a hand to the tower's one element, tiles of that element's colour and the element itself on them, to read from the walls: embers rising off fire,
// puddles and dripping water, stones and dust for earth, leaves blown round for air. Drawn from code: no art. Only the show:
// the core (World.TowerOf) decides who is at a tower; HoldShow makes it.
public class HoldTowers : MonoBehaviour
{
    const float TileGap = 0.35f, Lift = 0.04f;
    static Material particles;

    /// <summary>Each tower's element and its look's root, for the probe.</summary>
    public IReadOnlyList<(string element, Transform at)> Shown => shown;
    readonly List<(string element, Transform at)> shown = new();

    /// <summary>Puts each tower's look on its ground (World.TowerGround): the open cells in its reach, where a wizard stands to attune.</summary>
    public void Show(ChemGrid grid, World world)
    {
        foreach (var (_, at) in shown) Destroy(at.gameObject);
        shown.Clear();
        if (world.HoldSite == null) return;
        foreach (var tower in world.HoldSite.Towers)
        {
            var cells = world.TowerGround(tower);
            if (cells.Count == 0) continue;
            var points = new List<Vector3>();
            foreach (var (x, y) in cells)
            {
                var p = grid.CellCenter(x, y);
                p.y = grid.GroundAt(p) + Lift;
                points.Add(p);
            }
            var centre = Vector3.zero;
            foreach (var p in points) centre += p;
            centre /= points.Count;
            float radius = grid.cellSize * 0.5f;
            foreach (var p in points) radius = Mathf.Max(radius, new Vector2(p.x - centre.x, p.z - centre.z).magnitude + grid.cellSize * 0.5f);
            var root = new GameObject($"Tower of {tower.Element}").transform;
            root.SetParent(transform, true);
            root.position = centre;
            var colour = HudDraw.ElementColor(tower.Element);
            Tiles(root, points, grid.cellSize, colour);
            switch (tower.Element)
            {
                case ElementIds.Fire: Embers(root, radius); break;
                case ElementIds.Water: Puddles(root, points, radius); break;
                case ElementIds.Earth: Stones(root, points, radius); break;
                case ElementIds.Air: Leaves(root, radius); break;
            }
            shown.Add((tower.Element, root));
        }
    }

    // A flat tile of the element's colour, glowing a little, on each cell a wizard attunes on.
    static void Tiles(Transform root, List<Vector3> points, float size, Color colour)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        float half = size * (0.5f - TileGap / 4f);
        foreach (var p in points)
        {
            var at = p - root.position;
            int n = v.Count;
            v.Add(at + new Vector3(-half, 0f, -half));
            v.Add(at + new Vector3(-half, 0f, half));
            v.Add(at + new Vector3(half, 0f, half));
            v.Add(at + new Vector3(half, 0f, -half));
            t.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
        }
        var mesh = new Mesh { name = "Tower ground" };
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        var go = new GameObject("Ground");
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = UrpMaterials.NewLit("Tower ground", colour * 0.8f, 0.4f, colour * 0.35f);
    }

    static ParticleSystem Particles(Transform root, string name, Color from, Color to, bool additive)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startColor = new ParticleSystem.MinMaxGradient(from, to);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var look = go.GetComponent<ParticleSystemRenderer>();
        if (particles == null) particles = MakeParticles();
        look.sharedMaterial = additive ? Additive() : particles;
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ps;
    }

    static Material additive;

    static Material MakeParticles()
    {
        var m = UrpMaterials.NewUnlitParticles("Tower particles", Color.white, additive: false);
        var soft = ProceduralTextures.SoftPuff(32);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", soft);
        m.mainTexture = soft;
        return m;
    }

    static Material Additive()
    {
        if (additive != null) return additive;
        additive = UrpMaterials.NewUnlitParticles("Tower embers", Color.white, additive: true);
        var soft = ProceduralTextures.SoftPuff(32);
        if (additive.HasProperty("_BaseMap")) additive.SetTexture("_BaseMap", soft);
        additive.mainTexture = soft;
        return additive;
    }

    static void Shape(ParticleSystem ps, float radius)
    {
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.rotation = new Vector3(-90f, 0f, 0f);
    }

    // Embers rising off the tower, flickering out as they go.
    static void Embers(Transform root, float radius)
    {
        var ps = Particles(root, "Embers", new Color(1f, 0.55f, 0.1f), new Color(1f, 0.85f, 0.3f), additive: true);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.gravityModifier = -0.15f;
        var emission = ps.emission;
        emission.rateOverTime = 90f;
        Shape(ps, radius * 0.9f);
        Fade(ps);
        ps.Play();
    }

    // Glossy puddles on the tower's top and drops falling into them.
    static void Puddles(Transform root, List<Vector3> points, float radius)
    {
        var water = UrpMaterials.NewLit("Tower puddle", new Color(0.2f, 0.45f, 0.85f), 0.95f);
        var random = new System.Random(5);
        for (int i = 0; i < 7; i++)
        {
            var cell = points[random.Next(points.Count)] - root.position;
            var puddle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(puddle.GetComponent<Collider>());
            puddle.name = "Puddle";
            puddle.transform.SetParent(root, false);
            puddle.transform.localPosition = cell + new Vector3((float)random.NextDouble() - 0.5f, 0.02f, (float)random.NextDouble() - 0.5f);
            float size = 0.5f + 0.7f * (float)random.NextDouble();
            puddle.transform.localScale = new Vector3(size, 0.01f, size * 0.8f);
            puddle.GetComponent<MeshRenderer>().sharedMaterial = water;
        }
        var ps = Particles(root, "Drips", new Color(0.55f, 0.75f, 1f, 0.9f), new Color(0.8f, 0.9f, 1f, 0.8f), additive: false);
        ps.transform.localPosition = Vector3.up * 2.5f;
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.15f);
        main.gravityModifier = 0.6f;
        var emission = ps.emission;
        emission.rateOverTime = 60f;
        Shape(ps, radius * 0.8f);
        ps.Play();
    }

    // Stones strewn on the tower's top, and dust lifting off them.
    static void Stones(Transform root, List<Vector3> points, float radius)
    {
        var stone = UrpMaterials.NewLit("Tower stone", new Color(0.45f, 0.38f, 0.28f), 0.15f);
        var random = new System.Random(9);
        for (int i = 0; i < 14; i++)
        {
            var cell = points[random.Next(points.Count)] - root.position;
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(rock.GetComponent<Collider>());
            rock.name = "Stone";
            rock.transform.SetParent(root, false);
            float size = 0.25f + 0.35f * (float)random.NextDouble();
            rock.transform.SetLocalPositionAndRotation(cell + new Vector3(Mathf.Cos(a) * 0.6f, size * 0.3f, Mathf.Sin(a) * 0.6f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 15f));
            rock.transform.localScale = new Vector3(size, size * 0.6f, size * 0.8f);
            rock.GetComponent<MeshRenderer>().sharedMaterial = stone;
        }
        var ps = Particles(root, "Dust", new Color(0.55f, 0.45f, 0.32f, 0.5f), new Color(0.7f, 0.6f, 0.45f, 0.35f), additive: false);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.gravityModifier = -0.02f;
        var emission = ps.emission;
        emission.rateOverTime = 20f;
        Shape(ps, radius * 0.8f);
        Fade(ps);
        ps.Play();
    }

    // Leaves blown round the tower's top in a ring.
    static void Leaves(Transform root, float radius)
    {
        var ps = Particles(root, "Leaves", new Color(0.55f, 0.7f, 0.25f), new Color(0.85f, 0.75f, 0.3f), additive: false);
        ps.transform.localPosition = Vector3.up * 0.8f;
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var emission = ps.emission;
        emission.rateOverTime = 60f;
        Shape(ps, radius * 0.7f);
        var orbit = ps.velocityOverLifetime;
        orbit.enabled = true;
        orbit.space = ParticleSystemSimulationSpace.Local;
        orbit.orbitalY = new ParticleSystem.MinMaxCurve(1.6f);
        // Unity wants x, y and z in one mode.
        orbit.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        orbit.y = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        orbit.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var spin = ps.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
        Fade(ps);
        ps.Play();
    }

    static void Fade(ParticleSystem ps)
    {
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) },
        });
    }
}
