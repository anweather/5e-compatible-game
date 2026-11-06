using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's jobs scenario (docs/WORLD_LIFE.md, "Jobs"): the people with work to go to, met where the clock says they are, and
// the lake house and its dock, walked as a wizard walks them. The cells it uses are the valley's; nothing here is in the game.
public class JobsProbe : ProbeScenario
{
    const string Clear = "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 }";
    const int DockFootX = 250, DockEndX = 266, DockY = 374;
    const float MostWalk = 25f, NearWorker = 6f;

    Vector3 Ground(int x, int y)
    {
        var p = grid.CellCenter(x, y);
        p.y = grid.GroundAt(p);
        return p;
    }

    protected override IEnumerator Run()
    {
        var fauna = FindAnyObjectByType<ValleyFauna>();
        Check("fauna_found", fauna != null);
        if (fauna == null) yield break;
        yield return Switch(At(11f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Clear + " }");
        yield return Wait(1f);
        int workers = fauna.Jobs?.Workers.Count ?? 0;
        Metric("workers", workers);
        Metric("jobs_built_ms", fauna.JobsBuiltMs);
        Check("workers_have_jobs", workers == 7, $"{workers}");

        foreach (string job in new[] { "guard", "porter", "fisher", "net-mender" })
        {
            var worker = fauna.Jobs.Workers.First(w => w.Job == job);
            var now = worker.At(fauna.JobClock);
            var at = new Vector3(now.At.x, 0f, now.At.y);
            at.y = grid.GroundAt(at);
            Teleport(at + new Vector3(5f, 0.3f, 5f));
            Cut(at + new Vector3(4f, 2.5f, -4f), at + Vector3.up);
            yield return Wait(2f);
            now = worker.At(fauna.JobClock);
            var there = fauna.NearestWorker(new Vector3(now.At.x, 0f, now.At.y), out float apart);
            Check($"{job}_stands_where_the_clock_says", there && apart < NearWorker, $"{apart:0.0} m off");
            at = new Vector3(now.At.x, 0f, now.At.y);
            at.y = grid.GroundAt(at);
            Cut(at + new Vector3(4f, 2.5f, -4f), at + Vector3.up);
            yield return Wait(0.8f);
            Shot($"worker-{job}");
        }

        var foot = Ground(DockFootX, DockY);
        Teleport(foot + Vector3.up * 0.8f);
        yield return Wait(1f);
        var walk = puppet.WalkTo(grid.CellCenter(DockEndX, DockY));
        for (float t = 0f; t < MostWalk && walk.MoveNext(); t += Time.deltaTime) yield return walk.Current;
        puppet.Move(Vector2.zero);
        var cell = grid.CellAt(wizard.transform.position);
        float water = grid.WaterSurfaceY(cell.x, cell.y);
        Metric("deck_over_water_m", wizard.transform.position.y - water);
        Check("the_dock_carries_a_wizard", cell.x >= DockEndX - 3 && wizard.transform.position.y > water, $"at {cell}, y {wizard.transform.position.y:0.0}, water {water:0.0}");
        float deck = wizard.transform.position.y;
        Vector3 Over(int x, int y, float up)
        {
            var p = grid.CellCenter(x, y);
            p.y = deck + up;
            return p;
        }
        Cut(Over(272, 384, 7f), Over(256, DockY, 0f));
        yield return Wait(0.8f);
        Shot("dock");
        Cut(Over(262, 382, 3f), Over(244, DockY, 1f));
        yield return Wait(0.8f);
        Shot("lake-house");
    }
}
