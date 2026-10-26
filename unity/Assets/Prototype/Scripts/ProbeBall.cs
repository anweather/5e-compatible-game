using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's ball scenario (docs/FAST_LOOP.md): the court east of the yard
// and its ball (ThingSpawner spawns it from world/things.json). The wizard
// runs at the ball from 6 m away (Kicker). Checks the court is built, the ball
// is there, the run kicks it more than 2 m and it stays inside the court.
public class BallProbe : ProbeScenario
{
    float ballYAtSpawn = float.NaN;

    protected override IEnumerator Run()
    {
        var early = FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None).FirstOrDefault(p => p.kind == ItemIds.Ball);
        if (early != null) ballYAtSpawn = early.transform.position.y;
        var court = GameObject.Find("Ball court");
        Check("court_built", court != null && court.GetComponentsInChildren<Collider>().Length >= 8, "the Ball court with its rails and goals");
        var ball = FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None).FirstOrDefault(p => p.kind == ItemIds.Ball);
        Check("ball_spawned", ball != null, "a ball from world/things.json");
        if (ball == null || court == null) yield break;

        var start = ball.transform.position;
        Metric("ball_start_y", start.y);
        Metric("ground_at_ball", grid.GroundAt(start));
        Metric("ball_spawn_seen_y", ballYAtSpawn);
        // From the west, running east at it.
        var from = start - Vector3.right * 6f;
        var body = wizard.GetComponent<CharacterController>();
        body.enabled = false;
        wizard.transform.position = new Vector3(from.x, grid.GroundAt(from) + 0.05f, from.z);
        body.enabled = true;
        Cut(from + new Vector3(-6f, 4f, 0f), start);
        yield return Wait(0.3f);
        Shot("before");
        var rb = ball.GetComponent<Rigidbody>();
        Metric("ball_kinematic", rb != null && rb.isKinematic ? 1 : 0);
        Metric("ball_kg", rb != null ? rb.mass : -1);
        puppet.Move(Vector2.up);
        yield return Wait(4f);
        puppet.Move(Vector2.zero);
        Metric("wizard_end_x", wizard.transform.position.x);
        Metric("wizard_ball_gap_m", Vector3.Distance(wizard.transform.position, ball.transform.position));
        yield return Wait(2f);
        Shot("kicked");

        var end = ball.transform.position;
        float moved = Vector3.Distance(new Vector3(start.x, 0f, start.z), new Vector3(end.x, 0f, end.z));
        Metric("ball_moved_m", moved);
        Check("run_kicks_ball", moved > 2f, $"the ball went {moved:0.0} m");
        var walls = court.GetComponentsInChildren<Renderer>();
        var inside = walls[0].bounds;
        foreach (var r in walls) inside.Encapsulate(r.bounds);
        // The goals stand out past the end walls: a goal scored still counts as in.
        Check("ball_stays_in_court", end.x > inside.min.x && end.x < inside.max.x && end.z > inside.min.z && end.z < inside.max.z,
            $"ball at ({end.x:0.0}, {end.z:0.0}), court x {inside.min.x:0}..{inside.max.x:0}, z {inside.min.z:0}..{inside.max.z:0}");
    }
}
