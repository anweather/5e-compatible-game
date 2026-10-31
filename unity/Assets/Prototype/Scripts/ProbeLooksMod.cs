using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.SceneManagement;

// The probe's looksmod scenario (docs/RULES_MCP.md, "Looks: procedural only"): the view draws
// from the rules in force. A Fireball with the rules as they are, the Big fireball mod switched
// on mid-game through the mods panel's door (RuleSwitcher) with no scene load, the same Fireball
// on the ground beside it, then the Moonstone mod and its new material drawn by the block family,
// and a look-only change that redraws what already stands. With --shots it is a film (ClipFilm).
public class LooksModProbe : ClipProbe
{
    const int Half = 6, Apart = 16, Back = 14;
    const float Landed = 3.5f;

    readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[4096];
    int scenesLoaded;
    bool Filming => !string.IsNullOrEmpty(ScreenshotDir);

    void CountLoad(Scene s, LoadSceneMode m) => scenesLoaded++;

    protected override IEnumerator Run()
    {
        var switcher = grid.GetComponent<RuleSwitcher>();
        Check("switcher_in_scene", switcher != null);
        if (switcher == null) yield break;
        if (!FindMeadow(out var a))
        {
            Check("meadow_found", false, "no open grass for three casts near the yard");
            yield break;
        }
        var b = new Vector2Int(a.x + Apart, a.y);
        var c = new Vector2Int(a.x + Apart / 2, a.y + Back);
        var scene = SceneManager.GetActiveScene();
        var view = grid;
        int handle = scene.handle;
        SceneManager.sceneLoaded += CountLoad;

        // Noon, clear, and no fire spreading: the fire area is the blast's alone.
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }, 'fire': { 'spreadChance': 0 }");
        var mid = (grid.CellCenter(a.x, a.y) + grid.CellCenter(b.x, b.y)) / 2f;
        mid.y = grid.GroundAt(mid);
        var stand = grid.CellCenter(a.x + Apart / 2, a.y - 7);
        stand.y = grid.GroundAt(stand) + 0.1f;
        wizard.transform.position = stand;
        Cut(mid + V(0f, 19f, -35f), mid + V(0f, 0f, 2f));
        yield return Wait(0.6f);
        if (Filming)
        {
            StartFilm();
            yield return Wait(0.5f);
        }

        var off = new Measure();
        yield return Blast(a, off);
        Report("off", off);

        string fingerprint = grid.CurrentRules.Fingerprint;
        int tick = World.Tick, frame = Time.frameCount;
        Beat("mod_on_at_s");
        Check("big_fireball_on", switcher.Toggle("big-fireball") == null, switcher.LastError ?? "switched on");
        yield return Wait(0.6f);
        Check("mod_in_force", World.Rules.Spell(SpellIds.Fireball).Radius == 4 && grid.CurrentRules.Fingerprint != fingerprint, $"radius {World.Rules.Spell(SpellIds.Fireball).Radius}");
        Check("game_ran_on", World.Tick > tick && Time.frameCount > frame, $"tick {tick} to {World.Tick}");

        var on = new Measure();
        yield return Blast(b, on);
        Report("on", on);
        yield return Wait(1f);

        Check("blast_bigger", on.BlastMetres >= off.BlastMetres * 1.6f, $"{off.BlastMetres:0.0} m to {on.BlastMetres:0.0} m");
        Check("fire_area_bigger", on.FireCells >= off.FireCells * 2 && on.FireRadius > off.FireRadius, $"{off.FireCells} cells out to {off.FireRadius} to {on.FireCells} out to {on.FireRadius}");
        Check("crater_bigger", on.CraterCells > off.CraterCells && on.CraterRadius > off.CraterRadius, $"radius {off.CraterRadius} to {on.CraterRadius}, depth {off.CraterDepth} to {on.CraterDepth}, {off.CraterCells} to {on.CraterCells} cells");
        Check("crater_drawn_bigger", on.DrawnDrop > off.DrawnDrop, $"the terrain drew {off.DrawnDrop:0.00} m down, then {on.DrawnDrop:0.00} m");
        Check("fire_drawn_bigger", on.FlamesDrawn > off.FlamesDrawn, $"{off.FlamesDrawn} cells of flames drawn, then {on.FlamesDrawn}");

        Beat("moonstone_at_s");
        yield return Moonstone(switcher, c);

        Check("no_scene_reload", scenesLoaded == 0 && SceneManager.GetActiveScene().handle == handle && grid == view, $"{scenesLoaded} scenes loaded, same scene and grid");
        SceneManager.sceneLoaded -= CountLoad;
        if (Filming) yield return EndFilm();
    }

    // Where in the film a beat falls, for its captions.
    void Beat(string name)
    {
        if (Filming) Metric(name, FilmedSeconds);
    }

    // What one Fireball did, in the cells and as drawn.
    sealed class Measure
    {
        public float BlastMetres, DrawnDrop;
        public int FireCells, FireRadius, FlamesDrawn, CraterCells, CraterRadius, CraterDepth;
    }

    void Report(string name, Measure m)
    {
        Metric($"blast_m_{name}", m.BlastMetres);
        Metric($"fire_cells_{name}", m.FireCells);
        Metric($"fire_radius_{name}", m.FireRadius);
        Metric($"flames_drawn_{name}", m.FlamesDrawn);
        Metric($"crater_cells_{name}", m.CraterCells);
        Metric($"crater_radius_{name}", m.CraterRadius);
        Metric($"crater_depth_{name}", m.CraterDepth);
        Metric($"crater_drop_m_{name}", m.DrawnDrop);
    }

    IEnumerator Blast(Vector2Int at, Measure m)
    {
        var point = grid.CellCenter(at.x, at.y);
        point.y = grid.GroundAt(point);
        float before = TerrainGround(point);
        puppet.Face(point);
        SpellVisuals.LastImpact = null;
        caster.Cast(SpellIds.Fireball, at.x, at.y, 0);
        for (float t = 0f; t < Landed; t += Time.deltaTime)
        {
            if (SpellVisuals.LastImpact != null) m.BlastMetres = Mathf.Max(m.BlastMetres, Reach(SpellVisuals.LastImpact, point));
            if (t > 1.2f) Burning(at, m);
            yield return null;
        }
        for (int dy = -Half; dy <= Half; dy++)
            for (int dx = -Half; dx <= Half; dx++)
            {
                var cell = World.At(at.x + dx, at.y + dy);
                if (cell.Height >= 0) continue;
                m.CraterCells++;
                m.CraterRadius = Mathf.Max(m.CraterRadius, Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)));
                m.CraterDepth = Mathf.Max(m.CraterDepth, -cell.Height);
            }
        m.DrawnDrop = before - TerrainGround(point);
    }

    // The widest moment of the fire: what burns or has burnt within reach of the middle, and the flames drawn on it.
    void Burning(Vector2Int at, Measure m)
    {
        int cells = 0, radius = 0, flames = 0;
        var bank = grid.Flames;
        for (int dy = -Half; dy <= Half; dy++)
            for (int dx = -Half; dx <= Half; dx++)
            {
                var cell = World.At(at.x + dx, at.y + dy);
                if (cell.Fire <= 0 && cell.T == TerrainIds.Grass) continue;
                cells++;
                radius = Mathf.Max(radius, Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)));
                if (bank != null && bank.FlamesOn(World.Idx(at.x + dx, at.y + dy)).Any()) flames++;
            }
        m.FireCells = Mathf.Max(m.FireCells, cells);
        m.FireRadius = Mathf.Max(m.FireRadius, radius);
        m.FlamesDrawn = Mathf.Max(m.FlamesDrawn, flames);
    }

    // How far from the middle the blast's particles reach across the ground just now, without the slow smoke and debris.
    float Reach(GameObject show, Vector3 middle)
    {
        float far = 0f;
        foreach (var system in show.GetComponentsInChildren<ParticleSystem>())
        {
            if (system.name is "AdditonalSmoke" or "SmokeTrail" or "Debris" or "Embers" or "Embers (1)") continue;
            int n = system.GetParticles(particles);
            bool local = system.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            for (int i = 0; i < n; i++)
            {
                var p = local ? system.transform.TransformPoint(particles[i].position) : particles[i].position;
                p.y = middle.y;
                far = Mathf.Max(far, Vector3.Distance(p, middle));
            }
        }
        return far;
    }

    IEnumerator Moonstone(RuleSwitcher switcher, Vector2Int at)
    {
        var point = grid.CellCenter(at.x, at.y);
        point.y = grid.GroundAt(point);
        Cut(point + V(0f, 9f, -17f), point + V(0f, 0.8f, 0f));
        Check("moonstone_on", switcher.Toggle("moonstone") == null, switcher.LastError ?? "switched on");
        yield return Wait(0.6f);
        Check("new_spell_has_a_family_show", caster.LookOf("moon-frost")?.family == "motes", caster.LookOf("moon-frost")?.family ?? "none");
        puppet.Face(point);
        caster.Cast("moon-frost", at.x, at.y, 0);
        yield return Wait(2.6f);

        var cell = World.At(at.x, at.y);
        var look = World.Rules.Terrain["moonstone"].Look;
        Check("moonstone_in_cells", cell.T == "moonstone", cell.T);
        var drawn = grid.DressingAt(at.x, at.y);
        Check("moonstone_drawn_by_family", drawn != null && drawn.GetComponentsInChildren<Renderer>().Length > 0, drawn != null ? drawn.name : "nothing");
        if (drawn == null) yield break;
        var r = drawn.GetComponentInChildren<Renderer>();
        float tall = r.bounds.size.y - 0.2f;
        Metric("moonstone_height_m", tall);
        Check("moonstone_as_tall_as_its_look", Mathf.Abs(tall - look.Height) < 0.05f, $"{tall:0.00} m for {look.Height:0.00}");
        var tint = new MaterialPropertyBlock();
        r.GetPropertyBlock(tint);
        var base_ = tint.GetColor("_BaseColor");
        var shine = tint.GetColor("_EmissionColor");
        Metric("moonstone_blue", base_.b);
        Check("moonstone_takes_its_colour", base_.b > base_.r * 1.2f, $"{base_}");
        Check("moonstone_glows", shine.maxColorComponent > 0.2f, $"{shine}");
        var top = point + Vector3.up * 8f;
        Check("moonstone_blocks", Physics.RaycastAll(top, Vector3.down, 12f, ~0, QueryTriggerInteraction.Collide).Any(h => CellBlockers.IsBlocker(h.collider)), "a blocker on its cell");
        Shot("moonstone");

        // A look alone changes: what already stands is drawn again, on the rules' say.
        Beat("look_change_at_s");
        var relook = new ActiveMod("relook-moonstone", "{ 'patch': { 'terrain': { 'moonstone': { 'name': 'Moonstone', 'tags': ['blocking', 'trellis'], 'break': { 'leaves': 'rubble' }, 'look': { 'family': 'block', 'color': [1, 0.55, 0.75], 'height': 2.6, 'glow': 0.3, 'roughness': 0.25 } } } } }");
        string fingerprint = grid.CurrentRules.Fingerprint;
        grid.QueueRules(grid.CurrentRules.WithMods(grid.CurrentRules.Mods.Append(relook)));
        yield return Wait(1.6f);
        var again = grid.DressingAt(at.x, at.y);
        float taller = again != null ? again.GetComponentInChildren<Renderer>().bounds.size.y - 0.2f : 0f;
        Metric("relook_height_m", taller);
        Check("look_only_redraws_on_switch", Mathf.Abs(taller - 2.6f) < 0.05f, $"{taller:0.00} m");
        Check("look_only_keeps_the_fingerprint", grid.CurrentRules.Fingerprint == fingerprint, grid.CurrentRules.Fingerprint);
    }

    // Open flat grass for the three casts, as near the yard as there is.
    bool FindMeadow(out Vector2Int found)
    {
        foreach (var o in Candidates())
            if (Open(o) && Open(new Vector2Int(o.x + Apart, o.y)) && Open(new Vector2Int(o.x + Apart / 2, o.y + Back)) && Open(new Vector2Int(o.x + Apart / 2, o.y - 7)))
            {
                found = o;
                return true;
            }
        found = default;
        return false;
    }

    IEnumerable<Vector2Int> Candidates()
    {
        var home = grid.YardCell(12, 4);
        for (int r = 0; r < 60; r += 2)
            for (int dy = -r; dy <= r; dy += 2)
                for (int dx = -r; dx <= r; dx += 2)
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == r) yield return new Vector2Int(home.x + dx, home.y + dy);
    }

    bool Open(Vector2Int o)
    {
        for (int dy = -Half; dy <= Half; dy++)
            for (int dx = -Half; dx <= Half; dx++)
            {
                if (!World.InBounds(o.x + dx, o.y + dy)) return false;
                var c = World.At(o.x + dx, o.y + dy);
                if (c == null || c.T != TerrainIds.Grass || c.Water > 0 || c.Height != 0 || c.Cover != null) return false;
            }
        return true;
    }
}
