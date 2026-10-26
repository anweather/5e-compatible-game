using System.Collections;
using UnityEngine;

// The probe's edge scenario (docs/FAST_LOOP.md): the wizard runs at each end
// of the land (the valley's terrains stop there), north, east, south and west,
// from 15 m inside it, for longer than it takes to get there. Checks that the
// edge holds them: still over the land and standing on the ground, not fallen
// off it, no console errors. Screenshots of each edge.
public class EdgeProbe : ProbeScenario
{
    const float Inside = 15f;
    const float RunSeconds = 10f;

    protected override IEnumerator Run()
    {
        Check("edge_built", GameObject.Find("World edge") != null, "the World edge object (walls and rocks)");
        var land = Land();
        Metric("land_west", land.xMin);
        Metric("land_east", land.xMax);
        string[] names = { "north", "east", "south", "west" };
        for (int d = 0; d < 4; d++) yield return RunAt(land, d, names[d]);
    }

    // The terrains' extent on the ground plane (x, z).
    static Rect Land()
    {
        var rect = new Rect();
        bool first = true;
        foreach (var t in Terrain.activeTerrains)
        {
            var p = t.transform.position;
            var s = t.terrainData.size;
            var r = new Rect(p.x, p.z, s.x, s.z);
            rect = first ? r : Rect.MinMaxRect(Mathf.Min(rect.xMin, r.xMin), Mathf.Min(rect.yMin, r.yMin), Mathf.Max(rect.xMax, r.xMax), Mathf.Max(rect.yMax, r.yMax));
            first = false;
        }
        return rect;
    }

    IEnumerator RunAt(Rect land, int dir, string name)
    {
        var outward = dir switch { 0 => Vector3.forward, 1 => Vector3.right, 2 => Vector3.back, _ => Vector3.left };
        var start = dir switch
        {
            0 => new Vector3(land.center.x, 0f, land.yMax - Inside),
            1 => new Vector3(land.xMax - Inside, 0f, land.center.y),
            2 => new Vector3(land.center.x, 0f, land.yMin + Inside),
            _ => new Vector3(land.xMin + Inside, 0f, land.center.y),
        };
        start.y = TerrainGround(start) + 0.3f;
        Teleport(start);
        // The controller moves relative to the camera: look outward from behind the wizard.
        Cut(start - outward * 8f + Vector3.up * 5f, start + outward * 10f);
        yield return Wait(0.3f);
        puppet.Move(Vector2.up);
        yield return Wait(RunSeconds);
        puppet.Move(Vector2.zero);
        yield return Wait(0.5f);

        var p = wizard.transform.position;
        float ground = TerrainGround(p);
        Metric($"{name}_out_m", Vector3.Dot(p - start, outward));
        Check($"{name}_edge_holds", land.Contains(new Vector2(p.x, p.z)), $"wizard at ({p.x:0}, {p.z:0}), the land {land.xMin:0}..{land.xMax:0} x {land.yMin:0}..{land.yMax:0}");
        Check($"{name}_on_ground", p.y > ground - 1f && p.y < ground + 3f, $"wizard at y {p.y:0.0}, ground {ground:0.0}");
        Cut(p - outward * 9f + Vector3.up * 6f, p + outward * 12f);
        yield return Wait(0.3f);
        Shot(name);
    }

    // The terrains' own ground (the chemistry's GroundAt knows only its squares).
    static float TerrainGround(Vector3 p)
    {
        foreach (var t in Terrain.activeTerrains)
        {
            var o = t.transform.position;
            var s = t.terrainData.size;
            if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z) return o.y + t.SampleHeight(p);
        }
        return 0f;
    }

    void Teleport(Vector3 at)
    {
        var body = wizard.GetComponent<CharacterController>();
        body.enabled = false;
        wizard.transform.position = at;
        body.enabled = true;
    }
}
