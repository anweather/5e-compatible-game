using System.Collections;
using UnityEngine;

// The probe's court scenario (docs/FAST_LOOP.md): the ball court east of the
// yard, its dirt floor, its gate and its scoreboard (ValleyBuilder.BuildCourt,
// BallCourt, CourtGate). Checks the floor in the court's middle is dirt (the
// cell and the terrain's paint), the gate opens as the wizard walks up to it
// and lets it onto the court, then shuts after it has gone, a ball sent into
// the east goal scores one there (on the board too) and comes back to the
// centre spot, and a ball out past the rails comes back too.
public class CourtProbe : ProbeScenario
{
    BallCourt court;
    CourtGate gate;

    protected override IEnumerator Run()
    {
        court = FindFirstObjectByType<BallCourt>();
        gate = FindFirstObjectByType<CourtGate>();
        Check("court_built", court != null && gate != null && court.board != null, "BallCourt, CourtGate and CourtScoreboard in the Ball court");
        if (court == null || gate == null) yield break;
        Floor();
        yield return Gate();
        yield return Goal();
        yield return Out();
    }

    // The middle of the court: a dirt cell, painted bare earth on the terrain, no grass on it.
    void Floor()
    {
        var at = court.centre;
        var cell = grid.CellAt(at);
        string kind = World.At(cell.x, cell.y).T;
        Check("floor_cell_dirt", kind == TerrainIds.Dirt, $"the cell at the centre spot is {kind}");
        float earth = EarthWeight(at, out int grass);
        Metric("floor_earth_weight", earth);
        Metric("floor_grass_details", grass);
        Check("floor_painted_dirt", earth > 0.5f, $"the terrain's Earth layer weighs {earth:0.00} at the centre spot");
        Check("floor_no_grass", grass == 0, $"{grass} grass and plant details at the centre spot");
    }

    // The Earth layer's weight in the terrain's paint at a point, and the details growing there.
    static float EarthWeight(Vector3 p, out int details)
    {
        details = -1;
        foreach (var t in Terrain.activeTerrains)
        {
            var data = t.terrainData;
            var o = t.transform.position;
            float u = (p.x - o.x) / data.size.x, v = (p.z - o.z) / data.size.z;
            if (u < 0f || u > 1f || v < 0f || v > 1f) continue;
            var paint = data.GetAlphamaps(Mathf.Min((int)(u * data.alphamapWidth), data.alphamapWidth - 1), Mathf.Min((int)(v * data.alphamapHeight), data.alphamapHeight - 1), 1, 1);
            details = 0;
            int dx = Mathf.Min((int)(u * data.detailWidth), data.detailWidth - 1), dz = Mathf.Min((int)(v * data.detailHeight), data.detailHeight - 1);
            for (int k = 0; k < data.detailPrototypes.Length; k++) details += data.GetDetailLayer(dx, dz, 1, 1, k)[0, 0];
            var layers = data.terrainLayers;
            for (int k = 0; k < layers.Length; k++)
                if (layers[k] != null && layers[k].name.StartsWith(Chem.CellPaint.Earth, System.StringComparison.Ordinal)) return paint[0, 0, k];
            return 0f;
        }
        return 0f;
    }

    // From 9 m west of the gate, the wizard walks through it onto the court and on, then waits.
    IEnumerator Gate()
    {
        var middle = gate.Middle;
        Check("gate_shut_at_start", gate.Openness < 0.05f, $"open {gate.Openness:0.00}");
        var from = middle + Vector3.left * 9f;
        Teleport(new Vector3(from.x, grid.GroundAt(from) + 0.05f, from.z));
        puppet.Face(middle);
        Cut(middle + V(-12f, 6f, -8f), middle + V(2f, 0f, 0f));
        yield return Wait(0.5f);
        Shot("gate-shut");

        var target = middle + Vector3.right * 6f;
        float opened = 0f, openedAt = -1f;
        bool shot = false;
        var walk = puppet.WalkTo(target);
        for (float t = 0f; t < 12f && walk.MoveNext(); t += Time.deltaTime)
        {
            opened = Mathf.Max(opened, gate.Openness);
            if (openedAt < 0f && gate.Openness > 0.9f) openedAt = Flat(wizard.transform.position - middle);
            if (!shot && Mathf.Abs(wizard.transform.position.x - middle.x) < 0.3f)
            {
                shot = true;
                Shot("gate-open");
            }
            yield return walk.Current;
        }
        puppet.Move(Vector2.zero);
        Metric("gate_opened_most", opened);
        Metric("gate_open_at_m", openedAt);
        Check("gate_opens", opened > 0.9f, $"opened to {opened:0.00}, wide open with the wizard {openedAt:0.0} m from the gateway");
        float inside = wizard.transform.position.x - middle.x;
        Metric("wizard_inside_m", inside);
        Check("walks_through_gate", inside > 3f, $"the wizard is {inside:0.0} m inside the west rail");

        yield return Wait(gate.stayOpen + gate.swingSeconds + 1f);
        Metric("gate_after_leaving", gate.Openness);
        Check("gate_shuts", gate.Openness < 0.05f, $"open {gate.Openness:0.00} with the wizard {Flat(wizard.transform.position - middle):0.0} m away");
        Shot("gate-shut-again");
    }

    // The ball sent at the east goal from 3 m out: one goal there, on the board, then back to the spot.
    IEnumerator Goal()
    {
        for (float t = 0f; t < 3f && court.Ball == null; t += Time.deltaTime) yield return null;
        Check("ball_found", court.Ball != null, "the court's ball");
        if (court.Ball == null) yield break;
        int west = court.West, east = court.East;
        int mark = ConsoleMark;
        var mouth = court.centre + Vector3.right * (court.length / 2f);
        Cut(mouth + V(-8f, 5f, -7f), mouth);
        Send(mouth + V(-3f, 0.4f, 0f), Vector3.right * 7f);
        for (float t = 0f; t < 3f && court.East == east; t += Time.deltaTime) yield return null;
        Shot("goal");
        Check("goal_scored", court.East == east + 1 && court.West == west, $"west {west} -> {court.West}, east {east} -> {court.East}");
        Check("goal_announced", ConsoleSaid("Goal at the east end", mark), "EventLog line");
        yield return null;
        Check("board_shows_score", court.board.West == court.West && court.board.East == court.East,
            $"board {court.board.West} : {court.board.East}, score {court.West} : {court.East}");
        yield return Wait(2.5f);
        float off = Flat(court.Ball.transform.position - court.centre);
        Metric("ball_from_spot_after_goal_m", off);
        Check("ball_back_after_goal", off < 1.5f, $"{off:0.0} m from the centre spot");
    }

    // The ball left outside the south rail: back on the spot a couple of seconds later.
    IEnumerator Out()
    {
        if (court.Ball == null) yield break;
        Send(court.centre + V(0f, 0.4f, -(court.width / 2f + 3f)), Vector3.zero);
        yield return Wait(1f);
        Check("ball_out", !court.OnCourt(court.Ball.transform.position), "the ball lies outside the rails");
        yield return Wait(3f);
        float off = Flat(court.Ball.transform.position - court.centre);
        Metric("ball_from_spot_after_out_m", off);
        Check("ball_back_after_out", off < 1.5f, $"{off:0.0} m from the centre spot");
    }

    void Send(Vector3 at, Vector3 velocity)
    {
        var ball = court.Ball;
        var body = ball.GetComponent<Rigidbody>();
        at.y += grid.GroundAt(at);
        ball.transform.position = at;
        body.position = at;
        body.angularVelocity = Vector3.zero;
        body.linearVelocity = velocity;
    }

    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
