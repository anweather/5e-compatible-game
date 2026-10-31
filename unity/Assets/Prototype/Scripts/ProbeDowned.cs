using System.Collections;
using Chem;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's downed scenario (#14, #18): first the punch on a virtual gamepad:
// Circle punches with free hands, cancels a cast instead of punching, and Square
// doesn't punch. Then struck by a stand-in in the core (WizardStrike): on open ground,
// against a wall, into water. Then the wizard, carrying a thing, takes more than its health:
// it falls, drops the thing and loses its controls, takes no more harm, and after
// sim.json's seconds is back at the yard with full health and its controls.
public class DownedProbe : SpellLooksProbe
{
    Gamepad pad;
    PlayerControls controls;
    Vitals vitals;
    int punches;

    protected override IEnumerator Run()
    {
        controls = caster.controls;
        bool controlsWereOn = controls != null && controls.enabled, casterWasOn = caster.enabled;
        if (controls != null) controls.enabled = true;
        caster.enabled = true;
        vitals = wizard.GetComponent<Vitals>();
        hands.PunchStarted += Counted;
        pad = InputSystem.AddDevice<Gamepad>("Downed pad");
        try
        {
            Place(8, 12);
            yield return Wait(0.5f);
            yield return Punch();
            yield return Struck();
            yield return Downed();
        }
        finally
        {
            hands.PunchStarted -= Counted;
            if (controls != null) controls.enabled = controlsWereOn;
            caster.enabled = casterWasOn;
            InputSystem.RemoveDevice(pad);
        }
    }

    void Counted(ItemHolder.PunchLanding punch) => punches++;

    IEnumerator Punch()
    {
        yield return Press(GamepadButton.East);
        Check("circle_punches", punches == 1, $"{punches} punches");
        yield return Wait(0.5f);
        yield return Press(GamepadButton.West);
        Check("square_doesnt_punch", punches == 1, $"{punches} punches");
        yield return Wait(0.5f);
        string me = caster.CastPlayer;
        yield return Press(GamepadButton.RightShoulder);
        Check("cast_under_way", World.CastOf(me)?.Casting == true, "water loaded");
        yield return Press(GamepadButton.East);
        Check("circle_cancels_instead", World.CastOf(me)?.Casting == false && punches == 1, $"casting {World.CastOf(me)?.Casting}, {punches} punches");
        yield return Wait(0.5f);
    }

    // Struck by a stand-in that only the core knows: a hit on open ground, against a wall and into water.
    IEnumerator Struck()
    {
        const string Other = "stand-in";
        var strike = wizard.GetComponent<WizardStrike>();
        var o = Spot(9, 5);
        Patch(o, 9, 5, TerrainIds.Dirt);
        var stand = new Vector2Int(o.x + 3, o.y + 2);
        vitals.infinite = false;

        PlaceCell(stand.x, stand.y);
        yield return HitBy(Other, stand);
        Check("struck_loses_the_damage", strike.HitsTaken == 1 && Mathf.Approximately(strike.DamageTaken, 1f) && Mathf.Approximately(vitals.Health, vitals.HealthMax - 1f), $"{strike.HitsTaken} hits, health {vitals.Health} of {vitals.HealthMax}");
        var now = grid.CellAt(wizard.transform.position);
        Check("struck_moves_one_cell", now == new Vector2Int(stand.x + 1, stand.y), $"from {stand} to {now}");
        Check("struck_core_agrees", World.Stands[caster.CastPlayer].X == now.x && World.Stands[caster.CastPlayer].Y == now.y && World.Stands[caster.CastPlayer].Hp == Mathf.CeilToInt(vitals.Health), World.StrikeText());

        // Blocking ground just behind: it stays put, and takes the hit.
        World.At(stand.x + 1, stand.y).T = TerrainIds.Stone;
        PlaceCell(stand.x, stand.y);
        yield return Wait(0.3f);
        yield return HitBy(Other, stand);
        now = grid.CellAt(wizard.transform.position);
        Check("struck_against_a_wall_stays", strike.HitsTaken == 2 && now == stand, $"{strike.HitsTaken} hits, from {stand} to {now}");

        // Water behind: it goes in, and whatever the water does then is what happens.
        World.At(stand.x + 1, stand.y).T = TerrainIds.Dirt;
        World.At(stand.x + 1, stand.y).Water = 2;
        yield return Wait(0.3f);
        PlaceCell(stand.x, stand.y);
        yield return Wait(0.3f);
        yield return HitBy(Other, stand);
        now = grid.CellAt(wizard.transform.position);
        Check("struck_into_water_goes_in", strike.HitsTaken == 3 && now == new Vector2Int(stand.x + 1, stand.y), $"{strike.HitsTaken} hits, from {stand} to {now}");
        World.At(stand.x + 1, stand.y).Water = 0;

        // The wizard's own strike, north: the aim's rows run against the world's z.
        PlaceCell(stand.x, stand.y);
        yield return Wait(0.3f);
        World.ApplyStrike(StrikeCommand.Stand(Other, stand.x, stand.y - 1, 12));
        StrikeHit seen = null;
        void Saw(StrikeHit hit) => seen = hit;
        strike.Struck += Saw;
        strike.Strike(Vector3.forward);
        yield return null;
        yield return null;
        strike.Struck -= Saw;
        Check("strike_goes_along_the_way", seen?.TargetPlayer == Other, $"struck {seen?.TargetPlayer}");
        World.ApplyStrike(StrikeCommand.Leave(Other));
        vitals.infinite = true;
        vitals.Refill();
        yield return Wait(0.3f);
    }

    // Strikes east at the wizard from the next cell west until one lands, a roll at a time.
    IEnumerator HitBy(string other, Vector2Int stand)
    {
        var strike = wizard.GetComponent<WizardStrike>();
        int hits = strike.HitsTaken;
        World.ApplyStrike(StrikeCommand.Stand(other, stand.x - 1, stand.y, 12));
        yield return null;
        for (int i = 0; i < 30 && strike.HitsTaken == hits; i++)
        {
            int tick = World.Tick + World.Rules.Sim.Strike.CooldownTicks;
            World.ApplyStrike(StrikeCommand.Strike(other, stand.x - 1, stand.y, 1, 0));
            yield return null;
            yield return null;
            if (strike.HitsTaken != hits) break;
            while (World.Tick < tick) yield return null;
        }
        yield return Wait(0.5f);
    }

    IEnumerator Downed()
    {
        var pose = wizard.GetComponent<DownedPose>();
        var item = FindObjectsByType<Carryable>(FindObjectsSortMode.InstanceID).FirstOrDefault(c => c.Holder == null);
        if (item != null) hands.Grant(item);
        Check("carrying", hands.Held != null, $"holding {hands.Held}");
        var fellAt = wizard.transform.position;
        float started = Time.time;
        vitals.TakeDamage(vitals.HealthMax * 2f);
        yield return null;
        Check("falls_at_zero", vitals.IsDown && vitals.Health <= 0f, $"down {vitals.IsDown}, health {vitals.Health}");
        Check("drops_what_it_carries", hands.Held == null, $"holding {hands.Held}");
        Check("no_controls", !controls.enabled, $"controls {controls.enabled}");
        yield return Wait(0.6f);
        Check("lies_down", pose.Lying && pose.Weight >= 1f, $"weight {pose.Weight:0.00}");
        Shot("down");
        float wait = (float)World.Rules.Sim.Downed.Seconds + 1f;
        while (vitals.IsDown && Time.time - started < wait) yield return null;
        float seconds = Time.time - started;
        Metric("seconds_down", seconds);
        Check("up_after_the_seconds", !vitals.IsDown && Mathf.Abs(seconds - (float)World.Rules.Sim.Downed.Seconds) < 0.5f, $"{seconds:0.00} s");
        yield return Wait(0.6f);
        var start = grid.CellCenter(grid.PlayerStart.x, grid.PlayerStart.y);
        float fromStart = Vector2.Distance(new Vector2(wizard.transform.position.x, wizard.transform.position.z), new Vector2(start.x, start.z));
        Check("back_at_the_yard", fromStart < 2f && (fellAt - start).magnitude > 3f, $"{fromStart:0.0} m from the start, fell {(fellAt - start).magnitude:0.0} m from it");
        Check("full_health", vitals.Health >= vitals.HealthMax, $"health {vitals.Health}");
        Check("controls_back", controls.enabled, $"controls {controls.enabled}");
        Check("gets_up", !pose.Lying && pose.Weight <= 0f, $"weight {pose.Weight:0.00}");
        Shot("back");
    }

    IEnumerator Press(GamepadButton button)
    {
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return null;
        yield return null;
    }
}
