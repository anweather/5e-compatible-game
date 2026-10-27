using System.Collections;
using System.Globalization;
using Chem;
using Unity.Cinemachine;
using UnityEngine;

// The base of a scripted, one-take showcase for trailer recordings
// (DemoRecorder.RecordUntil): finds the grid and the wizard, hides the HUD,
// plays the subclass's chapters on a fixed schedule so every take plays the
// same, then reports Done. The helpers place the wizard, cast, cut and fly
// the camera, all in game time (the recorder runs it at 30 frames a second).
// See OvernightShowcase and ValleyShowcase.
public abstract class ShowcaseScript : MonoBehaviour
{
    public bool Done { get; private set; }

    protected ChemGrid grid;
    protected GameObject wizard;
    protected WizardPuppet puppet;
    protected SpellCaster caster;
    protected ItemHolder hands;
    protected CinemachineBrain brain;
    Vector3 lookingAt;
    float started;

    protected World World => grid.World;

    protected static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    protected static FlythroughCamera.Waypoint W(Vector3 position, Vector3 lookAt, float seconds) =>
        FlythroughCamera.Waypoint.At(position, lookAt, seconds);

    /// <summary>The chapters, played once from Start.</summary>
    protected abstract IEnumerator Play();

    protected virtual void OnDestroy() => EventLog.HudVisible = true;

    IEnumerator Start()
    {
        started = Time.time;
        EventLog.HudVisible = false;
        grid = FindAnyObjectByType<ChemGrid>();
        wizard = WizardRig.FindLocal();
        puppet = new WizardPuppet(wizard);
        caster = wizard.GetComponent<SpellCaster>();
        caster.enabled = false;
        hands = wizard.GetComponent<ItemHolder>();
        brain = Camera.main.GetComponent<CinemachineBrain>();

        yield return Play();

        EventLog.HudVisible = true;
        Done = true;
    }

    protected void Chapter(string title)
    {
        Debug.Log($"Showcase chapter at {Time.time - started:0.00} s: {title}");
    }

    protected Vector3 Yard(int x, int y) => grid.YardCellCenter(x, y);

    // Puts out every fire in the world, between chapters, while the camera is elsewhere.
    protected void PutOutFires()
    {
        foreach (var cell in grid.World.Cells) cell.Fire = 0;
    }

    // Moves the wizard to a yard cell at once (off camera).
    protected void Place(int x, int y)
    {
        var body = wizard.GetComponent<CharacterController>();
        body.enabled = false;
        var cell = grid.YardCell(x, y);
        wizard.transform.position = grid.CellCenter(cell.x, cell.y) + Vector3.up * (grid.SurfaceY(cell.x, cell.y) + 0.05f);
        body.enabled = true;
    }

    // Puts the camera somewhere at once, taking it from Cinemachine.
    protected void Cut(Vector3 position, Vector3 lookAt)
    {
        if (brain != null) brain.enabled = false;
        var cam = Camera.main.transform;
        cam.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
        lookingAt = lookAt;
    }

    // Glides on from the current shot; the camera stays where the flight ends.
    protected FlythroughCamera Fly(params FlythroughCamera.Waypoint[] waypoints)
    {
        var flight = FlythroughCamera.FromHere(lookingAt, waypoints);
        lookingAt = waypoints[waypoints.Length - 1].lookAt;
        Destroy(flight.gameObject, Seconds(waypoints) + 0.5f);
        return flight;
    }

    // Game time, as the recorder runs it (30 frames a second of it, however slowly the Editor renders).
    protected static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
    }

    static float Seconds(FlythroughCamera.Waypoint[] waypoints)
    {
        float s = 0f;
        foreach (var w in waypoints) s += w.seconds;
        return s;
    }

    /// <summary>The valley terrains' height under a point (0 off them).</summary>
    protected static float TerrainGround(Vector3 p)
    {
        foreach (var t in Terrain.activeTerrains)
        {
            var o = t.transform.position;
            var s = t.terrainData.size;
            if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z) return o.y + t.SampleHeight(p);
        }
        return 0f;
    }

    /// <summary>Puts the wizard at a point at once (its CharacterController off for the move).</summary>
    protected void Teleport(Vector3 at)
    {
        var body = wizard.GetComponent<CharacterController>();
        body.enabled = false;
        wizard.transform.position = at;
        body.enabled = true;
    }

    /// <summary>Puts the wizard on the ground of a world cell at once.</summary>
    protected void PlaceCell(int x, int y)
    {
        var at = grid.CellCenter(x, y);
        Teleport(new Vector3(at.x, grid.GroundAt(at) + 0.05f, at.z));
    }

    // The clock's start hour that puts the next tick at this hour of day 1 or later.
    protected string At(float hour)
    {
        var p = World.Rules.Sim;
        // The clock's tick, not the world's: Hasten Time runs it ahead (World.ClockTick).
        double elapsed = (double)(World.ClockTick + 1) * Sky.MinutesPerDay / Sky.Ticks(p, p.Day.LengthMinutes) / 60.0;
        double start = ((hour - elapsed) % 24 + 24) % 24;
        return "'day': { 'startHour': " + start.ToString("0.###", CultureInfo.InvariantCulture) + " }";
    }

    // A rule switch to a mod over sim.json (replacing the last one), in force at the next tick.
    protected IEnumerator Switch(string sim)
    {
        var mod = new ActiveMod("showcase-sky", "{ 'patch': { 'sim': { " + sim + " } } }");
        grid.QueueRules(grid.CurrentRules.WithMods(new[] { mod }));
        yield return Wait(0.5f);
    }

    protected IEnumerator Cast(string spell, int x, int y, int dir, float wait)
    {
        var cell = grid.YardCell(x, y);
        puppet.Face(grid.CellCenter(cell.x, cell.y));
        caster.Cast(spell, cell.x, cell.y, dir);
        yield return Wait(wait);
    }
}
