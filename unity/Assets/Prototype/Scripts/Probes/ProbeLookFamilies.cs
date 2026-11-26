using System;
using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's lookfamilies scenario: every effect family, and each thing the hand family
// draws, in a colour no spell ships with, set through the rules MCP over real HTTP as a
// player's Claude would (set_spell look.color, apply, revert), with nothing rebuilt. Each
// cast reads what's drawn from the show itself; with --shots it is a film (ClipFilm) with a
// beat for each, so each can be looked at. Last, spells of other elements than fire that chose
// no colour, with only their element's colour set (set_look): they follow it (J-052).
public class LookFamiliesProbe : SpellLookFxProbe
{
    const string Pink = "[1, 0.1, 0.6]", Green = "[0.15, 1, 0.35]";
    const float Hold = 3.2f;

    enum Aim { Target, Beside, Self }

    static readonly Color PinkColour = new(1f, 0.1f, 0.6f), GreenColour = new(0.15f, 1f, 0.35f);

    protected override IEnumerator Run()
    {
        Check("mcp_serving", RulesMcpServer.Url != null, RulesMcpServer.ConnectLine);
        if (RulesMcpServer.Url == null) yield break;
        if (!FindMeadow(out var a))
        {
            Check("meadow_found", false, "no open grass for three casts near the yard");
            yield break;
        }
        var spots = new[] { a, new Vector2Int(a.x + Apart, a.y), new Vector2Int(a.x + Apart / 2, a.y + Back) };
        yield return Calm();
        Frame(a);
        yield return Wait(0.5f);
        if (Filming)
        {
            StartFilm();
            yield return Wait(0.3f);
        }
        // Sparks and swirl are small and faint as they ship: the same casts in their own colours, to compare.
        yield return AsShipped(SpellIds.ShockingGrasp, spots[1], Aim.Target);
        yield return AsShipped(SpellIds.GustOfWind, spots[2], Aim.Beside);
        var pink = Burst(PinkColour);
        int spot = 0;
        IEnumerator Next(string family, string spell, Aim aim, Func<bool> seen, bool green = false, string element = null) =>
            Family(family, spell, spots[spot++ % spots.Length], aim, green, seen, element);

        yield return Next("splash", SpellIds.CreateWater, Aim.Target, Impact);
        yield return Next("spray", SpellIds.Grease, Aim.Target, Impact);
        yield return Next("sparks", SpellIds.ShockingGrasp, Aim.Target, Impact);
        yield return Next("swirl", SpellIds.GustOfWind, Aim.Beside, Impact);
        yield return Next("shockwave", SpellIds.Thunderwave, Aim.Self, Impact);
        yield return Next("beam", SpellIds.RayOfFrost, Aim.Beside, () => Line(FindAnyObjectByType<FrostBeam>(), PinkColour));
        yield return Next("storm", SpellIds.SleetStorm, Aim.Target, () => FindAnyObjectByType<SleetStorm>() is { } s && Hue(s.Sleet.main.startColor.color, PinkColour));
        yield return Next("hand_forge", "forge", Aim.Target, () => pink("Forge"));
        yield return Next("hand_acid", "acid-splash", Aim.Target, () => pink("AcidSplash"));
        yield return Next("hand_sand", "sand-blast", Aim.Beside, () => pink("SandBlast"));
        yield return Next("hand_lightning", "lightning-bolt", Aim.Beside, () => GameObject.Find("Arcs") is { } arcs && arcs.GetComponentsInChildren<LineRenderer>().Any(l => Hue(l.endColor, PinkColour)));
        yield return Next("hand_wave", "wave", Aim.Beside, () => GameObject.Find("Foam") is { } foam && Hue(foam.GetComponent<ParticleSystem>().main.startColor.colorMax, PinkColour));
        yield return Next("hand_whirl", "whirlwind", Aim.Target, () => GameObject.Find("Whirlwind") is { } w && Hue(w.GetComponent<ParticleSystem>().main.startColor.colorMin, PinkColour));
        yield return Next("bolt", SpellIds.FireBolt, Aim.Target, () => FindAnyObjectByType<SpellProjectile>() is { } b && Shader(b.gameObject) > 0, true);
        yield return Next("cone", "burning-hands", Aim.Beside, Impact, true);
        yield return Next("blast", SpellIds.Fireball, Aim.Target, () => SpellVisuals.LastImpact != null && Shader(SpellVisuals.LastImpact) > 0, true);
        yield return Next("meteor", SpellIds.Meteor, Aim.Target, () => FindAnyObjectByType<MeteorFall>() is { } m && m.GetComponentInChildren<Light>() is { } glow && Hue(glow.color, GreenColour), true);
        yield return Next("element_beam", SpellIds.RayOfFrost, Aim.Beside, () => Line(FindAnyObjectByType<FrostBeam>(), PinkColour), element: "cold");
        yield return Next("element_splash", SpellIds.CreateWater, Aim.Target, Impact, element: "water");
        yield return Next("element_acid", "acid-splash", Aim.Target, () => pink("AcidSplash"), element: "acid");
        yield return Next("element_lightning", "lightning-bolt", Aim.Beside, () => GameObject.Find("Arcs") is { } arcs && arcs.GetComponentsInChildren<LineRenderer>().Any(l => Hue(l.endColor, PinkColour)), element: "lightning");

        if (Filming) yield return EndFilm();
    }

    // The colour set on the spell, or on its element if one is named.
    IEnumerator Family(string family, string spell, Vector2Int at, Aim aim, bool green, Func<bool> seen, string element = null)
    {
        var def = World.Rules.Spell(spell);
        Check($"{family}_spell_known", def != null, spell);
        if (def == null) yield break;
        string title = "Look " + family.Replace('_', ' ');
        string colour = green ? Green : Pink;
        yield return element != null
            ? ThroughMcp($"{{\"id\":\"{element}\",\"param\":\"color\",\"value\":\"{colour}\",\"table\":\"element\"}}", title, "set_look")
            : ThroughMcp($"{{\"id\":\"{spell}\",\"field\":\"look.color\",\"value\":\"{colour}\"}}", title);
        yield return Calm();
        Frame(at);
        Near(def, at, aim);
        yield return Wait(0.2f);
        Beat($"{family}_at_s");
        SpellVisuals.LastImpact = null;
        Cast(spell, at, aim);
        bool drawn = false;
        for (float t = 0f; t < Hold; t += Time.deltaTime)
        {
            drawn |= seen();
            yield return null;
        }
        Check($"{family}_in_the_look", drawn, spell);
        var revert = RoomMcpProbe.Post(RoomMcpProbe.Tool("revert", "{\"mod\":\"" + title.ToLowerInvariant().Replace(' ', '-') + "\"}"));
        yield return RoomMcpProbe.Until(revert);
        Check($"{family}_reverted", revert.Result.body.Contains("switched off"), revert.Result.body);
        yield return Calm();
    }

    IEnumerator AsShipped(string spell, Vector2Int at, Aim aim)
    {
        Frame(at);
        Near(World.Rules.Spell(spell), at, aim);
        yield return Wait(0.2f);
        Beat($"shipped_{spell.Replace('-', '_')}_at_s");
        Cast(spell, at, aim);
        yield return Wait(Hold);
    }

    // Close on where the show plays, so a small one can be seen.
    void Near(SpellDef def, Vector2Int at, Aim aim)
    {
        var me = grid.CellAt(wizard.transform.position);
        // Where a line's show is to be seen: its end, or near the hand for one that runs from there.
        int along = def.Id switch
        {
            SpellIds.GustOfWind => def.Length,
            "sand-blast" => 2,
            _ => def.Length > 5 ? 4 : def.Length,
        };
        var point = aim switch
        {
            Aim.Beside => grid.CellCenter(me.x + along, me.y),
            Aim.Self => grid.CellCenter(me.x, me.y),
            _ => grid.CellCenter(at.x, at.y),
        };
        point.y = grid.GroundAt(point);
        Cut(point + V(-1f, 3.2f, Side * 8f), point + V(0f, 1f, 0f));
    }

    void Cast(string spell, Vector2Int at, Aim aim)
    {
        var me = grid.CellAt(wizard.transform.position);
        switch (aim)
        {
            case Aim.Beside: caster.Cast(spell, me.x + 1, me.y, 1); break;
            case Aim.Self: caster.Cast(spell, me.x, me.y, 1); break;
            default: caster.Cast(spell, at.x, at.y, 1); break;
        }
    }

    static bool Impact() => SpellVisuals.LastImpact != null && SpellVisuals.LastImpact.GetComponentsInChildren<ParticleSystemRenderer>(true)
        .SelectMany(r => r.sharedMaterials).Any(m => m != null && (m.hideFlags & HideFlags.DontSave) != 0);

    static bool Hue(Color a, Color b)
    {
        Color.RGBToHSV(a, out float one, out float s, out _);
        Color.RGBToHSV(b, out float two, out _, out _);
        return s > 0.2f && Mathf.Abs(Mathf.Repeat(one - two + 0.5f, 1f) - 0.5f) < 0.1f;
    }

    static bool Line(FrostBeam beam, Color look) =>
        beam != null && beam.GetComponentsInChildren<LineRenderer>().Any(l => Hue(l.endColor, look));

    static Func<string, bool> Burst(Color look) => kind =>
        GameObject.Find("Bursts") is { } bursts && bursts.GetComponentsInChildren<ParticleSystem>().Any(p => p.name == kind && p.IsAlive(true) && Hue(p.main.startColor.colorMin, look));
}
