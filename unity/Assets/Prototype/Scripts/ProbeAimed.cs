using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's aimed scenario (D16, unified-casting.md; the 2026-11-06 playtest): in a Hold run on the castle's north wall, with
// the hands on a virtual pad, Lightning (fire + air, both bumpers) at a flyer the camera looks up at, a little off the
// crosshair: first with the aim helpers off, as the game was (it misses), then on (it locks, pitches up and hits). Then Gust of
// Wind (air, the right bumper) at a raider on the ground below, which knocks it back, and a Whirlwind on another, which lifts it
// and flings it away. With --shots, the before and after pictures; aimedclip films the same takes. Nothing here is in the game.
public class AimedProbe : ProbeScenario
{
    const int WallX = 21, WallY = 3;
    const float MostWait = 8f, OffAim = 6f;

    Gamepad pad;
    BuildingRow row;
    readonly FiredCastsHeard heard = new();
    protected ClipFilm film;

    World Core => grid.World;
    string Me => caster.CastPlayer;

    /// <summary>Whether this run films its takes (aimedclip).</summary>
    protected virtual bool Filming => false;

    void Stand(Vector2Int cell)
    {
        var p = grid.CellCenter(cell.x, cell.y);
        p.y = grid.GroundAt(p);
        Teleport(p + Vector3.up * 0.05f);
    }

    void Pad(float left, float right, params GamepadButton[] held)
    {
        var state = new GamepadState { leftTrigger = left, rightTrigger = right };
        foreach (var b in held) state = state.WithButton(b);
        InputSystem.QueueStateEvent(pad, state);
    }

    void Let() => InputSystem.QueueStateEvent(pad, new GamepadState());

    // From the spell list before the run (the keep's room went, hold-batch): a moment each.
    IEnumerator AttuneTo(string element, int hand)
    {
        caster.AttuneTo(hand, element);
        for (float t = 0f; t < MostWait && (Core.HandsOf(Me).Holds[hand] != element || Core.HandsOf(Me).AttuneLeft > 0); t += Time.deltaTime) yield return null;
        Check($"attuned_{hand}_to_{element}", Core.HandsOf(Me).Holds[hand] == element, Core.HandsOf(Me).Holds[hand]);
    }

    // A tap of these buttons, and the spell it set off (null if none did in time), the aim kept on the raider as a player
    // keeps it while the spell goes off.
    IEnumerator Tap(System.Action press, string[] fired, Raider on, float off)
    {
        heard.StartFrom(Core);
        press();
        yield return null;
        yield return null;
        yield return null;
        Let();
        for (float t = 0f; t < MostWait && fired[0] == null; t += Time.deltaTime)
        {
            if (on.Alive) LookAt(on, off);
            foreach (var f in heard.Take(Core))
                if (f.Player == Me) fired[0] = f.Spell ?? f.Act;
            yield return null;
        }
    }

    IEnumerator Cooled()
    {
        for (float t = 0f; t < MostWait && (Core.HandsOf(Me).Casting || Core.HandCooling(Me, HandsDef.Left) > 0 || Core.HandCooling(Me, HandsDef.Right) > 0); t += Time.deltaTime) yield return null;
        yield return Wait(0.2f);
    }

    // Where a raider is drawn: up in the air for a flyer.
    Vector3 Drawn(Raider r)
    {
        var c = grid.CellCenter(r.X, r.Y);
        return new Vector3(c.x, grid.GroundAt(c) + (Core.Flying(r) ? DefenseField.FlyHeight : 1f), c.z);
    }

    // The camera over the wizard's shoulder, looking at a raider; the aim a few degrees to its side, as a player's thumb leaves it.
    void LookAt(Raider r, float off)
    {
        var at = Drawn(r);
        var t = wizard.transform;
        var flat = Vector3.ProjectOnPlane(at - t.position, Vector3.up).normalized;
        t.rotation = Quaternion.LookRotation(flat);
        // A flyer from up behind the wall; one on the ground from out in the field beyond it, looking back at the wall.
        var from = Core.Flying(r) ? t.position + new Vector3(3f, 15f, -11f) : at + flat * 9f + Vector3.up * 5f + t.right * 2f;
        Cut(from, Vector3.Lerp(t.position + Vector3.up * 1.5f, at, Core.Flying(r) ? 0.55f : 0.7f));
        caster.AimRayOverride = new Ray(from, Quaternion.AngleAxis(off, Vector3.up) * (at - from).normalized);
    }

    // A raider of a kind the run's waves won't crowd, where it's asked for (or the nearest open cell north of there).
    Raider Put(string kind, Vector2Int at, int hp)
    {
        for (int k = 0; k < 6; k++)
            if (Core.SpawnRaider(kind, at.x, at.y - k, hp) is { } r) return r;
        return null;
    }

    protected override IEnumerator Run()
    {
        var site = Core.HoldSite;
        Check("the_map_has_a_hold", site != null);
        if (site == null) yield break;
        if (Filming && string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "aimedclip films into --shots <folder>");
            yield break;
        }
        row = Core.BuildingRowOf(site.Castle);
        var controls = caster.controls;
        controls.enabled = true;
        caster.enabled = true;
        EventLog.HudVisible = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        pad = InputSystem.AddDevice<Gamepad>("Aimed pad");
        try
        {
            yield return Setup(site);
            if (!Core.HoldRunning) yield break;
            if (Filming)
            {
                film = new ClipFilm(this, ScreenshotDir, withHud: true);
                film.Roll();
            }
            yield return Lightning();
            yield return Gust();
            yield return Whirlwind();
            if (film != null)
            {
                yield return film.Stop();
                Metric("seconds", film.Seconds);
                Check("short_enough", film.Seconds <= 30f, $"{film.Seconds:0.0} s");
            }
        }
        finally
        {
            SpellCaster.AimHelp = true;
            caster.AimRayOverride = null;
            InputSystem.RemoveDevice(pad);
            caster.enabled = false;
        }
    }

    IEnumerator Setup(WorldHold site)
    {
        yield return AttuneTo("fire", HandsDef.Left);
        yield return AttuneTo("air", HandsDef.Right);
        Stand(new Vector2Int(site.Plate.x, site.Plate.y));
        for (float t = 0f; t < MostWait && !Core.HoldRunning; t += Time.deltaTime) yield return null;
        Check("a_run_starts", Core.HoldRunning);
        if (!Core.HoldRunning) yield break;
        Core.Hold.Health = int.MaxValue / 2; // the run outlasts the probe
        Stand(new Vector2Int(row.X + WallX, row.Y + WallY));
        yield return Wait(0.5f);
    }

    // Lightning at a flyer up in the air, a little off the crosshair: as the game was, then with lock-on and pitch.
    IEnumerator Lightning()
    {
        var me = grid.CellAt(wizard.transform.position);
        // Far out north, so it's still out there, in front of the wall, through both casts.
        var flyer = Put(RaiderIds.AshWing, new Vector2Int(me.x + 1, me.y - 17), 400);
        Check("a_flyer_comes", flyer != null && Core.Flying(flyer));
        if (flyer == null) yield break;
        yield return Wait(0.4f);
        string bolt = Core.Rules.Sim.Hold.Hands.Both("fire", "air", HandsDef.Throw)?.Spell;
        Check("both_bumpers_are_lightning", bolt == SpellIds.LightningBolt, bolt);

        SpellCaster.AimHelp = false;
        LookAt(flyer, OffAim);
        int before = flyer.Hp;
        var fired = new string[1];
        yield return Tap(() => Pad(0f, 0f, GamepadButton.LeftShoulder, GamepadButton.RightShoulder), fired, flyer, OffAim);
        yield return Wait(0.15f);
        Shot("lightning-before");
        yield return Wait(0.8f);
        Metric("before_flyer_hurt", before - flyer.Hp);
        Check("before_it_missed", flyer.Alive && flyer.Hp == before, $"{before - flyer.Hp} hurt");
        yield return Cooled();

        SpellCaster.AimHelp = true;
        LookAt(flyer, OffAim);
        before = flyer.Hp;
        fired[0] = null;
        yield return Tap(() => Pad(0f, 0f, GamepadButton.LeftShoulder, GamepadButton.RightShoulder), fired, flyer, OffAim);
        Check("lightning_fired", fired[0] == SpellIds.LightningBolt, fired[0]);
        var cast = caster.LastHandCast;
        var line = Core.SpellCells(Core.Rules.Spell(SpellIds.LightningBolt), cast.X, cast.Y, cast.Dir, cast.Charge, cast.AimX, cast.AimY);
        Check("it_locked_onto_the_flyer", caster.LastLocked == flyer, $"{caster.LastLocked?.Kind}, cast at ({cast.X}, {cast.Y}) aim ({cast.AimX}, {cast.AimY}), {line.Count} cells to {(line.Count > 0 ? line[line.Count - 1].ToString() : "-")}");
        yield return Wait(0.15f);
        Shot("lightning-after");
        yield return Wait(0.8f);
        Metric("after_flyer_hurt", before - flyer.Hp);
        Check("lightning_hits_the_flyer", !flyer.Alive || flyer.Hp < before, $"{before - flyer.Hp} hurt, {(flyer.Alive ? "alive" : "dead")}");
        yield return Cooled();
    }

    // Gust of Wind at a raider on the ground below the wall: it's knocked back, away from the wizard.
    IEnumerator Gust()
    {
        var me = grid.CellAt(wizard.transform.position);
        var hound = Put(RaiderIds.BoneHound, new Vector2Int(me.x, me.y - 5), 400); // straight out from the wall: the trees off to the west stop a line
        Check("a_raider_comes", hound != null);
        if (hound == null) yield break;
        hound.Wait = 60; // it stands a while, on its clear line (walking, it goes in among the trees, which stop a line)
        yield return Wait(0.4f);
        LookAt(hound, OffAim * 0.5f);
        int from = me.y - hound.Y;
        var fired = new string[1];
        yield return Tap(() => Pad(0f, 0f, GamepadButton.RightShoulder), fired, hound, OffAim * 0.5f);
        Check("gust_fired", fired[0] == SpellIds.GustOfWind, fired[0]);
        var gust = caster.LastHandCast;
        var cells = Core.SpellCells(Core.Rules.Spell(SpellIds.GustOfWind), gust.X, gust.Y, gust.Dir, gust.Charge, gust.AimX, gust.AimY);
        string Around(int x, int y) { var c = Core.At(x, y); return c == null ? "-" : $"{c.T}{(Core.IsBlocking(c) ? "#" : "")}"; }
        Check("cells_round", true, string.Join(" ", Enumerable.Range(-6, 8).Select(dy => $"y{me.y + dy}:" + string.Join(",", Enumerable.Range(-3, 5).Select(dx => Around(me.x + dx, me.y + dy))))));
        Check("it_locked_onto_the_raider", caster.LastLocked == hound,
            $"{caster.LastLocked?.Kind}; cast at ({gust.X}, {gust.Y}) way {gust.Dir} aim ({gust.AimX}, {gust.AimY}), {cells.Count} cells to {(cells.Count > 0 ? cells[cells.Count - 1].ToString() : "-")}; the raider at ({hound.X}, {hound.Y}), me {me}");
        yield return Wait(0.6f);
        Shot("gust-after");
        int to = me.y - hound.Y;
        Metric("gust_cells_back", to - from);
        Check("gust_knocks_it_back", to - from >= 3 && hound.StatusOf(World.ShovedStatus) == 1, $"{to - from} cells back, shoved {hound.StatusOf(World.ShovedStatus)}");
        yield return Wait(1.2f);
        yield return Cooled();
    }

    // A Whirlwind on a raider: it's lifted off its feet in the eye, then flung out and hurt.
    IEnumerator Whirlwind()
    {
        var me = grid.CellAt(wizard.transform.position);
        var crab = Put(RaiderIds.BoneHound, new Vector2Int(me.x + 3, me.y - 8), 400);
        Check("another_raider_comes", crab != null);
        if (crab == null) yield break;
        yield return Wait(0.3f);
        LookAt(crab, 0f);
        var start = new Vector2Int(crab.X, crab.Y);
        int hp = crab.Hp;
        caster.Cast(SpellIds.Whirlwind, crab.X, crab.Y, 0);
        bool lifted = false, shot = false;
        int most = 0;
        for (float t = 0f; t < 6f; t += Time.deltaTime)
        {
            if (crab.StatusOf(World.LiftedStatus) > 0)
            {
                lifted = true;
                if (!shot && crab.StatusOf(World.LiftedStatus) < 6)
                {
                    Shot("whirlwind-lifted");
                    shot = true;
                }
            }
            most = Mathf.Max(most, Mathf.RoundToInt(Vector2Int.Distance(start, new Vector2Int(crab.X, crab.Y))));
            yield return null;
        }
        Metric("whirlwind_flung_cells", most);
        Check("whirlwind_lifts_it", lifted);
        Check("whirlwind_flings_it", most >= 4 && crab.Hp < hp, $"{most} cells, {hp - crab.Hp} hurt");
    }
}
