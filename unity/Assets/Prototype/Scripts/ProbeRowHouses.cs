using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Chem;
using Newtonsoft.Json.Linq;
using Unity.Cinemachine;
using UnityEngine;

// The probe's rowhouses scenario (builders.md section 4): a stucco house on the lake's north shore,
// a wing, a shack and a revert, through the real MCP over HTTP as a client would, drawn by RowHouses.
// The house is a test fixture (J-055), as in BuildingTests: nothing named goes in code or base rules.
public class RowHousesProbe : ProbeScenario
{
    protected const string House = "lakeside#1", Title = "A stucco house";
    const string Stucco = "{ \"name\": \"Stucco wall\", \"tags\": [\"blocking\", \"trellis\"], \"break\": { \"leaves\": \"rubble\" }, \"look\": { \"family\": \"wall\", \"form\": \"built\", \"color\": [0.84, 0.71, 0.54] } }";
    const string Tile = "{ \"name\": \"Clay tile\", \"tags\": [\"roof\"], \"look\": { \"family\": \"ground\", \"form\": \"tile\", \"color\": [0.66, 0.3, 0.2] } }";
    const string Style = "{ \"fill\": \"hollow\", \"floor\": { \"kind\": \"slab\", \"height\": 0.15 }, \"wall\": { \"material\": \"stucco-wall\", \"height\": 2.9, \"thickness\": 0.3, \"finish\": { \"pattern\": \"smooth\", \"color\": \"#d6b48a\" } }, \"roof\": { \"shape\": \"hip\", \"pitch\": [4, 12], \"overhang\": 0.45, \"cover\": \"clay-tile\" } }";
    const int HouseWidth = 14, HouseDepth = 8, Dozen = 12, FrameSample = 90;
    const float MostWait = 6f, ViewBack = 28f, ViewHigh = 11f, CloseBack = 18f, ClipSwing = 25f, FromNorth = 180f;
    const int ClipWidth = 1280, ClipHeight = 720, ClipFrames = 48, ClipQuality = 85;

    static readonly HttpClient Http = new();

    protected RowHouses houses;
    protected string answer;

    protected override IEnumerator Run()
    {
        houses = grid.GetComponent<RowHouses>();
        Check("row_houses_here", houses != null && RulesMcpServer.Url != null, RulesMcpServer.Url);
        if (houses == null || RulesMcpServer.Url == null) yield break;
        yield return Tool("find_site", new JObject { ["width"] = HouseWidth, ["depth"] = HouseDepth, ["near"] = "lake", ["side"] = "north", ["edge"] = "shore" });
        var site = Regex.Match(answer ?? "", @"\[(\d+), ?(\d+)\]");
        Check("find_site_reads_the_real_valley", site.Success, answer);
        if (!site.Success) yield break;
        int x = int.Parse(site.Groups[1].Value, CultureInfo.InvariantCulture), y = int.Parse(site.Groups[2].Value, CultureInfo.InvariantCulture);
        var world = grid.World;
        string was = world.At(x + 4, y).T;
        Stand(x + 5, y);
        yield return null;
        yield return Tool("apply", new JObject { ["mod"] = ModJson(x, y), ["title"] = Title });
        yield return Until(() => houses.Root(House) != null);
        Check("the_house_goes_in", houses.Root(House) != null, answer);
        if (houses.Root(House) == null) yield break;
        Drawn("the_house_is_drawn", 20f, 11f);
        Check("its_walls_are_cells", world.At(x + 4, y).T == "stucco-wall", world.At(x + 4, y).T);
        Check("a_door_is_a_door_cell", world.BuildingCellsOf(world.BuildingRowOf(House)).Any(c => c.Terrain == "door"));
        var at = grid.CellAt(caster.transform.position);
        var laid = world.BuildingCellsOf(world.BuildingRowOf(House));
        Check("a_player_on_a_new_wall_is_moved", !world.At(at.x, at.y).T.Contains("stucco"), $"{at.x},{at.y}");
        Check("and_out_of_the_house_not_into_it", !laid.Any(c => c.X == at.x && c.Y == at.y), $"{at.x},{at.y}, {world.At(at.x, at.y).T}");
        yield return Look("house", x + 7, y + 4);
        yield return Look("garage", x + 2, y + 6, CloseBack, FromNorth);

        int wallsWere = Walls();
        yield return Tool("add_wing", new JObject { ["building"] = House, ["width"] = 4, ["depth"] = 4 });
        yield return ApplyDraft("A wing");
        yield return Until(() => Walls() != wallsWere);
        Check("the_wing_goes_in", Walls() > wallsWere, answer);
        Drawn("the_wing_is_drawn", 20f, 11f);
        yield return Look("wing", x + 7, y + 4);

        yield return Tool("set_building", new JObject { ["id"] = House, ["field"] = "roofs.main.shape", ["value"] = "\"shed\"" });
        string shack = Draft();
        yield return Tool("set_building", new JObject { ["id"] = House, ["field"] = "roofs.main.pitch", ["value"] = "[2, 12]", ["draft"] = shack });
        yield return Tool("set_building", new JObject { ["id"] = House, ["field"] = "parts", ["value"] = "[]", ["draft"] = shack });
        yield return Tool("set_building", new JObject { ["id"] = House, ["field"] = "roofs.main.cover", ["value"] = "\"shingle\"", ["draft"] = shack });
        yield return Tool("set_building", new JObject { ["id"] = House, ["field"] = "wall.material", ["value"] = "\"worn-logwall\"", ["draft"] = shack });
        yield return Tool("set_building", new JObject { ["id"] = House, ["field"] = "wall.finish", ["value"] = "{ \"pattern\": \"worn\", \"color\": \"#6b5138\" }", ["draft"] = shack });
        yield return ApplyDraft("A shack");
        yield return Until(() => world.At(x + 4, y).T == "worn-logwall");
        Check("the_shack_goes_in", world.At(x + 4, y).T == "worn-logwall", answer);
        yield return Look("shack", x + 7, y + 4);

        yield return RevertAll();
        yield return Until(() => houses.Count == 0);
        Check("revert_takes_it_all_off", houses.Count == 0 && world.At(x + 4, y).T == was, world.At(x + 4, y).T);
        yield return Base();
        yield return Dozen12();
    }

    protected static string ModJson(int x, int y, string stucco = Stucco)
    {
        string row = "{ \"at\": [" + x + ", " + y + "], \"style\": \"southwest\", \"seed\": 41, \"storeys\": 1, \"fill\": \"hollow\"," +
            " \"floor\": { \"kind\": \"slab\", \"height\": 0.15 }," +
            " \"wall\": { \"material\": \"stucco-wall\", \"height\": 2.9, \"thickness\": 0.3, \"finish\": { \"pattern\": \"smooth\", \"color\": \"#d6b48a\" } }," +
            " \"joins\": { \"a\": [8, 0], \"b\": [28, 0], \"c\": [28, 16], \"d\": [18, 16], \"e\": [18, 14], \"f\": [14, 14], \"g\": [14, 16], \"h\": [8, 16], \"i\": [0, 16], \"j\": [0, 8], \"k\": [8, 8] }," +
            " \"walls\": {" +
            " \"back\": { \"from\": \"a\", \"to\": \"b\", \"openings\": [{ \"kind\": \"wide-window\", \"at\": 4 }, { \"kind\": \"window\", \"at\": 14 }] }," +
            " \"east\": { \"from\": \"b\", \"to\": \"c\", \"openings\": [{ \"kind\": \"window\", \"at\": 5 }, { \"kind\": \"window\", \"at\": 11 }] }," +
            " \"front-e\": { \"from\": \"c\", \"to\": \"d\", \"openings\": [{ \"kind\": \"wide-window\", \"at\": 3 }] }," +
            " \"recess-e\": { \"from\": \"d\", \"to\": \"e\" }, \"recess-w\": { \"from\": \"f\", \"to\": \"g\" }," +
            " \"entry\": { \"from\": \"e\", \"to\": \"f\", \"openings\": [{ \"kind\": \"door\", \"at\": 1, \"finish\": { \"color\": \"#3a6f9a\" } }] }," +
            " \"arch\": { \"from\": \"g\", \"to\": \"d\", \"openings\": [{ \"kind\": \"arch\", \"at\": 0, \"width\": 3, \"shape\": \"round\" }] }," +
            " \"front-w\": { \"from\": \"g\", \"to\": \"h\", \"openings\": [{ \"kind\": \"window\", \"at\": 2 }] }," +
            " \"party\": { \"from\": \"h\", \"to\": \"k\", \"openings\": [{ \"kind\": \"door\", \"at\": 3 }] }," +
            " \"west\": { \"from\": \"k\", \"to\": \"a\", \"openings\": [{ \"kind\": \"window\", \"at\": 3 }] }," +
            " \"garage\": { \"from\": \"h\", \"to\": \"i\", \"openings\": [{ \"kind\": \"double-garage\", \"at\": 1 }] }," +
            " \"g-west\": { \"from\": \"i\", \"to\": \"j\" }, \"g-back\": { \"from\": \"j\", \"to\": \"k\" } }," +
            " \"roofs\": { \"main\": { \"over\": [\"a\", \"b\", \"c\", \"i\", \"j\", \"k\"], \"shape\": \"hip\", \"pitch\": [4, 12], \"overhang\": 0.45, \"cover\": \"clay-tile\" } }," +
            " \"parts\": [{ \"kind\": \"chimney\", \"on\": \"east\", \"at\": 8 }] }";
        return "{ \"terrain\": { \"stucco-wall\": " + stucco + ", \"clay-tile\": " + Tile + " }, \"buildings\": { \"styles\": { \"southwest\": " + Style + " } }," +
            " \"world\": { \"buildings\": { \"" + House + "\": " + row + " } } }";
    }

    // Calls a tool over HTTP on a thread of its own: the desk needs this main thread free to answer.
    protected IEnumerator Tool(string name, JObject arguments)
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JObject { ["name"] = name, ["arguments"] = arguments },
        }.ToString();
        string url = RulesMcpServer.Url;
        var call = Task.Run(async () =>
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var reply = await Http.PostAsync(url, content);
            return await reply.Content.ReadAsStringAsync();
        });
        while (!call.IsCompleted) yield return null;
        answer = call.IsFaulted ? call.Exception?.GetBaseException().Message : Text(call.Result);
    }

    static string Text(string reply)
    {
        var json = JObject.Parse(reply);
        return json["error"]?.ToString() ?? string.Join("\n", (json["result"]?["content"] as JArray ?? new JArray()).Select(c => (string)c["text"]));
    }

    string Draft() => Regex.Match(answer ?? "", @"[Dd]raft:? ""?(\w[\w-]*)").Groups[1].Value;

    IEnumerator ApplyDraft(string title)
    {
        yield return Tool("apply", new JObject { ["mod"] = Draft(), ["title"] = title });
    }

    protected static IEnumerator Until(System.Func<bool> done)
    {
        for (float t = 0f; t < MostWait && !done(); t += Time.unscaledDeltaTime) yield return null;
    }

    int Walls() => (grid.World.BuildingRowOf(House)?.Json["walls"] as JObject)?.Count ?? 0;

    void Drawn(string name, float wide, float deep)
    {
        var root = houses.Root(House);
        var bounds = new Bounds(root.transform.position, Vector3.zero);
        int vertices = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            bounds.Encapsulate(r.bounds);
            vertices += r.GetComponent<MeshFilter>().sharedMesh.vertexCount;
        }
        Check(name, bounds.size.x >= wide && bounds.size.z >= deep && bounds.size.y > 2.5f && bounds.size.y < 12f, $"{bounds.size}, {vertices} vertices");
    }

    int Vertices(string id) => houses.Root(id) == null ? 0 : houses.Root(id).GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.vertexCount);

    Vector3 Size(string id) => houses.Root(id) == null ? Vector3.zero : houses.Root(id).GetComponentsInChildren<MeshRenderer>().Aggregate(new Bounds(houses.Root(id).transform.position, Vector3.zero), (b, r) =>
    {
        b.Encapsulate(r.bounds);
        return b;
    }).size;

    protected void Stand(int x, int y)
    {
        var to = grid.CellCenter(x, y);
        to.y = grid.GroundAt(to) + 0.1f;
        bool hasBody = caster.TryGetComponent<CharacterController>(out var body);
        if (hasBody) body.enabled = false;
        caster.transform.position = to;
        if (hasBody) body.enabled = true;
    }

    // Frames of a building at 720p, the camera swung round it, for a clip: they're what proves it shows.
    // The probe flies the camera itself and renders off screen, so this needs no window.
    IEnumerator Look(string label, int x, int y, float back = ViewBack, float from = 0f)
    {
        if (string.IsNullOrEmpty(ScreenshotDir)) yield break;
        string folder = Path.Combine(ScreenshotDir, label);
        Directory.CreateDirectory(folder);
        var aim = grid.CellCenter(x, y);
        aim.y = grid.GroundAt(aim);
        var cam = Camera.main;
        bool flown = cam.TryGetComponent<CinemachineBrain>(out var brain);
        if (flown) brain.enabled = false;
        var target = new RenderTexture(ClipWidth, ClipHeight, 24);
        var page = new Texture2D(ClipWidth, ClipHeight, TextureFormat.RGB24, false);
        for (int f = 0; f < ClipFrames; f++)
        {
            float turn = from + Mathf.Lerp(-ClipSwing, ClipSwing, f / (ClipFrames - 1f));
            cam.transform.position = aim + Quaternion.Euler(0f, turn, 0f) * new Vector3(0f, ViewHigh * back / ViewBack, -back);
            cam.transform.LookAt(aim + Vector3.up * 2f);
            cam.targetTexture = target;
            cam.Render();
            cam.targetTexture = null;
            RenderTexture.active = target;
            page.ReadPixels(new Rect(0, 0, ClipWidth, ClipHeight), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(folder, $"frame_{f:00000}.jpg"), page.EncodeToJPG(ClipQuality));
            yield return null;
        }
        if (flown) brain.enabled = true;
        Destroy(page);
        Destroy(target);
    }

    protected IEnumerator RevertAll()
    {
        yield return Tool("whats_on", new JObject());
        foreach (var id in Regex.Matches(answer ?? "", @"^\d+\. .*\(([^)\n]+)\), added by", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value).Reverse().ToList())
            yield return Tool("revert", new JObject { ["mod"] = id });
    }

    // An edited cabin keeps its scene model; an edited village house gives way, and comes back.
    IEnumerator Base()
    {
        yield return Tool("add_wing", new JObject { ["building"] = "cabin#2", ["width"] = 3, ["depth"] = 3 });
        Check("a_cabin_takes_a_wing", !string.IsNullOrEmpty(Draft()), answer);
        yield return ApplyDraft("Cabin wing");
        yield return Until(() => houses.Root("cabin#2") != null);
        Check("a_cabin_stays_as_it_was_with_the_wing_beside_it", houses.Root("cabin#2") != null && houses.Hidden("cabin#2") == null, answer);
        Check("the_cabin_wing_shows", Vertices("cabin#2") > 0, $"{Vertices("cabin#2")} vertices, {Size("cabin#2")}");
        var cabin = grid.World.BuildingRowOf("cabin#2");
        yield return Look("cabin-wing", cabin.X + 3, cabin.Y, CloseBack, FromNorth);
        var row = grid.World.BuildingsNow.First(r => !r.Id.StartsWith("cabin", System.StringComparison.Ordinal));
        yield return Look("village-before", row.X + 3, row.Y + 3, CloseBack);
        yield return Tool("set_building", new JObject { ["id"] = row.Id, ["field"] = "wall.finish", ["value"] = "{ \"color\": \"#a04040\" }" });
        yield return ApplyDraft("Red house");
        yield return Until(() => houses.Root(row.Id) != null);
        var hidden = houses.Hidden(row.Id);
        Check("an_edited_village_house_replaces_the_scene_one", hidden != null && !hidden.gameObject.activeSelf, answer);
        Check("its_old_smoke_goes_and_its_chimney_smokes", hidden != null && !hidden.GetComponentsInChildren<ParticleSystem>(true).Any(p => p.gameObject.activeInHierarchy) && houses.Root(row.Id).GetComponentInChildren<ParticleSystem>() != null, answer);
        yield return Look("village-edited", row.X + 3, row.Y + 3, CloseBack);
        yield return RevertAll();
        yield return Until(() => houses.Count == 0);
        Check("reverting_brings_it_back", hidden != null && hidden.gameObject.activeSelf && houses.Count == 0);
        yield return Look("village-after", row.X + 3, row.Y + 3, CloseBack);
    }

    // Frame time with a dozen more houses than before. Hidden Editor numbers: for comparison only.
    IEnumerator Dozen12()
    {
        yield return Sample("frame_ms_before");
        for (int i = 0; i < Dozen; i++)
        {
            yield return Tool("find_site", new JObject { ["width"] = 6, ["depth"] = 5, ["near"] = "lake", ["within"] = 64 });
            var site = Regex.Match(answer ?? "", @"\[(\d+), ?(\d+)\]");
            if (!site.Success) break;
            yield return Tool("add_building", new JObject { ["at"] = site.Value, ["width"] = 6, ["depth"] = 5, ["id"] = $"row-probe#{i + 1}" });
            int count = houses.Count;
            yield return ApplyDraft("House " + i);
            yield return Until(() => houses.Count > count);
        }
        Metric("houses_drawn", houses.Count);
        Check("a_dozen_stand", houses.Count == Dozen, answer);
        yield return Sample("frame_ms_after");
        yield return RevertAll();
        yield return Until(() => houses.Count == 0);
    }

    protected IEnumerator Sample(string name)
    {
        float t = 0f;
        for (int i = 0; i < FrameSample; i++)
        {
            yield return null;
            t += Time.unscaledDeltaTime;
        }
        Metric(name, t / FrameSample * 1000f);
    }
}
