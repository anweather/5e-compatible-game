using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

// The probe's bigspells scenario (casting-pace-options.md, "A Meteor for every element"; J-078): in a Hold run on the castle's
// north wall, the hands on a virtual pad, each of the four big spells cast from its hand pair at a raider out in front: Thunder
// Strike (fire + air, both triggers) marks it and strikes it, Tornado (air + air, both bumpers) lifts it and flings it, Earth
// Spires (earth + earth, both bumpers) throws it up and leaves its spires standing, Iceberg (water + water, both bumpers) crushes
// it and leaves walkable ice. Each merges on screen as its own spell before it goes off. With --shots, a picture of each;
// bigspellsclip films the lot. Nothing here is in the game.
public class BigSpellsProbe : AimedProbe
{
    protected override IEnumerator Run()
    {
        var site = Core.HoldSite;
        Check("the_map_has_a_hold", site != null);
        if (site == null) yield break;
        if (Filming && string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "bigspellsclip films into --shots <folder>");
            yield break;
        }
        row = Core.BuildingRowOf(site.Castle);
        caster.controls.enabled = true;
        caster.enabled = true;
        EventLog.HudVisible = true;
        UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
        pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("Big spells pad");
        try
        {
            yield return Setup(site);
            if (!Core.HoldRunning) yield break;
            if (Filming)
            {
                film = new ClipFilm(this, ScreenshotDir, withHud: true);
                film.Roll();
            }
            yield return ThunderStrike();
            yield return Tornado();
            yield return Spires();
            yield return Iceberg();
            if (film != null)
            {
                yield return film.Stop();
                Metric("seconds", film.Seconds);
                Check("short_enough", film.Seconds <= 30f, $"{film.Seconds:0.0} s");
            }
        }
        finally
        {
            caster.AimRayOverride = null;
            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
            caster.enabled = false;
        }
    }

    // Both hands' buttons for a gesture: the triggers for a thrust, the bumpers for a throw.
    void Both(int gesture, bool down)
    {
        if (!down) Let();
        else if (gesture == HandsDef.Thrust) Pad(1f, 1f);
        else Pad(0f, 0f, GamepadButton.LeftShoulder, GamepadButton.RightShoulder);
    }

    // The pair held a moment, merged on screen as the spell, then let go to fire it at the raider; what it fired, or null.
    IEnumerator Cast(string left, string right, int gesture, string spell, Raider at, string[] fired)
    {
        yield return Rested();
        yield return AtTowers(left, right);
        string pair = Core.Rules.Sim.Hold.Hands.Both(left, right, gesture)?.Spell;
        Check($"{spell}_is_{left}_{right}", pair == spell, pair);
        LookAt(at, 0f);
        Both(gesture, true);
        var hud = EventLog.CastShown;
        string merged = null;
        for (float t = 0f; t < 1.2f && !(hud != null && hud.MergeShown && merged != null); t += Time.deltaTime)
        {
            if (at.Alive) LookAt(at, 0f);
            merged = Core.CastOf(Me)?.Merge;
            yield return null;
        }
        Check($"{spell}_merges_on_screen", hud != null && hud.MergeShown && merged == spell, $"{merged}, shown {hud?.MergeShown}");
        Check($"{spell}_has_a_name", !string.IsNullOrEmpty(Core.Rules.Spell(spell)?.Name) && Core.Rules.Spell(spell).Name != spell, Core.Rules.Spell(spell)?.Name);
        yield return Tap(() => Both(gesture, false), fired, at, 0f);
        Check($"{spell}_fired", fired[0] == spell, fired[0]);
    }

    // A big spell's rest is 90 ticks (15 s), longer than Cooled waits; the wait is cut from the clip.
    IEnumerator Rested()
    {
        if (film != null) film.Keeping = false;
        for (float t = 0f; t < RestWait && (Core.HandsOf(Me).Casting || Core.HandCooling(Me, HandsDef.Left) > 0 || Core.HandCooling(Me, HandsDef.Right) > 0); t += Time.deltaTime)
            yield return null;
        Check("hands_rested", Core.HandCooling(Me, HandsDef.Left) <= 0 && Core.HandCooling(Me, HandsDef.Right) <= 0);
        yield return Wait(0.2f);
        if (film != null) film.Keeping = true;
    }

    const float RestWait = 20f;

    // Blocking cells in the box from the wizard out to a raider, two cells either side.
    int BlockingTo(Vector2Int me, Raider r)
    {
        int n = 0;
        for (int y = Mathf.Min(me.y, r.Y) - 2; y <= Mathf.Max(me.y, r.Y) + 2; y++)
            for (int x = Mathf.Min(me.x, r.X) - 2; x <= Mathf.Max(me.x, r.X) + 2; x++)
                if (Core.At(x, y) is { } c && Core.IsBlocking(c)) n++;
        return n;
    }

    // In a run only a tower attunes (World.Attune): each hand that wants another element is attuned at that element's tower, a
    // hand at a time as a player does it, and the wizard is back on the wall after.
    IEnumerator AtTowers(string left, string right)
    {
        var wall = Me2;
        bool moved = false;
        foreach (var (hand, element) in new[] { (HandsDef.Left, left), (HandsDef.Right, right) })
        {
            if (Core.HandsOf(Me).Holds[hand] == element) continue;
            var tower = Core.HoldSite.Towers.FirstOrDefault(t => t.Element == element);
            Check($"a_{element}_tower", tower != null);
            if (tower == null) yield break;
            var (x, y) = Core.TowerGround(tower)[0];
            if (film != null) film.Keeping = false; // the walk to the towers is cut from the clip
            Stand(new Vector2Int(x, y));
            moved = true;
            for (float t = 0f; t < MostWait && Core.TowerOf(Me) != tower; t += Time.deltaTime) yield return null;
            yield return AttuneTo(element, hand);
        }
        if (!moved) yield break;
        Stand(wall);
        yield return Wait(0.3f);
        if (film != null) film.Keeping = true;
    }

    Raider Still(Vector2Int at)
    {
        var r = Put(RaiderIds.BoneHound, at, 400);
        if (r != null) r.Wait = 120; // it stands, so the spell meets it where it was aimed
        return r;
    }

    Vector2Int Me2 => grid.CellAt(wizard.transform.position);

    // Fire and air on the triggers: the mark, then the column of lightning and the thunder after.
    IEnumerator ThunderStrike()
    {
        var me = Me2;
        var r = Still(new Vector2Int(me.x - 2, me.y - 9));
        Check("thunder_raider_comes", r != null);
        if (r == null) yield break;
        int hp = r.Hp;
        var fired = new string[1];
        yield return Cast(ElementIds.Fire, ElementIds.Air, HandsDef.Thrust, SpellIds.ThunderStrike, r, fired);
        bool marked = false, bolted = false;
        for (float t = 0f; t < 4f && r.Hp == hp && r.Alive; t += Time.deltaTime)
        {
            marked |= Core.Stars.Any(s => s.Spell == SpellIds.ThunderStrike);
            bolted |= ArcFx.Playing > 0;
            yield return null;
        }
        yield return Wait(0.1f);
        bolted |= ArcFx.Playing > 0;
        Shot("thunder-strike");
        Metric("thunder_hurt", hp - r.Hp);
        Check("thunder_marks_first", marked);
        Check("thunder_bolts_from_the_sky", bolted, $"{ArcFx.Playing} arcs");
        Check("thunder_strikes_it", !r.Alive || r.Hp < hp, $"{hp - r.Hp} hurt");
        yield return Wait(1f);
    }

    // Air and air on the bumpers: the funnel lifts the raider in its eye and flings it.
    IEnumerator Tornado()
    {
        var me = Me2;
        var r = Still(new Vector2Int(me.x + 2, me.y - 9));
        Check("tornado_raider_comes", r != null);
        if (r == null) yield break;
        var start = new Vector2Int(r.X, r.Y);
        int hp = r.Hp;
        var fired = new string[1];
        yield return Cast(ElementIds.Air, ElementIds.Air, HandsDef.Throw, SpellIds.Tornado, r, fired);
        bool lifted = false, shot = false;
        int most = 0, radius = 0;
        for (float t = 0f; t < 7f; t += Time.deltaTime)
        {
            foreach (var w in Core.Whirls)
                if (w.Spell == SpellIds.Tornado) radius = Mathf.Max(radius, w.Radius);
            if (r.StatusOf(World.LiftedStatus) > 0)
            {
                lifted = true;
                if (!shot && r.StatusOf(World.LiftedStatus) < 6)
                {
                    Shot("tornado-lifted");
                    shot = true;
                }
            }
            most = Mathf.Max(most, Mathf.RoundToInt(Vector2Int.Distance(start, new Vector2Int(r.X, r.Y))));
            yield return null;
        }
        Metric("tornado_flung_cells", most);
        Check("tornado_whirls_big", radius >= 3, $"radius {radius}");
        Check("tornado_lifts_it", lifted);
        Check("tornado_flings_it", most >= 6 && r.Hp < hp, $"{most} cells, {hp - r.Hp} hurt");
    }

    // Earth and earth on the bumpers: the zigzag of spires from the wall out to the raider, which is thrown up.
    IEnumerator Spires()
    {
        var me = Me2;
        var r = Still(new Vector2Int(me.x, me.y - 5)); // on the line: the zigzag steps off it at its second cell and every other after
        Check("spires_raider_comes", r != null);
        if (r == null) yield break;
        int hp = r.Hp, before = BlockingTo(me, r);
        var fired = new string[1];
        yield return Cast(ElementIds.Earth, ElementIds.Earth, HandsDef.Throw, SpellIds.EarthSpires, r, fired);
        bool thrown = false;
        for (float t = 0f; t < 2f; t += Time.deltaTime)
        {
            thrown |= r.StatusOf(World.ThrownStatus) > 0;
            yield return null;
        }
        Shot("earth-spires");
        int standing = BlockingTo(me, r) - before;
        Metric("spires_standing", standing);
        Check("spires_hit_it", thrown || r.Hp < hp, $"thrown {thrown}, {hp - r.Hp} hurt");
        Check("spires_stand", standing >= 4, $"{standing} more blocking cells out to the raider");
    }

    // Water and water on the bumpers: the wave of ice rolls out, crushes the raider and leaves ice to walk on.
    IEnumerator Iceberg()
    {
        var me = Me2;
        var r = Still(new Vector2Int(me.x + 4, me.y - 7));
        Check("iceberg_raider_comes", r != null);
        if (r == null) yield break;
        int hp = r.Hp;
        var from = new Vector2Int(r.X, r.Y);
        int iceBefore = IceRound(from);
        var fired = new string[1];
        yield return Cast(ElementIds.Water, ElementIds.Water, HandsDef.Throw, SpellIds.Iceberg, r, fired);
        int drawn = 0;
        for (float t = 0f; t < 4f; t += Time.deltaTime)
        {
            drawn = Mathf.Max(drawn, grid.MoversShown);
            yield return null;
        }
        Shot("iceberg");
        int ice = IceRound(from) - iceBefore;
        Metric("iceberg_ice_cells", ice);
        Check("iceberg_rolls_on_screen", drawn > 0, $"{drawn} movers");
        Check("iceberg_crushes_it", !r.Alive || r.Hp < hp, $"{hp - r.Hp} hurt");
        Check("iceberg_leaves_walkable_ice", ice >= 5, $"{ice} more cells of ice round where it was cast");
    }

    // Frozen cells a wizard can walk on, within the wave's reach of where it was cast.
    int IceRound(Vector2Int at)
    {
        const int Reach = 10;
        int n = 0;
        for (int y = at.y - Reach; y <= at.y + Reach; y++)
            for (int x = at.x - Reach; x <= at.x + Reach; x++)
                if (Core.At(x, y) is { } c && c.Frozen && !Core.IsBlocking(c)) n++;
        return n;
    }
}
