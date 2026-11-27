using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

// The probe's bigspells scenario (casting-pace-options.md, "A Meteor for every element"; J-078): in a Hold run on the castle's
// north wall, the hands on a virtual pad, each of the four big spells cast from its hand pair at a raider out in front: Thunder
// Strike (fire + air, both triggers) marks it and strikes it, Tornado (air + air, both bumpers) lifts it and flings it, Earth
// Spires (earth + earth, both bumpers) throws it up and leaves its spires standing, Iceberg (water + water, both bumpers) crushes
// it and leaves walkable ice. The looks too: the thunderclap heard with the bolt, the spires bursting up and a raider riding
// one up, the pack ice the Iceberg leaves. Each merges on screen as its own spell before it goes off. With --shots, a picture of each;
// bigspellsclip films all but the Tornado. Nothing here is in the game.
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
            if (!Filming) yield return Tornado();
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

    void Both(int gesture, bool down)
    {
        if (!down) Let();
        else if (gesture == HandsDef.Thrust) Pad(1f, 1f);
        else Pad(0f, 0f, GamepadButton.LeftShoulder, GamepadButton.RightShoulder);
    }

    IEnumerator Cast(string left, string right, int gesture, string spell, Raider at, string[] fired)
    {
        yield return Rested();
        yield return AtTowers(left, right);
        string pair = Core.Rules.Sim.Hold.Hands.Both(left, right, gesture)?.Spell;
        Check($"{spell}_is_{left}_{right}", pair == spell, pair);
        LookAt(at, 0f);
        Both(gesture, true);
        var hud = WizardHud.CastShown;
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

    int BlockingTo(Vector2Int me, Raider r)
    {
        int n = 0;
        for (int y = Mathf.Min(me.y, r.Y) - 2; y <= Mathf.Max(me.y, r.Y) + 2; y++)
            for (int x = Mathf.Min(me.x, r.X) - 2; x <= Mathf.Max(me.x, r.X) + 2; x++)
                if (Core.At(x, y) is { } c && Core.IsBlocking(c)) n++;
        return n;
    }

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
            if (film != null) film.Keeping = false;
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
        if (r != null) r.Wait = 120;
        return r;
    }

    Vector2Int Me2 => grid.CellAt(wizard.transform.position);

    IEnumerator ThunderStrike()
    {
        var me = Me2;
        var r = Still(new Vector2Int(me.x - 2, me.y - 9));
        Check("thunder_raider_comes", r != null);
        if (r == null) yield break;
        int hp = r.Hp;
        var fired = new string[1];
        bool clapped = false;
        void Heard(string sound, Vector3 at) => clapped |= sound == SoundIds.ThunderClap;
        GameSounds.Played += Heard;
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
        for (float t = 0f; t < 2.5f; t += Time.deltaTime) yield return null;
        GameSounds.Played -= Heard;
        Check("thunder_claps", clapped, "a thunderclap with the bolt");
    }

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

    IEnumerator Spires()
    {
        var me = Me2;
        var r = Still(new Vector2Int(me.x, me.y - 5));
        var far = Still(new Vector2Int(me.x, me.y - 9));
        Check("spires_raider_comes", r != null);
        if (r == null) yield break;
        int hp = r.Hp, before = BlockingTo(me, r);
        var fired = new string[1];
        yield return Cast(ElementIds.Earth, ElementIds.Earth, HandsDef.Throw, SpellIds.EarthSpires, r, fired);
        bool thrown = false, shot = false;
        float rode = 0f;
        int rising = 0;
        for (float t = 0f; t < 2.6f; t += Time.deltaTime)
        {
            thrown |= r.StatusOf(World.ThrownStatus) > 0;
            float high = Mathf.Max(grid.SpireRide(r.Id) ?? 0f, far != null ? grid.SpireRide(far.Id) ?? 0f : 0f);
            rode = Mathf.Max(rode, high);
            rising = Mathf.Max(rising, FindObjectsByType<SpireRise>(FindObjectsSortMode.None).Length);
            if (!shot && high > 2f)
            {
                Shot("earth-spires-ride");
                shot = true;
            }
            yield return null;
        }
        Shot("earth-spires");
        Metric("spires_rode_metres", rode);
        Metric("spires_rising_most", rising);
        Check("spires_burst_up", rising >= 4, $"{rising} rising at once at most");
        Check("spires_lift_the_raider", rode > 2f, $"{rode:0.0} m up on a spire");
        int standing = BlockingTo(me, r) - before;
        Metric("spires_standing", standing);
        Check("spires_hit_it", thrown || r.Hp < hp, $"thrown {thrown}, {hp - r.Hp} hurt");
        Check("spires_stand", standing >= 4, $"{standing} more blocking cells out to the raider");
    }

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
        bool shot = false;
        for (float t = 0f; t < 4f; t += Time.deltaTime)
        {
            drawn = Mathf.Max(drawn, grid.MoversShown);
            if (!shot && t > 0.8f && grid.MoversShown > 0)
            {
                Shot("iceberg-rolling");
                shot = true;
            }
            yield return null;
        }
        Shot("iceberg");
        Metric("iceberg_pack_ice_cells", grid.PackIceShown);
        Check("iceberg_leaves_pack_ice", grid.PackIceShown >= 5, $"{grid.PackIceShown} cells of pack ice drawn");
        int ice = IceRound(from) - iceBefore;
        Metric("iceberg_ice_cells", ice);
        Check("iceberg_rolls_on_screen", drawn > 0, $"{drawn} movers");
        Check("iceberg_crushes_it", !r.Alive || r.Hp < hp, $"{hp - r.Hp} hurt");
        Check("iceberg_leaves_walkable_ice", ice >= 5, $"{ice} more cells of ice round where it was cast");
    }

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
