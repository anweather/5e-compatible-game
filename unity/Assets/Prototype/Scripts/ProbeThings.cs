using System.Collections;
using System.IO;
using System.Linq;
using Chem;
using Unity.Cinemachine;
using UnityEngine;

// A mod's new kinds of thing in play (docs/RULES_MCP.md "Things"): each stands in the
// world dressed as an animal, is carried and found, and goes with its mod. The golden
// things here are the probes' own: named content never goes in the code or rules (J-055).

/// <summary>The mod the things probes switch on: a carried kind and a pushed one, at two cells.</summary>
public static class ProbeThingsMod
{
    public const string Id = "probe-things", Title = "Probe things", Find = "probe-find", Stump = "probe-stump";

    public static string Json(Vector2Int item, Vector2Int prop) => $@"{{ ""title"": ""{Title}"", ""patch"": {{
        ""items"": {{ ""{Find}"": {{ ""name"": ""Golden squirrel"", ""material"": ""flesh"", ""mass"": 0.5,
            ""look"": {{ ""family"": ""creature"", ""species"": ""squirrel"", ""color"": [1, 0.78, 0.15], ""glow"": 0.4, ""scale"": 2 }} }} }},
        ""entities"": {{ ""{Stump}"": {{ ""name"": ""Blue deer"", ""material"": ""flesh"", ""hp"": 20, ""mass"": 40,
            ""look"": {{ ""family"": ""creature"", ""species"": ""deer"", ""color"": [0.2, 0.4, 1] }} }} }},
        ""world"": {{ ""add"": [
            {{ ""kind"": ""{Find}"", ""cell"": [{item.x}, {item.y}], ""rot"": 135 }},
            {{ ""kind"": ""{Stump}"", ""cell"": [{prop.x}, {prop.y}] }} ] }} }} }}";

    /// <summary>Two open cells near a place, by the same check a peer makes at the switch.</summary>
    public static (Vector2Int item, Vector2Int prop) Near(ChemGrid grid, Vector3 at)
    {
        var from = grid.CellAt(at);
        var open = Enumerable.Range(3, 20)
            .SelectMany(r => new[] { new Vector2Int(r, 0), new Vector2Int(-r, 0), new Vector2Int(0, r), new Vector2Int(0, -r), new Vector2Int(r, r), new Vector2Int(-r, -r) })
            .Select(o => from + o)
            .Where(c => grid.World.CellProblem(c.x, c.y) == null)
            .ToList();
        return (open[0], open.First(c => (c - open[0]).sqrMagnitude >= 9));
    }
}

/// <summary>Solo: a mod's things spawn dressed, rest on the ground, are found once, and go with the mod, even out of a hand.</summary>
[DefaultExecutionOrder(200)] // a picture is taken after ThingBodies has drawn
public class ModThingProbe : ProbeScenario
{
    const int ShotWidth = 1280, ShotHeight = 720, ShotQuality = 85;
    const float Settle = 1.5f;

    RuleSwitcher switcher;
    ThingSpawner spawner;
    ThingBodies bodies;
    ItemHolder hands;
    string snapTo;
    Vector3 snapFrom, snapAt;

    protected override IEnumerator Run()
    {
        switcher = grid.GetComponent<RuleSwitcher>();
        spawner = FindAnyObjectByType<ThingSpawner>();
        bodies = spawner.GetComponent<ThingBodies>();
        hands = wizard.GetComponent<ItemHolder>();
        int things = World.ThingsNow.Count;
        var (itemCell, propCell) = ProbeThingsMod.Near(grid, wizard.transform.position);
        Metric("item_cell_x", itemCell.x);
        Metric("item_cell_y", itemCell.y);

        string refused = switcher.Ask(RoomCommand.Apply(switcher.Me, ProbeThingsMod.Id, ProbeThingsMod.Json(itemCell, propCell), ProbeThingsMod.Title));
        yield return Wait(Settle);
        Check("mod_on", World.Room.Mods.Any(m => m.Id == ProbeThingsMod.Id), refused);
        var item = Live(ProbeThingsMod.Find);
        var prop = Live(ProbeThingsMod.Stump);
        Check("both_spawned", item != null && prop != null && World.ThingsNow.Count == things + 2, $"{World.ThingsNow.Count} things, {things} before");
        if (item == null || prop == null) yield break;
        Check("both_dressed", bodies.Count == 2, $"{bodies.Count} dressed");
        Check("the_carried_one_is_an_item", item.GetComponent<Carryable>() != null && prop.GetComponent<Carryable>() == null);
        Check("the_item_on_its_cell", grid.CellAt(item.transform.position) == itemCell, $"{grid.CellAt(item.transform.position)}");
        Rests("item", item);
        Rests("prop", prop);
        var size = item.transform.localScale;
        Metric("squirrel_x", size.x);
        Metric("squirrel_y", size.y);
        Metric("squirrel_z", size.z);
        Metric("deer_y", prop.transform.localScale.y);
        Check("a_deer_stands_taller_than_a_squirrel", prop.transform.localScale.y > size.y, $"{prop.transform.localScale.y:0.00} m against {size.y:0.00} m");
        Check("the_box_draws_nothing", !item.GetComponent<Renderer>().enabled);
        yield return Snap("standing", item.transform.position);
        yield return Snap("prop", prop.transform.position);

        // Found once: the first pick-up logs it, the second doesn't.
        wizard.transform.position = item.transform.position + Vector3.right;
        int tick = World.Tick;
        var carried = item.GetComponent<Carryable>();
        hands.PickUp(carried);
        yield return Wait(Settle);
        Check("in_the_hand", hands.Held == carried);
        Check("the_find_logged", LogSince(tick).Contains("Player 1 found the golden squirrel"), string.Join(" | ", LogSince(tick)));
        Check("the_find_in_the_room", World.Room.Found.Count == 1, World.RoomText());
        yield return Snap("in-hand", hands.HandPosition);
        hands.Drop();
        yield return Wait(Settle);
        tick = World.Tick;
        hands.PickUp(carried);
        yield return Wait(Settle);
        Check("found_only_once", !LogSince(tick).Any(l => l.Contains("found the golden squirrel")) && World.Room.Found.Count == 1);

        // Reverting takes it out of the hand and out of the world.
        switcher.Ask(RoomCommand.Revert(switcher.Me, ProbeThingsMod.Id));
        yield return Wait(Settle);
        Check("gone_from_the_hand", hands.Held == null);
        Check("gone_from_the_world", Live(ProbeThingsMod.Find) == null && Live(ProbeThingsMod.Stump) == null && World.ThingsNow.Count == things, $"{World.ThingsNow.Count} things");
        Check("no_body_left", bodies.Count == 0, $"{bodies.Count} dressed");
        Check("the_find_went_with_it", World.Room.Found.Count == 0, World.RoomText());

        // And it's only composing: the same mod brings the same things back.
        switcher.Ask(RoomCommand.Apply(switcher.Me, ProbeThingsMod.Id, ProbeThingsMod.Json(itemCell, propCell), ProbeThingsMod.Title));
        yield return Wait(Settle);
        Check("back_when_on_again", Live(ProbeThingsMod.Find) != null && bodies.Count == 2, $"{bodies.Count} dressed");
        switcher.Ask(RoomCommand.Revert(switcher.Me, ProbeThingsMod.Id));
        yield return Wait(Settle);
    }

    PhysicsProp Live(string kind) => spawner.Live.Where(t => t.prop.kind == kind).Select(t => t.prop).FirstOrDefault();

    void Rests(string what, PhysicsProp prop)
    {
        var ground = grid.GroundAt(prop.transform.position);
        float gap = prop.GetComponent<Collider>().bounds.min.y - ground;
        Metric(what + "_above_ground", gap);
        Check(what + "_rests_on_the_ground", Mathf.Abs(gap) < 0.25f, $"{gap:0.00} m");
    }

    // One picture from a little way off, the Game view being whatever shape a hidden Editor gives it.
    IEnumerator Snap(string label, Vector3 at)
    {
        if (string.IsNullOrEmpty(ScreenshotDir)) yield break;
        Directory.CreateDirectory(ScreenshotDir);
        snapAt = at;
        snapFrom = at + new Vector3(1.6f, 1.1f, -2.2f);
        snapTo = Path.Combine(ScreenshotDir, label + ".jpg");
        while (snapTo != null) yield return null;
    }

    void LateUpdate()
    {
        if (snapTo == null) return;
        var cam = Camera.main;
        bool flown = cam.TryGetComponent<CinemachineBrain>(out var brain) && brain.enabled;
        if (flown) brain.enabled = false;
        var target = new RenderTexture(ShotWidth, ShotHeight, 24);
        var page = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
        cam.transform.position = snapFrom;
        cam.transform.LookAt(snapAt);
        cam.targetTexture = target;
        cam.Render();
        cam.targetTexture = null;
        RenderTexture.active = target;
        page.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
        RenderTexture.active = null;
        File.WriteAllBytes(snapTo, page.EncodeToJPG(ShotQuality));
        if (flown) brain.enabled = true;
        Destroy(page);
        Destroy(target);
        snapTo = null;
    }
}

/// <summary>A mod's things across peers: the client adopts them dressed, finds one, a late joiner gets them, and a revert takes one out of the client's hand.</summary>
public class RoomThingProbe : RoomProbe
{
    const float Settle = 1f, CatchUp = 20f;

    protected override ChangeMode Mode => ChangeMode.Open;
    protected override bool StartsWithAMod => false;

    protected override IEnumerator Steps()
    {
        var at = NetworkManagerWizard().position;
        var (item, prop) = ProbeThingsMod.Near(grid, at);
        Switcher.Ask(RoomCommand.Apply(Switcher.Me, ProbeThingsMod.Id, ProbeThingsMod.Json(item, prop), ProbeThingsMod.Title));
        yield return InStep();
        Metric("client_caught_up_seconds", caughtUp);
        yield return Agree("things_in", $"Player 1 switched on {ProbeThingsMod.Title}");
        if (Theirs == null) yield break;
        Check("host_has_both", Mine.things.Split(' ').Length == 2 && Mine.dressed == 2, $"{Mine.things}, {Mine.dressed} dressed");
        Check("client_has_the_same", Theirs.things == Mine.things && Theirs.dressed == 2, $"{Theirs.things}, {Theirs.dressed} dressed; here {Mine.things}");

        yield return Ask("grab", ProbeThingsMod.Find);
        yield return InStep();
        yield return Agree("found", "Player 2 found the golden squirrel");
        if (Theirs == null) yield break;
        Check("in_the_clients_hand", Theirs.held == ProbeThingsMod.Find, Theirs.held);

        // Leaving drops it; the late joiner's copy arrives before its rules do, so it waits for them.
        HashesFrom = Net.NetTick;
        yield return Ask("rejoin");
        yield return Wait(Settle);
        yield return InStep();
        if (Theirs == null) yield break;
        Check("a_late_joiner_has_them_dressed", Theirs.things == Mine.things && Theirs.dressed == 2, $"{Theirs.things}, {Theirs.dressed} dressed; here {Mine.things}");
        Check("a_late_joiner_knows_the_find", Theirs.roomText == World.RoomText(), Theirs.roomText);

        yield return Ask("grab", ProbeThingsMod.Find);
        Check("in_hand_for_the_revert", Theirs != null && Theirs.held == ProbeThingsMod.Find, Theirs?.held);
        Switcher.Ask(RoomCommand.Revert(Switcher.Me, ProbeThingsMod.Id));
        yield return InStep();
        yield return Agree("things_out", $"Player 1 switched off {ProbeThingsMod.Title}");
        if (Theirs == null) yield break;
        Check("out_of_the_clients_hand", string.IsNullOrEmpty(Theirs.held), Theirs.held);
        Check("gone_from_both", Mine.things == "" && Theirs.things == "" && Mine.dressed == 0 && Theirs.dressed == 0, $"{Mine.things}/{Theirs.things}, {Mine.dressed}/{Theirs.dressed} dressed");
    }

    float caughtUp;

    // A virtual player in the background runs behind: this waits until it holds what this side holds.
    IEnumerator InStep()
    {
        for (caughtUp = 0f; caughtUp < CatchUp; caughtUp += Settle)
        {
            yield return Ask("report");
            if (Theirs == null || (Theirs.roomText == World.RoomText() && Theirs.rules == World.Rules.Fingerprint)) yield break;
            yield return Wait(Settle);
        }
    }

    static Transform NetworkManagerWizard() => Unity.Netcode.NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().transform;
}
