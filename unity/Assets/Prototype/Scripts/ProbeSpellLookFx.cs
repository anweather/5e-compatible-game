using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.SceneManagement;

// The probe's lookfx scenario: every stage of a Fireball takes the spell's look.
// One Fireball cast by elements as it ships, its look changed mid-game through the
// rules MCP over real HTTP (set_spell look.color, apply: only that, no rebuild), one
// beside it, then a third with the mod reverted while its fire burns, and a last
// one after. Each stage is read
// from what's actually drawn: the orbs' colour, the materials on the bead and
// the blast, the debris's particles, each flame, the embers and the fire lights.
// Then only the fire element's colour changed, and a Fireball that chose no colour
// follows it at every stage, the orbs before the spell is known too (J-052); and one
// that did choose keeps its own among that fire.
// With --shots it is a film (ClipFilm), with a beat for each stage.
public class SpellLookFxProbe : LooksModProbe
{
    const string Title = "Blue fireball", Mod = "blue-fireball", Blue = "[0.2, 0.45, 1]", Green = "[0.15, 1, 0.35]";
    const string FireTitle = "Blue fire", FireMod = "blue-fire", OwnTitle = "Green fireball", OwnMod = "green-fireball";
    const string LookShader = "Prototype/LookParticle";
    const float Linger = 1f;

    int scenes;
    float side = -1f;

    /// <summary>Which side of the casts the camera stands, as Frame chose.</summary>
    protected float Side => side;

    string calmSim;
    // What a stage in the look looks like: blue, but for the Fireball that chose green among blue fire.
    System.Func<Color, bool> inLook = IsBlue;
    Color firstOrb;

    void Counted(Scene s, LoadSceneMode m) => scenes++;

    protected override IEnumerator Run()
    {
        Check("mcp_serving", RulesMcpServer.Url != null, RulesMcpServer.ConnectLine);
        if (RulesMcpServer.Url == null) yield break;
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

        yield return Calm();
        Frame(a);
        yield return Wait(0.6f);
        if (Filming)
        {
            StartFilm();
            yield return Wait(0.5f);
        }

        Beat("plain_at_s");
        yield return Fireball(a, "plain", false);

        string fingerprint = grid.CurrentRules.Effective.Fingerprint;
        yield return ThroughMcp("{\"id\":\"fireball\",\"field\":\"look.color\",\"value\":\"" + Blue + "\"}", Title);
        yield return Calm();
        Check("mod_in_force", caster.TintOf(SpellIds.Fireball).Any, $"turned {caster.TintOf(SpellIds.Fireball).Turn:0.00}");
        Check("look_keeps_the_fingerprint", grid.CurrentRules.Effective.Fingerprint == fingerprint, grid.CurrentRules.Effective.Fingerprint);

        Frame(b);
        yield return Wait(0.4f);
        Beat("blue_at_s");
        yield return Fireball(b, "blue", true);
        Spreads(b);

        Frame(c);
        yield return Wait(0.4f);
        Beat("switch_at_s");
        yield return Burning(c);
        var revert = RoomMcpProbe.Post(RoomMcpProbe.Tool("revert", "{\"mod\":\"" + Mod + "\"}"));
        yield return RoomMcpProbe.Until(revert);
        Check("mcp_reverted", revert.Result.body.Contains("switched off"), revert.Result.body);
        yield return Wait(0.8f);
        var turned = FlamesAround(c).Count(f => f != 0f);
        Metric("flames_still_turned_after_switch", turned);
        Check("switch_redraws_what_burns", FlamesAround(c).Any() && turned == 0, $"{FlamesAround(c).Count()} flames, {turned} still turned");
        Check("flames_not_rebuilt_by_a_scene", scenes == 0 && SceneManager.GetActiveScene().handle == handle && grid == view, $"{scenes} scenes loaded, same scene and grid");

        yield return Calm();
        yield return Wait(1.6f);
        Beat("plain_again_at_s");
        yield return Fireball(c, "plain_again", false);

        yield return FollowsTheElement(a, b);

        SceneManager.sceneLoaded -= Counted;
        caster.AimOverride = null;
        caster.enabled = casterWasOn;
        if (Filming) yield return EndFilm();
    }

    // Only the element's colour moves: a spell that chose none follows it, one that chose keeps its own (J-052).
    IEnumerator FollowsTheElement(Vector2Int at, Vector2Int other)
    {
        string fingerprint = grid.CurrentRules.Effective.Fingerprint;
        yield return ThroughMcp("{\"id\":\"fire\",\"param\":\"color\",\"value\":\"" + Blue + "\",\"table\":\"element\"}", FireTitle, "set_look");
        yield return Calm();
        Check("element_in_force", grid.FireElementTint.Any, $"turned {grid.FireElementTint.Turn:0.00}");
        Check("element_keeps_the_fingerprint", grid.CurrentRules.Effective.Fingerprint == fingerprint, grid.CurrentRules.Effective.Fingerprint);
        Check("element_not_in_the_spell", LookDef.SameColor(grid.CurrentRules.Base.Spell(SpellIds.Fireball).Look, World.Rules.Spell(SpellIds.Fireball).Look), "only the element's colour changed");

        Frame(at);
        yield return Wait(0.4f);
        Beat("element_at_s");
        yield return Fireball(at, "element", true, true);
        var burning = Around(at).Where(c => World.At(c.x, c.y).Fire > 0).ToList();
        Check("element_fire_is_the_elements", burning.Count > 0 && burning.All(c => grid.FireTint(c.x, c.y).Turn == grid.FireElementTint.Turn), $"{burning.Count} cells burning");

        yield return ThroughMcp("{\"id\":\"fireball\",\"field\":\"look.color\",\"value\":\"" + Green + "\"}", OwnTitle);
        yield return Calm();
        Frame(other);
        yield return Wait(0.4f);
        Beat("chosen_at_s");
        inLook = IsGreen;
        yield return Fireball(other, "chosen", true, true);
        inLook = IsBlue;
        burning = Around(other).Where(c => World.At(c.x, c.y).Fire > 0).ToList();
        Check("chosen_fire_keeps_its_own", burning.Any(c => grid.FireTint(c.x, c.y).Turn != grid.FireElementTint.Turn), $"{burning.Count} cells burning");

        yield return Off(OwnMod);
        yield return Off(FireMod);
        yield return Wait(0.6f);
        Check("element_back", !grid.FireElementTint.Any && !caster.TintOf(SpellIds.Fireball).Any, "both mods off");
    }

    IEnumerator Off(string mod)
    {
        var revert = RoomMcpProbe.Post(RoomMcpProbe.Tool("revert", "{\"mod\":\"" + mod + "\"}"));
        yield return RoomMcpProbe.Until(revert);
        Check($"mcp_reverted_{mod.Replace('-', '_')}", revert.Result.body.Contains("switched off"), revert.Result.body);
    }

    static bool IsBlue(Color c) => c.b > c.r;

    static bool IsGreen(Color c) => c.g > c.r && c.g > c.b;

    // Noon, clear, no fire spreading, as a mod of the probe's own: on top of what's on, as the room's change drops it.
    protected IEnumerator Calm()
    {
        // Once: the hour in it is worked out from the clock, and the same mod keeps the same fingerprint.
        calmSim ??= At(12f);
        var calm = new ActiveMod("probe-calm", "{ 'patch': { 'sim': { " + calmSim + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }, 'fire': { 'spreadChance': 0 } } } }");
        grid.QueueRules(grid.CurrentRules.WithMods(grid.CurrentRules.Mods.Where(m => m.Id != calm.Id).Append(calm)));
        yield return Wait(0.5f);
    }

    // One change (set_spell, or the tool named) in a new draft and an apply with a title, as a player's Claude would: over HTTP, the game's only door.
    protected IEnumerator ThroughMcp(string change, string title, string tool = "set_spell")
    {
        var written = RoomMcpProbe.Post(RoomMcpProbe.Tool(tool, change));
        yield return RoomMcpProbe.Until(written);
        string body = written.Result.body;
        int at = body.IndexOf("Draft ", System.StringComparison.Ordinal);
        Check($"mcp_draft_{title.Replace(' ', '_')}", at >= 0, body);
        if (at < 0) yield break;
        string draft = body.Substring(at + 6).Split(':')[0];
        var applied = RoomMcpProbe.Post(RoomMcpProbe.Tool("apply", $"{{\"mod\":\"{draft}\",\"title\":\"{title}\"}}"));
        yield return RoomMcpProbe.Until(applied);
        Check($"mcp_applied_{title.Replace(' ', '_')}", applied.Result.body.Contains("switched on"), applied.Result.body);
        yield return Wait(0.6f);
    }

    // The wizard to the west of a spot, looking at it, the camera side on to take in both.
    protected void Frame(Vector2Int at)
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
        firstOrb = wizard.GetComponent<CastBeats>().OrbColor(0);
        caster.LoadElement(ElementIds.Fire);
        yield return Wait(0.35f);
    }

    // A Fireball cast and in the look at the cast's every stage, or not; the first orb, before there's a spell, is its element's.
    IEnumerator Fireball(Vector2Int at, string name, bool blue, bool elementMoved = false)
    {
        var beats = wizard.GetComponent<CastBeats>();
        SpellVisuals.LastImpact = null;
        yield return Loaded(at);
        var orb = beats.OrbColor(0);
        Check($"{name}_first_orb_{(elementMoved ? "in_the_element" : "as_authored")}", IsBlue(firstOrb) == elementMoved, $"{firstOrb}");
        Stage(name, "orbs", inLook(orb), blue, $"{orb}");
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
        if (blast != null)
        {
            var glow = blast.GetComponentsInChildren<ParticleSystem>(true).FirstOrDefault(p => p.lights.enabled);
            var shine = glow != null ? glow.main.startColor.color : default;
            Check($"{name}_burst_has_lights", glow != null);
            Stage(name, "burst_lights", inLook(shine), blue, $"{shine}");
        }
        yield return null;
        var debris = Debris();
        Stage(name, "debris", inLook(debris), blue, $"{debris}");

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
        Stage(name, "fire_light", lit.Any(inLook), blue, string.Join(" ", lit));
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
    protected static int Shader(GameObject show) =>
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
