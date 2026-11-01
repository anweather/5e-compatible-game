using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The probe's worldlooks scenario (docs/RULES_MCP.md, "Looks"): the world's colours come from the
// rules in force. The colours as they ship are today's, to the number. Then looks only, each a rule
// switch with no scene load, as set_look makes them: the fire element's colour on what's already
// burning, a burning lake, one Fireball that chose its own colour among them (J-052), the gases, the
// covers, the ground and the water, and all of it back again with the mods off. With --shots, a film
// of each into a folder of its own: as it was, then the look coming in.
public class WorldLooksProbe : LooksModProbe
{
    const int Strip = 14, Rows = 3;
    const float Settle = 2.5f, Show = 3f, GasEvery = 1.5f;
    const int Fresh = 22, Clear = 32, GasRows = 3, GasApart = 5, Pond = 6, PondDeep = 6;
    const string SkyMod = "showcase-sky";
    static readonly float[] Blue = { 0.25f, 0.45f, 1f }, Green = { 0.2f, 0.9f, 0.3f }, Pink = { 1f, 0.45f, 0.75f }, Purple = { 0.55f, 0.2f, 0.8f };

    string sky;
    Vector2Int at, lit;

    protected override IEnumerator Run()
    {
        DefaultsAreToday();
        if (!FindMeadow(out at))
        {
            Check("meadow_found", false, "no open grass near the yard");
            yield break;
        }
        sky = "{ 'patch': { 'sim': { " + At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } } } } }";
        grid.QueueRules(grid.CurrentRules.WithMods(new[] { new ActiveMod(SkyMod, sky) }));
        yield return Wait(0.5f);
        var mid = grid.CellCenter(at.x + Strip / 2, at.y + 2);
        mid.y = grid.GroundAt(mid);
        var stand = grid.CellCenter(at.x + Strip / 2, at.y - 7);
        stand.y = grid.GroundAt(stand) + 0.1f;
        wizard.transform.position = stand;
        Cut(mid + V(0f, 9f, -18f), mid);
        yield return Wait(0.6f);
        yield return Take("fire", Flames());
        yield return Take("lake", BurningLake());
        PutOut(lit);
        yield return Take("gas", Gases());
        yield return Take("covers", Covers());
        yield return Take("ground", Ground());
        yield return Take("water", Water());
        yield return AllBack();
    }

    IEnumerator Take(string name, IEnumerator scene)
    {
        ClipFilm film = null;
        if (Filming)
        {
            film = new ClipFilm(this, Path.Combine(ScreenshotDir, name));
            film.Roll();
        }
        yield return scene;
        if (film == null) yield break;
        yield return film.Stop();
        Metric($"{name}_seconds", film.Seconds);
    }

    // The colours the view drew before they came from the rules, as literals.
    void DefaultsAreToday()
    {
        void Same(string name, Color drawn, float r, float g, float b) =>
            Check(name, Mathf.Abs(drawn.r - r) + Mathf.Abs(drawn.g - g) + Mathf.Abs(drawn.b - b) < 1e-4f, $"{drawn.r:0.###} {drawn.g:0.###} {drawn.b:0.###}");
        Same("default_fog", grid.GasColor(GasIds.Fog, Color.magenta), 0.86f, 0.88f, 0.9f);
        Same("default_dust", grid.GasColor(GasIds.Dust, Color.magenta), 0.72f, 0.58f, 0.38f);
        Same("default_cloud", grid.GasColor(GasIds.Cloud, Color.magenta), 0.46f, 0.48f, 0.54f);
        Same("default_storm", grid.GasColor(GasIds.Storm, Color.magenta), 0.13f, 0.14f, 0.2f);
        Same("default_snowcloud", grid.GasColor(GasIds.SnowCloud, Color.magenta), 0.9f, 0.93f, 0.98f);
        Same("default_oil", grid.CoverColor(CoverIds.Oil, Color.magenta), 0.03f, 0.028f, 0.03f);
        Same("default_grease", grid.CoverColor(CoverIds.Grease, Color.magenta), 0.62f, 0.52f, 0.2f);
        Same("default_frost", grid.CoverColor(CoverIds.Frost, Color.magenta), 0.8f, 0.9f, 0.97f);
        Same("default_thorns", grid.CoverColor(CoverIds.Thorns, Color.magenta), 0.3f, 0.33f, 0.16f);
        Same("default_vines", grid.CoverColor(CoverIds.Vines, Color.magenta), 0.2f, 0.4f, 0.14f);
        Same("default_ash", grid.CoverColor(CoverIds.Ash, Color.magenta), 0.35f, 0.34f, 0.33f);
        Check("default_fire_is_its_art", !grid.FireElementTint.Any);
    }

    // Looks only, on top of the sky, as set_look writes them: a colour on the element, or the whole entry with its colour moved.
    IEnumerator Looks(JObject patch)
    {
        var mod = new ActiveMod("world-look", new JObject { ["patch"] = patch }.ToString());
        grid.QueueRules(grid.CurrentRules.WithMods(new[] { new ActiveMod(SkyMod, sky), mod }));
        yield return Wait(0.6f);
    }

    static JObject Colour(float[] c) => new() { ["look"] = new JObject { ["color"] = new JArray(c[0], c[1], c[2]) } };

    JObject Entry(string table, string id, float[] c)
    {
        var entry = (JObject)JObject.Parse(World.Rules.ToJson())[table][id].DeepClone();
        var look = entry["look"] as JObject ?? new JObject();
        look["color"] = new JArray(c[0], c[1], c[2]);
        entry["look"] = look;
        return entry;
    }

    JObject Spell(string id, float[] c)
    {
        var entry = (JObject)JObject.Parse(World.Rules.ToJson())["spells"].First(s => (string)s["id"] == id).DeepClone();
        entry["look"]["color"] = new JArray(c[0], c[1], c[2]);
        return entry;
    }

    void Light(int x, int y)
    {
        var c = World.At(x, y);
        if (c == null) return;
        c.Fire = 3;
        c.Fuel = 200;
    }

    // Burning first, then the element's colour on what burns; then green fire with one blue Fireball in it.
    IEnumerator Flames()
    {
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Strip; x++) Light(at.x + x, at.y + y);
        yield return Wait(Settle);
        yield return Looks(new JObject { ["elements"] = new JObject { ["fire"] = Colour(Blue) } });
        yield return Wait(Show);
        Check("fire_takes_the_element", grid.FireElementTint.Any && grid.FireTint(at.x + 2, at.y + 1).Turn == grid.FireElementTint.Turn, $"turn {grid.FireTint(at.x + 2, at.y + 1).Turn:0.00}");
        Metric("fire_turn", grid.FireElementTint.Turn);
        yield return Looks(new JObject { ["elements"] = new JObject { ["fire"] = Colour(Green) }, ["spells"] = new JArray(Spell(SpellIds.Fireball, Blue)) });
        var blast = new Vector2Int(at.x + Strip - 2, at.y + 1);
        wizard.transform.LookAt(grid.CellCenter(blast.x, blast.y));
        float element = grid.FireElementTint.Turn;
        caster.Cast(SpellIds.Fireball, blast.x, blast.y, 0);
        for (float t = 0f; t < MostLanding && grid.FireTint(blast.x, blast.y).Turn == element; t += Time.deltaTime) yield return null;
        yield return OneChoseItsOwn(blast);
        yield return Wait(Show);
    }

    const float MostLanding = 10f;
    const int Reach = 4;

    // J-052: a spell that chose a colour keeps it among fire of the element's; a fire already burning
    // keeps its colour beside it; a new fire beside it catches its look.
    IEnumerator OneChoseItsOwn(Vector2Int blast)
    {
        yield return Wait(1f);
        float element = grid.FireElementTint.Turn, own = grid.FireTint(blast.x, blast.y).Turn;
        Check("one_fireball_keeps_its_own", own != element, $"its {own:0.00}, the element's {element:0.00}");
        Check("plain_fire_stays_the_elements", grid.FireTint(at.x + 1, at.y + 1).Turn == element, $"{grid.FireTint(at.x + 1, at.y + 1).Turn:0.00}");
        var burning = new List<Vector2Int>();
        for (int y = blast.y - Reach; y <= blast.y + Reach; y++)
            for (int x = blast.x - Reach; x <= blast.x + Reach; x++)
                if (World.At(x, y)?.Fire > 0 && grid.FireTint(x, y).Turn == own) burning.Add(new Vector2Int(x, y));
        Check("its_fire_burns_in_its_colour", burning.Count > 0, $"{burning.Count} cells");
        var beside = burning.SelectMany(b => new[] { b + Vector2Int.right, b + Vector2Int.left, b + Vector2Int.up, b + Vector2Int.down })
            .FirstOrDefault(n => World.At(n.x, n.y)?.Fire == 0 && World.At(n.x, n.y).Water == 0);
        if (beside == default)
        {
            Metric("no_cell_beside_it", 1f);
            yield break;
        }
        Light(beside.x, beside.y);
        yield return Wait(0.5f);
        Check("a_new_fire_beside_it_catches_its_look", World.At(beside.x, beside.y).Fire == 0 || grid.FireTint(beside.x, beside.y).Turn == own, $"{grid.FireTint(beside.x, beside.y).Turn:0.00}");
    }

    // A slick on the lake alight, drawn orange, then every fire blue with one change to the fire element.
    IEnumerator BurningLake()
    {
        var shore = FindLake();
        Check("lake_found", shore.HasValue);
        if (!shore.HasValue) yield break;
        yield return Looks(new JObject());
        var s = shore.Value;
        lit = s;
        var mid = grid.CellCenter(s.x, s.y);
        mid.y = grid.LakeLevelAt(s.x, s.y, World.At(s.x, s.y));
        var stand = mid + V(0f, 0f, -14f);
        stand.y = grid.GroundAt(stand) + 0.1f;
        wizard.transform.position = stand;
        Cut(mid + V(0f, 8f, -17f), mid);
        for (int dy = -3; dy <= 3; dy++)
            for (int dx = -7; dx <= 7; dx++)
            {
                var c = World.At(s.x + dx, s.y + dy);
                if (c == null || c.Water <= 0) continue;
                c.Cover = CoverIds.Oil;
                Light(s.x + dx, s.y + dy);
            }
        yield return Wait(Settle);
        var before = grid.FireTint(s.x, s.y);
        Check("lake_burns_in_its_art", World.At(s.x, s.y).Fire > 0 && !before.Any, $"fire {World.At(s.x, s.y).Fire}");
        yield return Looks(new JObject { ["elements"] = new JObject { ["fire"] = Colour(Blue) } });
        yield return Wait(Show);
        Check("all_fire_blue_turns_the_lake", World.At(s.x, s.y).Fire > 0 && grid.FireTint(s.x, s.y).Any, $"turn {grid.FireTint(s.x, s.y).Turn:0.00}");
    }

    Vector2Int? FindLake()
    {
        if (grid.Valley == null) return null;
        for (int y = 2; y < World.H - 2; y += 3)
            for (int x = 2; x < World.W - 2; x += 3)
            {
                var c = World.At(x, y);
                if (c == null || !grid.Valley.IsLake(c, x, y)) continue;
                var land = World.At(x, y - 12);
                if (land != null && land.Water == 0) return new Vector2Int(x, y);
            }
        return null;
    }

    // A fresh patch of the meadow for each take, away from what burnt.
    void Back()
    {
        at += new Vector2Int(0, Fresh);
        var mid = grid.CellCenter(at.x + Strip / 2, at.y + 2);
        mid.y = grid.GroundAt(mid);
        var stand = grid.CellCenter(at.x + Strip / 2, at.y - 7);
        stand.y = grid.GroundAt(stand) + 0.1f;
        wizard.transform.position = stand;
        Cut(mid + V(0f, 9f, -18f), mid);
    }

    // Everything round a place out, the fire's smoke too, so one take stands clear of the last.
    void PutOut(Vector2Int middle)
    {
        for (int y = -Clear; y <= Clear; y++)
            for (int x = -Clear; x <= Clear; x++)
            {
                var c = World.At(middle.x + x, middle.y + y);
                if (c == null) continue;
                c.Fire = 0;
                c.Fuel = 0;
                c.Gas = null;
                c.GasAmt = 0;
            }
    }

    IEnumerator Gases()
    {
        yield return Looks(new JObject());
        PutOut(at);
        wizard.transform.position = grid.CellCenter(at.x + Strip / 2, at.y - 7) + Vector3.up * 2f;
        var mid = grid.CellCenter(at.x + Strip / 2, at.y + GasApart);
        mid.y = grid.GroundAt(mid);
        Cut(mid + V(0f, 11f, -21f), mid);
        void Fill(int y, string gas)
        {
            for (int row = 0; row < GasRows; row++)
                for (int x = 0; x < Strip; x++)
                {
                    var c = World.At(at.x + x, at.y + y + row);
                    c.Gas = gas;
                    c.GasAmt = 9;
                }
        }
        IEnumerator Keep(float seconds)
        {
            for (float t = 0f; t < seconds; t += GasEvery)
            {
                Fill(0, GasIds.Fog);
                Fill(GasApart, GasIds.Steam);
                Fill(GasApart * 2, GasIds.Smoke);
                yield return Wait(GasEvery);
            }
        }
        yield return Keep(Settle);
        yield return Looks(new JObject
        {
            ["gases"] = new JObject { [GasIds.Fog] = Entry("gases", GasIds.Fog, Pink), [GasIds.Steam] = Entry("gases", GasIds.Steam, Blue), [GasIds.Smoke] = Entry("gases", GasIds.Smoke, Purple) },
        });
        yield return Keep(Show);
        var fog = grid.Fog.ColorAt(World.Idx(at.x + 2, at.y));
        Check("fog_takes_its_look", fog.HasValue && Mathf.Abs(fog.Value.r - Pink[0]) + Mathf.Abs(fog.Value.g - Pink[1]) < 0.01f, fog?.ToString() ?? "no fog there");
        bool Tinted(int y) => Enumerable.Range(0, Strip).Any(x => grid.GasInLookAt(at.x + x, at.y + y));
        Check("steam_takes_its_look", Enumerable.Range(0, GasRows).Any(r => Tinted(GasApart + r)));
        Check("smoke_takes_its_look", Enumerable.Range(0, GasRows).Any(r => Tinted(GasApart * 2 + r)));
    }

    IEnumerator Covers()
    {
        yield return Looks(new JObject());
        PutOut(at);
        Back();
        void Row(int y, string cover)
        {
            for (int x = 0; x < Strip; x++) World.At(at.x + x, at.y + y).Cover = cover;
        }
        Row(0, CoverIds.Oil);
        Row(1, CoverIds.Grease);
        Row(2, CoverIds.Frost);
        Row(3, CoverIds.Thorns);
        Row(4, CoverIds.Vines);
        yield return Wait(Settle);
        yield return Looks(new JObject
        {
            ["covers"] = new JObject
            {
                [CoverIds.Oil] = Entry("covers", CoverIds.Oil, Purple), [CoverIds.Grease] = Entry("covers", CoverIds.Grease, Blue),
                [CoverIds.Frost] = Entry("covers", CoverIds.Frost, Pink), [CoverIds.Thorns] = Entry("covers", CoverIds.Thorns, Blue),
                [CoverIds.Vines] = Entry("covers", CoverIds.Vines, Pink),
            },
        });
        yield return Wait(Show);
        Check("oil_takes_its_look", Near(grid.SlickColorAt(at.x + 3, at.y), Purple), grid.SlickColorAt(at.x + 3, at.y)?.ToString());
        Check("grease_takes_its_look", Near(grid.SlickColorAt(at.x + 3, at.y + 1), Blue), grid.SlickColorAt(at.x + 3, at.y + 1)?.ToString());
        Check("frost_takes_its_look", Near(grid.SlickColorAt(at.x + 3, at.y + 2), Pink), grid.SlickColorAt(at.x + 3, at.y + 2)?.ToString());
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < Strip; x++) World.At(at.x + x, at.y + y).Cover = null;
    }

    static bool Near(Color? c, float[] want) => c.HasValue && Mathf.Abs(c.Value.r - want[0]) + Mathf.Abs(c.Value.g - want[1]) + Mathf.Abs(c.Value.b - want[2]) < 0.02f;

    Vector4 GrassLayer() =>
        Terrain.activeTerrains.SelectMany(t => t.terrainData.terrainLayers).First(l => l != null && l.name.StartsWith(CellPaint.Grass, System.StringComparison.Ordinal)).diffuseRemapMax;

    IEnumerator Ground()
    {
        yield return Looks(new JObject());
        Back();
        var mid = grid.CellCenter(at.x + Strip / 2, at.y + 2);
        mid.y = grid.GroundAt(mid);
        Cut(mid + V(0f, 12f, -30f), mid + V(0f, 0f, 6f));
        yield return Wait(Settle);
        var was = GrassLayer();
        yield return Looks(new JObject { ["terrain"] = new JObject { [TerrainIds.Grass] = Entry("terrain", TerrainIds.Grass, Blue) } });
        yield return Wait(Show);
        var now = GrassLayer();
        Check("grass_takes_its_look", now.z > was.z * 2f && now.y < was.y, $"{was} to {now}");
    }

    IEnumerator Water()
    {
        yield return Looks(new JObject());
        Back();
        for (int y = 0; y < Pond; y++)
            for (int x = 0; x < Strip; x++) World.At(at.x + x, at.y + y).Water = PondDeep;
        yield return Wait(Settle);
        yield return Looks(new JObject { ["elements"] = new JObject { ["water"] = Colour(Pink) } });
        yield return Wait(Show);
        var lake = GameObject.Find("Lake");
        var block = new MaterialPropertyBlock();
        lake.GetComponent<Renderer>().GetPropertyBlock(block);
        var drawn = block.GetColor("_BaseColor");
        Check("lake_takes_the_water", drawn.r > drawn.b, drawn.ToString());
        var pond = grid.CellWaterColor;
        Check("cell_water_takes_the_water", pond.r > pond.g, pond.ToString());
        for (int y = 0; y < Pond; y++)
            for (int x = 0; x < Strip; x++) World.At(at.x + x, at.y + y).Water = 0;
    }

    // With the mods off everything is its art again.
    IEnumerator AllBack()
    {
        var mods = new List<ActiveMod> { new(SkyMod, sky) };
        grid.QueueRules(grid.CurrentRules.WithMods(mods));
        yield return Wait(0.6f);
        Check("back_fire_is_its_art", !grid.FireElementTint.Any);
        Check("back_oil", grid.CoverColor(CoverIds.Oil, Color.magenta).r < 0.05f);
        var grass = GrassLayer();
        yield return Wait(0.2f);
        Metric("grass_back_blue", grass.z);
    }
}
