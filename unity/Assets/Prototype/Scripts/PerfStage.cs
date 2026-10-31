using System.Collections;
using System.Linq;
using UnityEngine;

// Sets the scene for a PerfScenario: noon, the weather held to one kind by a
// rule switch (the mod the probes use over sim.json), and the camera cut to a
// fixed shot, so every run films the same thing. The meadow, storm, snow and
// fog stand on the `perf` probe's camera; the wildfire is the `wildfire` probe's
// (the Wildfire mod on, Fire Bolts into the meadow north of the yard); the
// village films Hollowmere's square with the wizard standing in it. Ready says
// when it's done; the bot's route needs none of this. The forest walks the wizard
// through the chunks of playtest finding F-0002 by the stick, as the bot does, at
// night unless Hour says otherwise; aim holds each kind of aim marker on a moving
// aim; flamewall stands a wall of flames close to the camera, kept alight; pso
// tries every loaded material on URP's Particles/Lit shader (PerfPsoCheck).
public class PerfStage : ShowcaseScript
{
    // Hollowmere's place and square in cells (world/villages.json).
    const int HollowmereX = 222, HollowmereY = 300, SquareX = 5, SquareY = 6;
    static readonly int[] BoltsAt = { 2, 8, 14, 20, 25 };
    const float BoltGap = 0.4f, Noon = 12f, Night = 21.5f;
    // The route in chunks: F-0002's, in a ring through the forest, walked until the run ends.
    static readonly (int cx, int cy)[] ForestRoute = { (6, 7), (6, 6), (5, 6), (5, 5), (4, 5), (4, 4), (5, 4), (6, 4), (6, 5), (7, 5), (7, 6), (6, 6) };
    const int ChunkCells = 64, WallCells = 40, WallDeep = 6;
    const float StuckSeconds = 6f, JumpEverySeconds = 1f, ArriveMetres = 3f;
    const float AimSecondsEach = 5f, AimStepSeconds = 0.1f, AimRingMetres = 14f, RelightSeconds = 0.5f;

    /// <summary>The scenario's name. Set before Start.</summary>
    public string Scenario { get; set; }
    /// <summary>The hour to set, or a negative number for the scenario's own.</summary>
    public float Hour { get; set; } = -1f;
    /// <summary>Whether the scene is set.</summary>
    public bool Ready { get; private set; }
    /// <summary>The measuring has begun: the wizard sets off.</summary>
    public bool Go { get; set; }

    protected override IEnumerator Play()
    {
        yield return Sky(Scenario is "storm" or "snow" or "fog" ? Scenario : "clear");
        if (Scenario == "wildfire") yield return Wildfire();
        else if (Scenario == "village") Hollowmere();
        else if (Scenario == "forest") LogMap();
        else Cut(V(40f, 10f, 1f), V(28f, 0f, 15f));
        if (Scenario == "forest") StartCoroutine(Walk());
        if (Scenario == "aim") StartCoroutine(Aim());
        if (Scenario == "flamewall") StartCoroutine(FlameWall());
        if (Scenario == "pso") yield return new PerfPsoCheck(this).Run();
        Ready = true;
    }

    // Noon, this weather and no other, ramped in at once.
    IEnumerator Sky(string only)
    {
        var kinds = new[] { "clear", "cloudy", "windy", "fog", "rain", "storm", "snow" };
        var weights = string.Join(", ", kinds.Select(k => $"'{k}': {(k == only ? 1 : 0)}"));
        float hour = Hour >= 0f ? Hour : Scenario == "forest" ? Night : Noon;
        return Switch(At(hour) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': { " + weights + " } }");
    }

    // What stands where, chunk by chunk: for choosing a route, and for reading a spike's chunk.
    void LogMap()
    {
        var line = new System.Text.StringBuilder("PERF MAP cx,cy:trees/wet");
        foreach (var (cx, cy) in World.ChunkCoords.OrderBy(c => c.cy).ThenBy(c => c.cx))
        {
            int trees = 0, wet = 0;
            for (int y = cy * ChunkCells; y < (cy + 1) * ChunkCells; y++)
                for (int x = cx * ChunkCells; x < (cx + 1) * ChunkCells; x++)
                {
                    var c = World.At(x, y);
                    if (c == null) continue;
                    if (c.T == TerrainIds.Tree) trees++;
                    if (c.Water > 0) wet++;
                }
            line.Append($" {cx},{cy}:{trees}/{wet}");
        }
        Debug.Log(line);
    }

    Vector3 ChunkMiddle((int cx, int cy) chunk)
    {
        var at = grid.CellCenter(chunk.cx * ChunkCells + ChunkCells / 2, chunk.cy * ChunkCells + ChunkCells / 2);
        at.y = grid.GroundAt(at);
        return at;
    }

    // The stick, sprinting, as PlaytestPlayer steers: jumps when blocked, and a teleport (counted) when stuck.
    IEnumerator Walk()
    {
        Teleport(ChunkMiddle(ForestRoute[0]) + Vector3.up * 0.1f);
        var inputs = wizard.GetComponent<StarterAssets.StarterAssetsInputs>();
        inputs.cursorInputForLook = false;
        while (!Go) yield return null;
        for (int leg = 1; ; leg = leg % (ForestRoute.Length - 1) + 1)
        {
            var target = ChunkMiddle(ForestRoute[leg]);
            float stuckSince = Time.time, nextJump = Time.time + JumpEverySeconds, best = float.MaxValue;
            var from = wizard.transform.position;
            while (true)
            {
                var to = target - wizard.transform.position;
                to.y = 0f;
                if (to.magnitude < ArriveMetres) break;
                if (to.magnitude < best - 1f)
                {
                    best = to.magnitude;
                    stuckSince = Time.time;
                }
                float rel = (Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - Camera.main.transform.eulerAngles.y) * Mathf.Deg2Rad;
                inputs.move = new Vector2(Mathf.Sin(rel), Mathf.Cos(rel));
                inputs.sprint = true;
                if (Time.time >= nextJump)
                {
                    inputs.jump = (wizard.transform.position - from).magnitude < 0.3f;
                    from = wizard.transform.position;
                    nextJump = Time.time + JumpEverySeconds;
                }
                if (Time.time - stuckSince > StuckSeconds)
                {
                    PerfCounts.Add("walk.teleports");
                    Teleport(target + Vector3.up * 0.1f);
                    break;
                }
                yield return null;
            }
            PerfCounts.Add("walk.legs");
        }
    }

    // One spell of each kind of marker, its aim walking round a ring so the mesh is redrawn all the time.
    IEnumerator Aim()
    {
        var mid = V(28f, 0f, 15f);
        Place(13, 10);
        var spells = World.Rules.Spells.Where(s => AimMarker.KindOf(s) is not (AimKind.None or AimKind.Lock))
            .GroupBy(AimMarker.KindOf).Select(g => g.First()).ToList();
        caster.controls.enabled = true;
        caster.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        while (!Go) yield return null;
        for (float angle = 0f; ; )
            foreach (var spell in spells)
            {
                caster.Select(spell.Id);
                for (float t = 0f; t < AimSecondsEach; t += AimStepSeconds)
                {
                    angle += 0.2f;
                    var at = mid + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * AimRingMetres;
                    at.y = grid.GroundAt(at);
                    caster.AimOverride = at;
                    if (caster.Marker != null) PerfCounts.Peak("aim.vertices." + AimMarker.KindOf(spell), caster.Marker.Vertices);
                    yield return new WaitForSeconds(AimStepSeconds);
                }
            }
    }

    // A wall WallCells long and WallDeep thick on open ground, the camera low and close, kept burning at its hottest.
    IEnumerator FlameWall()
    {
        var mid = grid.CellAt(V(28f, 0f, 15f));
        Place(13, 10);
        var at = grid.CellCenter(mid.x, mid.y);
        at.y = grid.GroundAt(at) + 1f;
        Cut(at + V(0f, 1.5f, -10f), at + V(0f, 1f, 0f));
        while (true)
        {
            for (int y = 0; y < WallDeep; y++)
                for (int x = -WallCells / 2; x < WallCells / 2; x++)
                {
                    var c = World.At(mid.x + x, mid.y + y);
                    if (c == null || c.Water > 0) continue;
                    c.Fire = 3;
                    c.Fuel = 100;
                }
            yield return new WaitForSeconds(RelightSeconds);
        }
    }

    /// <summary>Takes the camera to a fixed shot; for PerfPsoCheck.</summary>
    public void CutTo(Vector3 position, Vector3 lookAt) => Cut(position, lookAt);

    // After Sky: a rule switch replaces the mods, a Toggle adds to them.
    IEnumerator Wildfire()
    {
        if (grid.TryGetComponent<RuleSwitcher>(out var switcher)) switcher.Toggle("wildfire");
        Place(13, 10);
        Cut(Yard(13, 4) + V(0f, 30f, -26f), Yard(13, 0));
        foreach (int x in BoltsAt) yield return Cast(SpellIds.FireBolt, x, 0, 0, BoltGap);
    }

    void Hollowmere()
    {
        int x = HollowmereX + SquareX, y = HollowmereY + SquareY;
        PlaceCell(x, y);
        var at = wizard.transform.position;
        Cut(at + V(-16f, 11f, -16f), at);
    }
}
