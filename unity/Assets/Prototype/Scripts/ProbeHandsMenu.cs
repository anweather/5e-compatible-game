using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's handsmenu scenario (pad-hands-menu): the pad's hands menu on a virtual pad. Holding the d-pad's down shows the two
// hands and stills the wizard; each stick points its hand at an element; letting go attunes both, the second once the first is
// done; B lets go without. The hands' icons stay off the HUD until a hand is at work, then a trigger casts. In a Hold run the menu
// gives nothing away from the towers and the tower's element at one, which the trigger then casts. With --shots it films the clip
// (frames, sound.wav, timeline.txt) of the free-roam part: open, pick, let go, cast. Nothing here is in the game.
public class HandsMenuProbe : ProbeScenario
{
    const float MostWait = 8f;

    Gamepad pad;
    ClipFilm film;
    readonly List<string> timeline = new();
    readonly FiredCastsHeard heard = new();

    World Core => grid.World;
    string Me => caster.CastPlayer;
    IReadOnlyList<string> Elements => Core.Rules.Combos.Elements;
    string Holds(int hand) => Core.HandsOf(Me).Holds[hand];

    void Note(string what) => timeline.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.000} {1}", film != null ? film.Seconds : 0f, what));

    void Pad(bool dpadDown, Vector2 left = default, Vector2 right = default, float leftTrigger = 0f, float rightTrigger = 0f, GamepadButton? also = null)
    {
        var state = new GamepadState { leftStick = left, rightStick = right, leftTrigger = leftTrigger, rightTrigger = rightTrigger };
        if (dpadDown) state = state.WithButton(GamepadButton.DpadDown);
        if (also.HasValue) state = state.WithButton(also.Value);
        InputSystem.QueueStateEvent(pad, state);
    }

    Vector2 Toward(string element)
    {
        var (x, y) = HandsPick.Direction(Elements.ToList().IndexOf(element), Elements.Count);
        return new Vector2(x, y);
    }

    // The stick leaning out to an element over a moment, as a thumb does, the d-pad's down held.
    IEnumerator Lean(int hand, string element, Vector2 other)
    {
        var to = Toward(element);
        for (float t = 0f; t < 0.35f; t += Time.deltaTime)
        {
            var now = to * Mathf.Clamp01(t / 0.35f);
            Pad(true, hand == HandsDef.Left ? now : other, hand == HandsDef.Left ? other : now);
            yield return null;
        }
        Pad(true, hand == HandsDef.Left ? to : other, hand == HandsDef.Left ? other : to);
        yield return null;
    }

    protected override IEnumerator Run()
    {
        var controls = caster.controls;
        bool controlsWereOn = controls.enabled, casterWasOn = caster.enabled, hudWas = EventLog.HudVisible;
        controls.enabled = true;
        caster.enabled = true;
        EventLog.HudVisible = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        pad = InputSystem.AddDevice<Gamepad>("Hands menu pad");
        try
        {
            Place(8, 12);
            var aim = Yard(14, 12);
            aim.y = grid.GroundAt(aim);
            caster.AimOverride = aim;
            wizard.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(aim - wizard.transform.position, Vector3.up));
            var t = wizard.transform;
            Cut(t.position - t.forward * 4.6f + t.right * 1.2f + Vector3.up * 2.4f, t.position + t.forward * 4f + Vector3.up * 0.9f);
            yield return Wait(0.8f);
            Check("the_hands_are_on", controls.HandPad);
            if (!controls.HandPad) yield break;
            var hud = EventLog.CastShown;
            Check("no_hand_icons_at_rest", hud != null && !hud.HandDrawn(HandsDef.Left) && !hud.HandDrawn(HandsDef.Right));
            heard.StartFrom(Core);

            if (!string.IsNullOrEmpty(ScreenshotDir)) film = new ClipFilm(this, ScreenshotDir, withHud: true);
            film?.Roll();
            yield return Wait(0.8f);
            yield return PickBoth();
            yield return Cast(hud);
            yield return Cancel();
            if (film != null)
            {
                yield return film.Stop();
                File.WriteAllLines(Path.Combine(ScreenshotDir, "timeline.txt"), timeline);
                Metric("seconds", film.Seconds);
                Check("short_enough", film.Seconds <= 30f, $"{film.Seconds:0.0} s");
                film = null;
            }
            caster.AimOverride = null;
            yield return InAHoldRun();
        }
        finally
        {
            HandsMenu.Close();
            InputSystem.RemoveDevice(pad);
            caster.AimOverride = null;
            controls.enabled = controlsWereOn;
            caster.enabled = casterWasOn;
            EventLog.HudVisible = hudWas;
        }
    }

    // Down held, each stick to a new element, let go: the left attunes, then the right.
    IEnumerator PickBoth()
    {
        string left = Elements.First(e => e != Holds(HandsDef.Left) && Core.ElementOpen(Me, e));
        string right = Elements.First(e => e != Holds(HandsDef.Right) && e != left && Core.ElementOpen(Me, e));
        var input = caster.GetComponent<PlayerInput>();
        var stood = wizard.transform.position;
        Note("d-pad down held: the hands menu");
        Pad(true);
        yield return Wait(0.5f);
        Check("dpad_down_opens_the_hands_menu", HandsMenu.IsOpen && !SpellListMenu.Open);
        Check("the_wizard_stands_while_its_open", input == null || !input.enabled);
        Note($"left stick: {left}");
        yield return Lean(HandsDef.Left, left, Vector2.zero);
        yield return Wait(0.5f);
        Note($"right stick: {right}");
        yield return Lean(HandsDef.Right, right, Toward(left));
        yield return Wait(0.7f);
        Check("each_stick_points_its_hand", HandsMenu.Pick(HandsDef.Left) == left && HandsMenu.Pick(HandsDef.Right) == right,
            $"{HandsMenu.Pick(HandsDef.Left)} {HandsMenu.Pick(HandsDef.Right)}");
        Shot("menu");
        Check("the_wizard_stayed_put", Vector3.Distance(stood, wizard.transform.position) < 0.3f, $"{Vector3.Distance(stood, wizard.transform.position):0.00} m");
        Note("let go: both hands attune");
        Pad(false);
        yield return null;
        yield return null;
        Check("letting_go_closes_it", !HandsMenu.IsOpen);
        Check("the_wizard_moves_again", input == null || input.enabled);
        for (float s = 0f; s < MostWait && (Holds(HandsDef.Left) != left || Holds(HandsDef.Right) != right); s += Time.deltaTime) yield return null;
        Check("both_hands_attune", Holds(HandsDef.Left) == left && Holds(HandsDef.Right) == right, $"{Holds(HandsDef.Left)} {Holds(HandsDef.Right)}");
        Check("none_left_waiting", !HandsMenu.Waiting);
        yield return Wait(0.6f);
    }

    // The right trigger held: its icon shows, small, the other stays off; let go, it fires.
    IEnumerator Cast(CastHud hud)
    {
        heard.Take(Core);
        Note($"right trigger: {Holds(HandsDef.Right)}");
        Pad(false, rightTrigger: 1f);
        yield return Wait(0.9f);
        Check("a_held_hand_shows_its_icon", hud != null && hud.HandDrawn(HandsDef.Right) && !hud.HandDrawn(HandsDef.Left));
        Shot("charging");
        Pad(false);
        bool fired = false;
        for (float s = 0f; s < MostWait && !fired; s += Time.deltaTime)
        {
            fired = heard.Take(Core).Any(f => f.Player == Me);
            yield return null;
        }
        Check("then_it_casts", fired);
        yield return Wait(1.6f);
        Note($"left trigger: {Holds(HandsDef.Left)}");
        Pad(false, leftTrigger: 1f);
        yield return Wait(0.2f);
        Pad(false);
        yield return Wait(1.6f);
    }

    // B with the menu open lets go without attuning.
    IEnumerator Cancel()
    {
        string was = Holds(HandsDef.Left);
        string other = Elements.First(e => e != was && Core.ElementOpen(Me, e));
        Pad(true);
        yield return Wait(0.3f);
        yield return Lean(HandsDef.Left, other, Vector2.zero);
        Pad(true, Toward(other), Vector2.zero, also: GamepadButton.East);
        yield return null;
        yield return null;
        Check("b_shuts_it", !HandsMenu.IsOpen);
        Pad(false);
        yield return Wait(1f);
        Check("without_attuning", Holds(HandsDef.Left) == was, Holds(HandsDef.Left));
    }

    // A run: away from the towers nothing can be picked; at one, its element, and the trigger casts it.
    IEnumerator InAHoldRun()
    {
        var site = Core.HoldSite;
        Check("a_hold", site != null);
        if (site == null) yield break;
        StandOn(site.Plate);
        for (float s = 0f; s < MostWait && !Core.HoldRunning; s += Time.deltaTime) yield return null;
        Check("a_run_starts", Core.HoldRunning);
        if (!Core.HoldRunning) yield break;
        Core.Hold.Health = int.MaxValue / 2;
        Core.Hold.ToSpawn = 0;
        StandOn(site.Rise);
        yield return Wait(1f);
        string was = Holds(HandsDef.Left);
        string other = Elements.First(e => e != was);
        Pad(true);
        yield return Wait(0.3f);
        yield return Lean(HandsDef.Left, other, Vector2.zero);
        Check("away_from_the_towers_nothing_picks", HandsMenu.IsOpen && HandsMenu.Pick(HandsDef.Left) == null, HandsMenu.Pick(HandsDef.Left));
        Pad(false);
        yield return Wait(1f);
        Check("nor_attunes", Holds(HandsDef.Left) == was, Holds(HandsDef.Left));

        var tower = site.Towers.FirstOrDefault(t => t.Element != Holds(HandsDef.Left) && Core.ElementOpen(Me, t.Element));
        Check("a_tower_to_try", tower != null);
        if (tower == null) yield break;
        StandOn(Core.TowerGround(tower)[0]);
        yield return Wait(1f);
        Check("at_the_tower", Core.TowerOf(Me) == tower);
        string wrong = Elements.First(e => e != tower.Element && e != Holds(HandsDef.Left));
        Pad(true);
        yield return Wait(0.3f);
        yield return Lean(HandsDef.Left, wrong, Vector2.zero);
        Check("not_another_towers_element", HandsMenu.Pick(HandsDef.Left) == null, HandsMenu.Pick(HandsDef.Left));
        yield return Lean(HandsDef.Left, tower.Element, Vector2.zero);
        Check("the_towers_element_picks", HandsMenu.Pick(HandsDef.Left) == tower.Element, HandsMenu.Pick(HandsDef.Left));
        Shot("at-a-tower");
        Pad(false);
        for (float s = 0f; s < MostWait && Holds(HandsDef.Left) != tower.Element; s += Time.deltaTime) yield return null;
        Check("it_attunes_at_the_tower", Holds(HandsDef.Left) == tower.Element, Holds(HandsDef.Left));
        for (float s = 0f; s < MostWait && Core.HandsOf(Me).Busy; s += Time.deltaTime) yield return null;
        heard.Take(Core);
        Pad(false, leftTrigger: 1f);
        yield return Wait(0.2f);
        Pad(false);
        bool fired = false;
        for (float s = 0f; s < MostWait && !fired; s += Time.deltaTime)
        {
            fired = heard.Take(Core).Any(f => f.Player == Me);
            yield return null;
        }
        Check("and_the_trigger_casts_it_in_the_run", fired);
    }
}
