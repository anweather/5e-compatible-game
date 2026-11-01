using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.SceneManagement;

// The probe's lookfx scenario: every stage of a Fireball takes the spell's look.
// One Fireball cast by elements as it ships, the Blue fireballs mod switched on
// mid-game, one beside it, then a third with the mod switched off while its fire
// burns, and a last one after. (As a rule switch of the probe's own, not the mods
// panel's door: that would drop the noon and the fire settings the casts are
// compared under; looksmod goes through the door.) Each stage is read
// from what's actually drawn: the orbs' colour, the materials on the bead and
// the blast, the debris's particles, each flame, the embers and the fire lights.
// With --shots it is a film (ClipFilm), with a beat for each stage.
public class SpellLookFxProbe : LooksModProbe
{
    const string Mod = "blue-fireballs";
    const string LookShader = "Prototype/LookParticle";
    const float Linger = 1f;

    int scenes;
    float side = -1f;

    void Counted(Scene s, LoadSceneMode m) => scenes++;

    protected override IEnumerator Run()
    {
        var catalog = grid.GetComponent<RuleSwitcher>().Catalog.Find(Mod);
        Check("mod_in_the_catalog", catalog != null && catalog.Error == null, catalog?.Error ?? Mod);
        if (catalog == null) yield break;
        if (!FindMeadow(out var a))
        {
            Check("meadow_found", false, "no open grass for three casts near the yard");
            yield break;
        }
        var b = new Vector2Int(a.x + Apart, a.y);
        var c = new Vector2Int(a.x + Apart / 2, a.y + Back);
        var view = grid;
        int handle = SceneManager.GetActiveScene().handle;
        SceneManager.sceneLoaded += Counted;
        bool casterWasOn = caster.enabled;
        caster.enabled = true;
        caster.Select(SpellIds.Fireball);

        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }, 'fire': { 'spreadChance': 0 }");
        Frame(a);
        yield return Wait(0.6f);
        if (Filming)
        {
            StartFilm();
            yield return Wait(0.5f);
        }

        Beat("plain_at_s");
        yield return Fireball(a, "plain", false);

        string fingerprint = grid.CurrentRules.Fingerprint;
        grid.QueueRules(grid.CurrentRules.WithMods(grid.CurrentRules.Mods.Append(catalog.AsActive())));
        yield return Wait(0.6f);
        Check("mod_in_force", caster.TintOf(SpellIds.Fireball).Any, $"turned {caster.TintOf(SpellIds.Fireball).Turn:0.00}");
        Check("look_keeps_the_fingerprint", grid.CurrentRules.Fingerprint == fingerprint, grid.CurrentRules.Fingerprint);

        Frame(b);
        yield return Wait(0.4f);
        Beat("blue_at_s");
        yield return Fireball(b, "blue", true);
        Spreads(b);

        Frame(c);
        yield return Wait(0.4f);
        Beat("switch_at_s");
        yield return Burning(c);
        grid.QueueRules(grid.CurrentRules.WithMods(grid.CurrentRules.Mods.Where(m => m.Id != Mod)));
        yield return Wait(0.8f);
        var turned = FlamesAround(c).Count(f => f != 0f);
        Metric("flames_still_turned_after_switch", turned);
        Check("switch_redraws_what_burns", FlamesAround(c).Any() && turned == 0, $"{FlamesAround(c).Count()} flames, {turned} still turned");
        Check("flames_not_rebuilt_by_a_scene", scenes == 0 && SceneManager.GetActiveScene().handle == handle && grid == view, $"{scenes} scenes loaded, same scene and grid");

        yield return Wait(1.6f);
        Beat("plain_again_at_s");
        yield return Fireball(c, "plain_again", false);
        yield return GreenMeteor(a);

        SceneManager.sceneLoaded -= Counted;
        caster.AimOverride = null;
        caster.enabled = casterWasOn;
        if (Filming) yield return EndFilm();
    }

    // The wizard to the west of a spot, looking at it, the camera side on to take in both.
    void Frame(Vector2Int at)
    {
        var point = grid.CellCenter(at.x, at.y);
        point.y = grid.GroundAt(point);
        var stand = grid.CellCenter(at.x - 7, at.y);
        stand.y = grid.GroundAt(stand) + 0.1f;
        wizard.transform.position = stand;
        puppet.Face(point);
        // A tree's own trunk can stand where the camera would: from whichever side has fewer.
        side = Trees(at, -1) <= Trees(at, 1) ? -1f : 1f;
        Wide(at);
    }

    int Trees(Vector2Int at, int way)
    {
        int found = 0;
        for (int y = 2; y <= 14; y++)
            for (int x = -10; x <= 6; x++)
                if (World.At(at.x + x, at.y - way * y)?.T == TerrainIds.Tree) found++;
        return found;
    }

    void Wide(Vector2Int at)
    {
        var mid = (grid.CellCenter(at.x, at.y) + grid.CellCenter(at.x - 7, at.y)) / 2f;
        mid.y = grid.GroundAt(mid);
        Cut(mid + V(0f, 5f, side * 15f), mid + V(0f, 1.4f, 0f));
    }

    // Close on the hands, for the orbs.
    void Close()
    {
        var hands = wizard.transform.position + Vector3.up * 1.2f + wizard.transform.forward * 0.3f;
        Cut(hands + wizard.transform.forward * 1.2f + V(0f, 0.4f, side * 2.4f), hands);
    }

    IEnumerator Loaded(Vector2Int at)
    {
        Close();
        var point = grid.CellCenter(at.x, at.y);
        point.y = grid.GroundAt(point);
        caster.AimOverride = point;
        caster.LoadElement(ElementIds.Fire);
        yield return Wait(0.2f);
        caster.LoadElement(ElementIds.Fire);
        yield return Wait(0.35f);
    }

    // A Fireball cast and blue in the cast's every stage, or not.
    IEnumerator Fireball(Vector2Int at, string name, bool blue)
    {
        var beats = wizard.GetComponent<CastBeats>();
        SpellVisuals.LastImpact = null;
        yield return Loaded(at);
        var orb = beats.OrbColor(0);
        Stage(name, "orbs", orb.b > orb.r, blue, $"{orb}");
        Metric($"orb_blue_{name}", orb.b);
        Beat($"{name}_orbs_at_s");
        Shoot($"{name}-orbs");
        yield return Wait(0.2f);
        Wide(at);

        SpellProjectile bead = null;
        for (float t = 0f; bead == null && t < 3f; t += Time.deltaTime)
        {
            bead = FindAnyObjectByType<SpellProjectile>();
            yield return null;
        }
        Check($"{name}_bead_flew", bead != null);
        Beat($"{name}_bead_at_s");
        if (bead != null) Stage(name, "bead", Shader(bead.gameObject) > 0, blue, $"{Shader(bead.gameObject)} of its materials in the look");

        for (float t = 0f; SpellVisuals.LastImpact == null && t < 3f; t += Time.deltaTime) yield return null;
        var blast = SpellVisuals.LastImpact;
        Check($"{name}_burst_played", blast != null);
        Beat($"{name}_burst_at_s");
        if (blast != null) Stage(name, "burst", Shader(blast) > 0, blue, $"{Shader(blast)} of its materials in the look");
        yield return null;
        var debris = Debris();
        Stage(name, "debris", debris.b > debris.r, blue, $"{debris}");

        var embers = new System.Collections.Generic.List<float>();
        for (float t = 0f; t < Linger; t += Time.deltaTime)
        {
            var now = Embers(at).ToList();
            if (now.Count > 0) embers = now;
            yield return null;
        }
        Beat($"{name}_lingering_at_s");
        var flames = FlamesAround(at).ToList();
        Check($"{name}_fire_burns", flames.Count > 0, $"{flames.Count} flames");
        Stage(name, "flames", flames.Any(t => t != 0f), blue, $"{flames.Count(t => t != 0f)} of {flames.Count} flames turned");
        Stage(name, "all_flames", flames.All(t => t != 0f), blue, $"{flames.Count(t => t != 0f)} of {flames.Count} flames turned");
        var lit = FireLights(at).ToList();
        Check($"{name}_fire_lit", lit.Count > 0, $"{lit.Count} lights");
        Stage(name, "fire_light", lit.Any(l => l.b > l.r), blue, string.Join(" ", lit));
        Metric($"embers_{name}", embers.Count);
        Check($"{name}_embers_seen", embers.Count > 0, $"{embers.Count} cells");
        Stage(name, "embers", embers.Any(t => t != 0f), blue, $"{embers.Count} cells");
        yield return Wait(Linger);
    }

    void Shoot(string label)
    {
        if (!Filming) Shot(label);
    }

    // A check that a stage shows the look exactly when it should.
    void Stage(string name, string stage, bool blueNow, bool blue, string detail) =>
        Check($"{name}_{stage}_{(blue ? "in_the_look" : "as_authored")}", blueNow == blue, detail);

    // How many of a show's materials are drawn turned.
    static int Shader(GameObject show) =>
        show.GetComponentsInChildren<ParticleSystemRenderer>(true).SelectMany(r => r.sharedMaterials).Count(m => m != null && m.shader.name == LookShader);

    static Color Debris()
    {
        var bursts = GameObject.Find("Bursts");
        if (bursts == null) return default;
        var alive = bursts.GetComponentsInChildren<ParticleSystem>().FirstOrDefault(p => p.name == nameof(BurstFx.Kind.Debris) && p.IsAlive(true));
        return alive != null ? alive.main.startColor.colorMin : default;
    }

    static System.Collections.Generic.IEnumerable<(int x, int y)> Around(Vector2Int at)
    {
        for (int dy = -Half; dy <= Half; dy++)
            for (int dx = -Half; dx <= Half; dx++) yield return (at.x + dx, at.y + dy);
    }

    System.Collections.Generic.IEnumerable<float> FlamesAround(Vector2Int at) =>
        Around(at).SelectMany(c => grid.Flames.FlamesOn(World.Idx(c.x, c.y))).Select(f => f.turn);

    System.Collections.Generic.IEnumerable<Color> FireLights(Vector2Int at)
    {
        var point = grid.CellCenter(at.x, at.y);
        return grid.GetComponentsInChildren<Light>().Where(l => l.name == "Fire light" && l.enabled && Vector3.Distance(l.transform.position, point) < Half * grid.cellSize * 1.5f).Select(l => l.color);
    }

    System.Collections.Generic.IEnumerable<float> Embers(Vector2Int at) =>
        Around(at).Where(c => World.At(c.x, c.y).Cover == CoverIds.Embers).Select(c => grid.EmberTurnOn(c.x, c.y));

    // A fire spreading from a blue cell burns blue, though the spell never touched that cell: lit beside the
    // fire by hand here, as the rules' spread is off so the fire area stays the blast's.
    void Spreads(Vector2Int at)
    {
        var burning = Around(at).Where(c => World.At(c.x, c.y).Fire > 0).ToList();
        foreach (var (x, y) in burning)
            for (int d = 1; d <= Half; d++)
            {
                var next = World.At(x + d, y);
                if (next == null || next.Fire > 0) continue;
                if (grid.FireTint(x + d, y).Any) continue;
                if (d > 1) break;
                next.Fire = 1;
                Metric("spread_x", x + d);
                StartCoroutine(SpreadSeen(x + d, y));
                return;
            }
    }

    IEnumerator SpreadSeen(int x, int y)
    {
        yield return Wait(0.3f);
        Check("spread_keeps_the_look", grid.FireTint(x, y).Any, $"cell ({x}, {y})");
    }

    // A meteor in a look of its own: the rock's light and trail, then its blast.
    IEnumerator GreenMeteor(Vector2Int at)
    {
        var green = new ActiveMod("green-meteor", "{ 'patch': { 'spells': [ { 'id': 'meteor', 'group': 'fire', 'name': 'Meteor', 'look': { 'family': 'meteor', 'color': [0.2, 1, 0.4] }, 'level': 7, 'shape': 'burst', 'radius': 4, 'element': 'fire' } ] } }".Replace('\'', '"'));
        grid.QueueRules(grid.CurrentRules.WithMods(grid.CurrentRules.Mods.Append(green)));
        yield return Wait(0.6f);
        Frame(at);
        Beat("meteor_at_s");
        SpellVisuals.LastImpact = null;
        caster.Cast(SpellIds.Meteor, at.x, at.y, 0);
        MeteorFall meteor = null;
        for (float t = 0f; meteor == null && t < 3f; t += Time.deltaTime)
        {
            meteor = FindAnyObjectByType<MeteorFall>();
            yield return null;
        }
        Check("meteor_fell", meteor != null);
        if (meteor == null) yield break;
        var light = meteor.GetComponentInChildren<Light>().color;
        Check("meteor_light_in_the_look", light.g > light.r, $"{light}");
        var trail = meteor.GetComponentInChildren<TrailRenderer>().colorGradient.Evaluate(0.35f);
        Check("meteor_trail_in_the_look", trail.g > trail.r, $"{trail}");
        for (float t = 0f; meteor != null && Shader(meteor.gameObject) == 0 && t < 1f; t += Time.deltaTime) yield return null;
        Check("meteor_flames_in_the_look", meteor != null && Shader(meteor.gameObject) > 0, "its flames' materials");
        yield return Wait(MeteorFall.Seconds + 1f);
        Beat("meteor_landed_at_s");
    }

    // A Fireball in the mod's look, left burning.
    IEnumerator Burning(Vector2Int at)
    {
        SpellVisuals.LastImpact = null;
        yield return Loaded(at);
        Wide(at);
        for (float t = 0f; SpellVisuals.LastImpact == null && t < 3f; t += Time.deltaTime) yield return null;
        yield return Wait(1.2f);
        Check("switch_fire_is_turned", FlamesAround(at).Any(f => f != 0f), $"{FlamesAround(at).Count()} flames");
    }
}
