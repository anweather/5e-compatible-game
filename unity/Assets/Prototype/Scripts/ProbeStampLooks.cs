using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's stamplooks scenario (#147, #148): land stamps seen small and with crates on them. A radius 3 dome
// should read as a bump, not a rock patch. Then crates on a 15 m dome as it rises, and the same crates set on the
// still dome with nothing carrying them: if the carried ones go much further, the carry flings them; if not, it's
// the slope.
public class StampLooksProbe : LandShapesProbe
{
    const int Crates = 10, SmallRadius = 3, SmallRise = 4, DomeRadius = 15, DomeRise = 15;

    sealed class Watched
    {
        public PhysicsProp Prop;
        public Rigidbody Body;
        public Vector3 Home;
        public float Fastest, Highest;
    }

    ValleyChemistry valley;
    double carryMs, carryMostMs;
    int carryFrames, carryFound, carryCarried;

    Vector3 Centre => grid.CellCenter(X, Y);

    IEnumerator Stamp(int radius, int rise)
    {
        yield return Say("act", $"{{\"what\":\"land\",\"near\":\"[{X}, {Y}]\",\"radius\":{radius},\"rise\":{rise},\"profile\":\"dome\"}}");
        Check($"dome_{radius}_moved_the_land", said.Contains("moved the land"), said.Length > 200 ? said.Substring(0, 200) : said);
    }

    // Stamped cells around the middle that paint mostly rock.
    static int RockCells(World world, int reach)
    {
        int rock = CellPaint.Layers.ToList().IndexOf(CellPaint.Rock), n = 0;
        for (int y = Y - reach; y <= Y + reach; y++)
            for (int x = X - reach; x <= X + reach; x++)
                if (CellPaint.Mix(world, x, y)[rock] > 0.5f) n++;
        return n;
    }

    // The slope of the drawn ground under a point, in degrees.
    float Slope(Vector3 p)
    {
        const float d = 0.75f;
        float dx = valley.GroundAt(p + Vector3.right * d) - valley.GroundAt(p - Vector3.right * d);
        float dz = valley.GroundAt(p + Vector3.forward * d) - valley.GroundAt(p - Vector3.forward * d);
        return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) / (2f * d)) * Mathf.Rad2Deg;
    }

    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    // Watches the crates for some seconds: their top speeds and highest jump off the ground, and the land carry's cost.
    IEnumerator Watch(List<Watched> crates, float seconds)
    {
        foreach (var c in crates) c.Fastest = c.Highest = 0f;
        int seen = -1;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            yield return null;
            foreach (var c in crates)
            {
                c.Fastest = Mathf.Max(c.Fastest, c.Body.linearVelocity.magnitude);
                var box = c.Prop.GetComponentInChildren<Collider>().bounds;
                c.Highest = Mathf.Max(c.Highest, box.min.y - valley.GroundAt(box.center));
            }
            var cost = grid.LandCarryCost;
            if (cost.frame == seen || cost.frame != Time.frameCount) continue;
            seen = cost.frame;
            carryFrames++;
            carryMs += cost.ms;
            carryMostMs = System.Math.Max(carryMostMs, cost.ms);
            carryFound = Mathf.Max(carryFound, cost.found);
            carryCarried = Mathf.Max(carryCarried, cost.carried);
        }
    }

    int Buried(List<Watched> crates) =>
        crates.Count(c => c.Prop.GetComponentInChildren<Collider>().bounds.center.y < valley.GroundAt(c.Body.position) - 0.2f);

    void Report(string phase, List<Watched> crates)
    {
        var far = crates.Select(c => Flat(c.Body.position, c.Home)).ToList();
        Metric($"{phase}_slid_most_m", far.Max());
        Metric($"{phase}_slid_mean_m", far.Average());
        Metric($"{phase}_fastest_m_s", crates.Max(c => c.Fastest));
        Metric($"{phase}_highest_off_ground_m", crates.Max(c => c.Highest));
        Metric($"{phase}_buried", Buried(crates));
        Debug.Log($"[stamplooks] {phase}: " + string.Join("; ", crates.Select(c =>
            $"home {Flat(c.Home, Centre):0.0} m out, slope there {Slope(c.Home):0} deg, slid {Flat(c.Body.position, c.Home):0.0} m, top {c.Fastest:0.0} m/s, up {c.Highest:0.00} m")));
    }

    protected override IEnumerator Run()
    {
        var server = RulesMcpServer.Instance;
        if (!server.Serving) server.Serving = true;
        valley = FindAnyObjectByType<ValleyChemistry>();
        var world = grid.World;
        PlaceCell(X - 28, Y - 28);
        yield return Wait(1f);

        // #148: a small stamp.
        var centre = Centre;
        centre.y = valley.GroundAt(centre);
        Cut(centre + new Vector3(-11f, 7f, -11f), centre);
        yield return Stamp(SmallRadius, SmallRise);
        yield return Wait(12f);
        Metric("small_rock_cells", RockCells(world, SmallRadius + 1));
        Metric("small_rocks", valley.Rocks.Near(X, Y, SmallRadius + 4));
        Metric("small_top_m", valley.Ground.ShownGround(X, Y) - valley.GroundAt(centre + new Vector3(8f, 0f, 8f)));
        Check("a_small_stamp_is_a_bump_not_rock", RockCells(world, SmallRadius + 1) == 0 && valley.Rocks.Near(X, Y, SmallRadius + 4) == 0,
            $"{RockCells(world, SmallRadius + 1)} rock cells, {valley.Rocks.Near(X, Y, SmallRadius + 4)} rocks");
        Shot("small-stamp");
        yield return Say("undo_act", "{}");
        yield return Wait(10f);

        // #147: crates on a dome as it rises.
        yield return Say("act", $"{{\"what\":\"crate\",\"count\":{Crates},\"near\":\"[{X}, {Y}]\",\"within\":12,\"spacing\":3}}");
        yield return Wait(3f);
        var crates = FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None)
            .Where(p => p.name.StartsWith("crate", System.StringComparison.Ordinal) && Flat(p.transform.position, centre) < 25f)
            .Select(p => new Watched { Prop = p, Body = p.GetComponent<Rigidbody>(), Home = p.transform.position }).ToList();
        Check("crates_placed", crates.Count >= Crates / 2, $"{crates.Count} crates");
        if (crates.Count == 0) yield break;
        Cut(centre + new Vector3(-34f, 22f, -34f), centre + Vector3.up * 5f);
        yield return Stamp(DomeRadius, DomeRise);
        yield return Watch(crates, 30f);
        Report("carried", crates);
        Shot("dome-crates");
        Metric("carry_frames", carryFrames);
        Metric("carry_ms_mean", carryFrames > 0 ? carryMs / carryFrames : 0);
        Metric("carry_ms_most", carryMostMs);
        Metric("carry_colliders_found", carryFound);
        Metric("carry_bodies", carryCarried);
        Check("none_buried_on_the_dome", Buried(crates) == 0, $"{Buried(crates)} buried");
        float carriedUp = crates.Average(c => c.Highest), carriedFar = crates.Average(c => Flat(c.Body.position, c.Home));
        Check("the_carry_is_cheap", carryFrames > 0 && carryMostMs < 2.0, $"{carryMostMs:0.00} ms at most over {carryFrames} frames");

        // The same crates set on the still dome: what the slope alone does.
        var homes = crates.Select(c => c.Home).ToList();
        foreach (var c in crates)
        {
            var on = new Vector3(c.Home.x, valley.GroundAt(c.Home) + 0.6f, c.Home.z);
            c.Body.position = on;
            c.Body.rotation = Quaternion.identity;
            c.Body.linearVelocity = c.Body.angularVelocity = Vector3.zero;
            c.Prop.transform.SetPositionAndRotation(on, Quaternion.identity);
            c.Home = on;
        }
        yield return Watch(crates, 15f);
        Report("still", crates);
        // A crate tumbling down a 50 degree flank hops and runs on by itself: the carry may not add to that. Over the
        // crates, not the one that went furthest: one crate's tumble runs anywhere from 70 to 140 m either way.
        float stillUp = crates.Average(c => c.Highest), stillFar = crates.Average(c => Flat(c.Body.position, c.Home));
        Metric("carried_highest_mean_m", carriedUp);
        Metric("still_highest_mean_m", stillUp);
        Check("the_carry_flings_none_past_the_slope", carriedUp <= stillUp + 0.5f && carriedFar <= stillFar * 1.5f + 5f,
            $"a crate on average: carried up {carriedUp:0.0} m, {carriedFar:0} m off; on the still dome up {stillUp:0.0} m, {stillFar:0} m off");

        yield return Say("undo_act", "{}");
        for (int i = 0; i < crates.Count; i++) crates[i].Home = homes[i];
        yield return Watch(crates, 30f);
        Report("undone", crates);
        Check("none_buried_after_the_undo", Buried(crates) == 0, $"{Buried(crates)} buried");
        yield return Say("undo_act", "{}");
    }
}
