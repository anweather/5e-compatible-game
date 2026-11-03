using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Chem;

// The forestload scenario: a player's fire, as Andrew plays it. With the Wildfire mod on, a Fireball
// goes into the valley's thickest forest, and while it spreads the wizard walks a widening ring round
// it with the camera on the fire, casting something else every few seconds, a Fireball or a Meteor
// into the fire every other time, and at RainAtSeconds calls the rain, as his worst minutes came
// (-perf-no-rain leaves it dry). Everything goes through a virtual pad and
// the wizard's own controls, so the times are a player's: from the release of Cast to the cast
// starting, and from the stick to the wizard moving, read at the end of the frame that shows it.
// The script runs on real seconds, as a player does, so a world that falls behind burns less.
public partial class PerfStage
{
    const float RingStartMetres = 24f, RingGrowthPerSecond = 0.15f, RingStepDegrees = 25f;
    const float CastGapSeconds = 3f, StopEverySeconds = 20f, StopSeconds = 0.8f;
    const float LookGain = 0.02f, MovedMetres = 0.01f, GaveUpSeconds = 1f, AimAheadMetres = 7f;
    const int TapFrames = 2, SwitchFrames = 30, ChaseCells = 60, ChaseFrom = 3;
    const float SwitchHoldSeconds = 3f;
    static readonly float[] SwitchAtSeconds = { 22f, 36f, 270f, 290f, 310f };
    const float RainAtSeconds = 120f, RainHoldsSeconds = 235f, MostCastMetres = 18f;
    const int WeatherSteps = 4;
    static readonly string[] BigSpells = { SpellIds.Fireball, SpellIds.Meteor };
    static readonly string[] OtherSpells =
    {
        SpellIds.FireBolt, SpellIds.RayOfFrost, SpellIds.GustOfWind, SpellIds.Thunderwave,
        SpellIds.BurningHands, SpellIds.ShockingGrasp, SpellIds.CreateWater, SpellIds.FogCloud,
    };

    /// <summary>Each cast's time from the release of Cast to the cast starting: seconds into the run, then ms.</summary>
    public List<(float at, float ms)> CastLatency { get; } = new();
    /// <summary>Each stop's time from the stick to the wizard moving: seconds into the run, then ms.</summary>
    public List<(float at, float ms)> MoveLatency { get; } = new();

    Gamepad pad;
    Vector2 stick, lookStick;
    float trigger, wentAt;
    Vector3 fire;
    bool holdStill;

    void ForestLoadStage()
    {
        LogMap();
        if (grid.TryGetComponent<RuleSwitcher>(out var switcher)) switcher.Toggle("wildfire");
        var thickest = World.ChunkCoords.OrderByDescending(TreesIn).ThenBy(c => c.cy).ThenBy(c => c.cx).First();
        fire = ChunkMiddle(thickest);
        Debug.Log($"PERF FORESTLOAD chunk={thickest.cx},{thickest.cy} trees={TreesIn(thickest)}");
        Teleport(RingPoint(0f, RingStartMetres) + Vector3.up * 0.1f);
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        pad = InputSystem.AddDevice<Gamepad>("Perf pad");
        wizard.GetComponent<PlayerControls>().ElementPad = false;
        caster.controls.enabled = true;
        caster.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        StartCoroutine(ForestLoad());
    }

    int TreesIn((int cx, int cy) chunk)
    {
        int trees = 0;
        for (int y = chunk.cy * ChunkCells; y < (chunk.cy + 1) * ChunkCells; y++)
            for (int x = chunk.cx * ChunkCells; x < (chunk.cx + 1) * ChunkCells; x++)
                if (World.At(x, y)?.T == TerrainIds.Tree) trees++;
        return trees;
    }

    Vector3 RingPoint(float degrees, float metres)
    {
        var at = fire + Quaternion.Euler(0f, degrees, 0f) * Vector3.forward * metres;
        at.y = grid.GroundAt(at);
        return at;
    }

    IEnumerator ForestLoad()
    {
        var inputs = wizard.GetComponent<StarterAssets.StarterAssetsInputs>();
        inputs.cursorInputForLook = false;
        StartCoroutine(HoldThePad());
        while (!Go) yield return null;
        wentAt = Time.realtimeSinceStartup;
        inputs.sprint = true;
        if (Asked("-perf-look-switch")) StartCoroutine(LookSwitches());
        if (Asked("-perf-scope-near")) NearMeGreen();
        StartCoroutine(WalkTheRing());
        StartCoroutine(StopNowAndThen());
        yield return CastAt(SpellIds.Fireball, fire);
        float rainDue = Asked("-perf-no-rain") ? float.PositiveInfinity : RainAtSeconds;
        for (int spell = 0; enabled && wizard != null; spell++)
        {
            yield return new WaitForSecondsRealtime(CastGapSeconds);
            if (Time.realtimeSinceStartup - wentAt >= rainDue)
            {
                // A held Control Weather lasts 4 minutes, then four more steps of it come back to rain.
                for (int step = rainDue > RainAtSeconds ? WeatherSteps : 1; step > 0; step--) yield return CastAt(SpellIds.ControlWeather, wizard.transform.position, Chem.World.MaxCharge);
                rainDue += RainHoldsSeconds;
                continue;
            }
            if (spell % 2 == 0)
            {
                yield return CastAt(BigSpells[spell / 2 % BigSpells.Length], TowardsFire());
                continue;
            }
            var ahead = wizard.transform.position + wizard.transform.forward * AimAheadMetres;
            ahead.y = grid.GroundAt(ahead);
            yield return CastAt(OtherSpells[spell / 2 % OtherSpells.Length], ahead);
        }
    }

    // As far towards the fire's start as a cast from here goes.
    Vector3 TowardsFire()
    {
        var at = Vector3.MoveTowards(wizard.transform.position, fire, MostCastMetres);
        at.y = grid.GroundAt(at);
        return at;
    }

    // "The fire near me green", on from the start, so the whole walk is with a moving scope.
    void NearMeGreen()
    {
        var switcher = grid.GetComponent<RuleSwitcher>();
        var bench = new Chem.Mcp.Workbench(ChemGrid.Folders.Rules) { Here = new Chem.Mcp.StandInHere(new Chem.Mcp.HereSpot(switcher.Me, 0, 0)) };
        var draft = bench.SetLook(null, ElementIds.Fire, "color", "[0.2, 0.9, 0.3]", "elements", out _, ("{\"around\": \"me\", \"within\": 10}", null));
        string mod = new Newtonsoft.Json.Linq.JObject { ["title"] = "Near me green", ["patch"] = draft.Patch }.ToString();
        Debug.Log("PERF SCOPE " + switcher.Ask(RoomCommand.Apply(switcher.Me, "near-me-green", mod, "Near me green")));
    }

    // What a look's rule switch costs: all fire blue and the grass recoloured, each on and off through
    // the room as set_look's draft is, at the burn's height and well into the rain.
    IEnumerator LookSwitches()
    {
        var switcher = grid.GetComponent<RuleSwitcher>();
        string fire = "{ \"title\": \"Blue fire\", \"patch\": { \"elements\": { \"fire\": { \"look\": { \"color\": [0.25, 0.45, 1] } } } } }";
        var grass = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.Linq.JObject.Parse(World.Rules.ToJson())["terrain"]?[TerrainIds.Grass]?.DeepClone();
        if (grass == null) grass = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.Linq.JObject.Parse(World.Rules.ToJson())["materials"]["terrain"][TerrainIds.Grass].DeepClone();
        grass["look"]["color"] = new Newtonsoft.Json.Linq.JArray(0.25, 0.45, 1);
        string green = "{ \"title\": \"Blue grass\", \"patch\": { \"terrain\": { \"grass\": " + grass + " } } }";
        foreach (float at in SwitchAtSeconds)
        {
            while (Time.realtimeSinceStartup - wentAt < at) yield return null;
            yield return TimeSwitch(switcher, "fire", fire);
            yield return TimeSwitch(switcher, "grass", green);
        }
    }

    IEnumerator TimeSwitch(RuleSwitcher switcher, string name, string mod)
    {
        string id = "look-switch-" + name;
        switcher.Ask(RoomCommand.Apply(switcher.Me, id, mod, id));
        yield return WorstFrames(name + "_on");
        yield return new WaitForSecondsRealtime(SwitchHoldSeconds);
        switcher.Ask(RoomCommand.Revert(switcher.Me, id));
        yield return WorstFrames(name + "_off");
        yield return new WaitForSecondsRealtime(SwitchHoldSeconds);
    }

    IEnumerator WorstFrames(string name)
    {
        float worst = 0f, before = 0f;
        for (int i = 0; i < SwitchFrames; i++)
        {
            yield return null;
            worst = Mathf.Max(worst, Time.unscaledDeltaTime * 1000f);
            before += Time.unscaledDeltaTime * 1000f;
        }
        Debug.Log($"PERF LOOK SWITCH {name} t={(Time.realtimeSinceStartup - wentAt).ToString("0", System.Globalization.CultureInfo.InvariantCulture)} worst_ms={worst.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} mean_ms={(before / SwitchFrames).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
    }

    // The pad's state, once a frame, as the steering asked for it.
    IEnumerator HoldThePad()
    {
        while (enabled && wizard != null)
        {
            var toFire = fire - wizard.transform.position;
            float off = Mathf.DeltaAngle(Camera.main.transform.eulerAngles.y, Mathf.Atan2(toFire.x, toFire.z) * Mathf.Rad2Deg);
            lookStick = new Vector2(Mathf.Clamp(off * LookGain, -1f, 1f), 0f);
            SendPad();
            yield return null;
        }
    }

    void SendPad() => InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = stick, rightStick = lookStick, rightTrigger = trigger });

    IEnumerator WalkTheRing()
    {
        float degrees = 0f;
        while (enabled && wizard != null)
        {
            degrees += RingStepDegrees;
            // -perf-walk-through goes from fire to fire instead of walking round them.
            var target = Asked("-perf-walk-through") ? NearestFire(degrees) : RingPoint(degrees, RingStartMetres + (Time.realtimeSinceStartup - wentAt) * RingGrowthPerSecond);
            float best = float.MaxValue, stuckSince = Time.realtimeSinceStartup;
            while (enabled && wizard != null)
            {
                var to = target - wizard.transform.position;
                to.y = 0f;
                if (to.magnitude < ArriveMetres) break;
                if (to.magnitude < best - 1f)
                {
                    best = to.magnitude;
                    stuckSince = Time.realtimeSinceStartup;
                }
                if (holdStill) stuckSince = Time.realtimeSinceStartup;
                else stick = StickTowards(to);
                if (Time.realtimeSinceStartup - stuckSince > StuckSeconds)
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

    // The burning cell nearest the wizard and a few cells on, or the ring if nothing burns in reach.
    Vector3 NearestFire(float degrees)
    {
        var me = grid.CellAt(wizard.transform.position);
        var best = new Vector2Int(-1, -1);
        int nearest = int.MaxValue;
        for (int y = me.y - ChaseCells; y <= me.y + ChaseCells; y++)
            for (int x = me.x - ChaseCells; x <= me.x + ChaseCells; x++)
            {
                int d = (x - me.x) * (x - me.x) + (y - me.y) * (y - me.y);
                if (d <= ChaseFrom * ChaseFrom || d >= nearest || World.At(x, y) is not { Fire: > 0 }) continue;
                nearest = d;
                best = new Vector2Int(x, y);
            }
        if (best.x < 0) return RingPoint(degrees, RingStartMetres);
        var at = grid.CellCenter(best.x, best.y);
        at.y = grid.GroundAt(at);
        return at;
    }

    static Vector2 StickTowards(Vector3 to)
    {
        float rel = (Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - Camera.main.transform.eulerAngles.y) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(rel), Mathf.Cos(rel));
    }

    // Now and then the wizard stands still, then sets off, to time the stick.
    IEnumerator StopNowAndThen()
    {
        var shown = new WaitForEndOfFrame();
        while (enabled && wizard != null)
        {
            yield return new WaitForSecondsRealtime(StopEverySeconds);
            var going = stick;
            holdStill = true;
            stick = Vector2.zero;
            yield return new WaitForSecondsRealtime(StopSeconds);
            var from = wizard.transform.position;
            stick = going;
            SendPad();
            float pressed = Time.realtimeSinceStartup;
            while (Horizontal(wizard.transform.position - from) < MovedMetres && Time.realtimeSinceStartup - pressed < GaveUpSeconds) yield return shown;
            MoveLatency.Add((pressed - wentAt, (Time.realtimeSinceStartup - pressed) * 1000f));
            holdStill = false;
        }
    }

    static float Horizontal(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    // A tap of Cast at this point. Every spell charges, so it goes on the release.
    IEnumerator CastAt(string spell, Vector3 at, int charge = 1)
    {
        var shown = new WaitForEndOfFrame();
        caster.Select(spell);
        caster.AimOverride = at;
        yield return null;
        trigger = 1f;
        for (int i = 0; i < TapFrames || caster.Charge < charge; i++) yield return null;
        int had = caster.CastsStarted;
        trigger = 0f;
        SendPad();
        float released = Time.realtimeSinceStartup;
        while (caster.CastsStarted == had && Time.realtimeSinceStartup - released < GaveUpSeconds) yield return shown;
        if (caster.CastsStarted == had) PerfCounts.Add("cast.no_cast");
        else CastLatency.Add((released - wentAt, (Time.realtimeSinceStartup - released) * 1000f));
    }
}
