using System.Collections.Generic;
using UnityEngine;

// A waystone at each spawn of a map with more than one (SpawnPoints), so players can find them: a standing stone with a
// glowing cap beside the spawn's cell, and the party's reach of them noted as any wizard walks up to one (a wizard who goes
// down comes back at the nearest one reached, WizardDown). Only drawn, and only on such a map: the valley on its own has
// one spawn and no waystone. ChemGrid adds it.
[RequireComponent(typeof(ChemGrid))]
public sealed class Waystones : MonoBehaviour
{
    // Beside the spawn's cell, not on it: a player starts on the cell itself.
    static readonly Vector2Int Beside = new(2, -2);
    const float Height = 2.2f, Width = 0.7f, Every = 0.5f;

    ChemGrid grid;
    readonly List<GameObject> stones = new();
    float nextVisit;
    Material stone, glow;

    void Awake() => grid = GetComponent<ChemGrid>();

    void Start()
    {
        grid.WorldReset += Place;
        Place();
    }

    void OnDestroy()
    {
        if (grid != null) grid.WorldReset -= Place;
        foreach (var s in stones) if (s != null) Destroy(s);
        if (stone != null) Destroy(stone);
        if (glow != null) Destroy(glow);
    }

    void Place()
    {
        foreach (var s in stones) if (s != null) Destroy(s);
        stones.Clear();
        if (!SpawnPoints.Several) return;
        foreach (var spawn in SpawnPoints.Current) stones.Add(MakeStone(spawn));
    }

    GameObject MakeStone(SpawnPoints.Spawn spawn)
    {
        var cell = spawn.Cell + Beside;
        var at = grid.CellCenter(cell.x, cell.y);
        at.y = grid.GroundAt(at);
        var root = new GameObject($"Waystone {spawn.Name}");
        root.transform.position = at;
        var body = Part(root, PrimitiveType.Cube, new Vector3(0f, Height * 0.5f - 0.15f, 0f), new Vector3(Width, Height, Width * 0.55f), Stone());
        body.transform.localRotation = Quaternion.Euler(0f, (spawn.Id.GetHashCode() & 0xff) * 1.4f, 2.5f);
        Part(root, PrimitiveType.Sphere, new Vector3(0f, Height - 0.05f, 0f), Vector3.one * 0.38f, Glow());
        var light = new GameObject("Light").AddComponent<Light>();
        light.transform.SetParent(root.transform, false);
        light.transform.localPosition = new Vector3(0f, Height + 0.3f, 0f);
        light.type = LightType.Point;
        light.color = new Color(0.55f, 0.8f, 1f);
        light.range = 7f;
        light.intensity = 2.5f;
        light.shadows = LightShadows.None;
        return root;
    }

    static GameObject Part(GameObject root, PrimitiveType shape, Vector3 at, Vector3 size, Material material)
    {
        var part = GameObject.CreatePrimitive(shape);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(root.transform, false);
        part.transform.localPosition = at;
        part.transform.localScale = size;
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
        return part;
    }

    Material Stone()
    {
        if (stone == null) stone = Lit(new Color(0.42f, 0.42f, 0.45f), Color.black);
        return stone;
    }

    Material Glow()
    {
        if (glow == null) glow = Lit(new Color(0.6f, 0.85f, 1f), new Color(0.6f, 0.9f, 1.4f));
        return glow;
    }

    static Material Lit(Color colour, Color emission)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var m = new Material(shader) { color = colour };
        if (emission != Color.black)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
        }
        return m;
    }

    // Every half second: the spawns any wizard here has walked up to are the party's.
    void Update()
    {
        if (Time.time < nextVisit || grid.World == null || !SpawnPoints.Several) return;
        nextVisit = Time.time + Every;
        // Every wizard here, the local one and the party's (each has its Vitals).
        foreach (var wizard in FindObjectsByType<Vitals>(FindObjectsSortMode.None))
        {
            var cell = grid.CellAt(wizard.transform.position);
            if (SpawnPoints.Reach(cell)) EventLog.Add("A waystone: the party can come back here");
        }
    }
}
