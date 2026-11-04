using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Chem;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

// The probe's triggerview scenario (docs/design/events-and-placing.md 2c): two step triggers through the rules
// MCP, as Claude's add_trigger and apply write them, in an Open room with the virtual player joined: a Fire Bolt
// 2 cells behind each step (every 6 ticks) and a Meteor 2 cells ahead after 6 ticks (every 12), for each player.
// Each wizard walks a row of cells, moved cell by cell. Each side counts the shows it plays (TriggerShowCount):
// one projectile per bolt, one meteor per meteor, falling for the meteor's delay, and none from the core's small
// falling star. Then a late joiner sees no stale shows; the host walks a long loop at a step a tick and a second
// late joiner replays every cast from the stands its strike history kept (#83); and once reverted, steps fire nothing.
public class TriggerViewProbe : RoomProbe
{
    const string Title = "Steps of fire", Slug = "steps-of-fire";
    const string Bolts = "[{\\\"cast\\\":\\\"fire-bolt\\\",\\\"at\\\":\\\"behind\\\",\\\"by\\\":2}]";
    const string Meteors = "[{\\\"cast\\\":\\\"meteor\\\",\\\"at\\\":\\\"ahead\\\",\\\"by\\\":2,\\\"delay\\\":6}]";
    const int WalkSteps = 12, BackSteps = 6, MeteorDelay = 6;
    /// <summary>Seconds between a walk's steps: 3 ticks, a run.</summary>
    public const float StepSeconds = 0.5f;
    const float Settle = 3f, FrameSeconds = 1f / 15f, ReplaySeconds = 100f;
    /// <summary>The long walk: a loop of cells (east, south, west, north), a step a tick.</summary>
    static readonly (int dx, int dy, int steps)[] Loop = { (1, 0, 18), (0, 1, 8), (-1, 0, 18), (0, -1, 8) };
    const float LongStepSeconds = 1f / 6f;
    const int FilmWidth = 1280, FilmHeight = 720;

    protected override ChangeMode Mode => ChangeMode.Open;
    protected override bool StartsWithAMod => false;

    TriggerShowCount count;
    GameObject me;
    bool filming;
    int frames;

    protected override void OnDestroy()
    {
        if (count != null) Destroy(count.gameObject);
        base.OnDestroy();
    }

    /// <summary>Walks a wizard a cell at a time along (dx, dy), as its stand sends each new cell (WizardStrike), facing the way.</summary>
    public static IEnumerator Walk(ChemGrid grid, GameObject wizard, int dx, int dy, int steps, float seconds = StepSeconds)
    {
        var body = wizard.GetComponent<CharacterController>();
        var vitals = wizard.GetComponent<Vitals>();
        var at = grid.CellAt(wizard.transform.position);
        for (int i = 0; i < steps; i++)
        {
            var to = new Vector2Int(at.x + dx, at.y + dy);
            var way = grid.CellCenter(to.x, to.y) - grid.CellCenter(at.x, at.y);
            way.y = 0f;
            body.enabled = false;
            wizard.transform.SetPositionAndRotation(grid.CellCenter(to.x, to.y) + Vector3.up * (grid.SurfaceY(to.x, to.y) + 0.05f), Quaternion.LookRotation(way));
            body.enabled = true;
            if (vitals != null) vitals.Refill();
            at = to;
            yield return new WaitForSecondsRealtime(seconds);
        }
    }

    /// <summary>The triggered casts in this world after the Nth, as "n:spell@x,y", in order.</summary>
    public static List<string> CastsAfter(World world, int n) => world.TriggeredAfter(n).Select(c => $"{c.N}:{c.Spell}@{c.X},{c.Y}").ToList();

    /// <summary>The shows this side played (ChemGrid.TriggeredShown), as "n:spell@x,y".</summary>
    public static List<string> Shown(ChemGrid grid) => grid.TriggeredShown.Select(s => $"{s.n}:{s.spell}@{s.x},{s.y}").ToList();

    /// <summary>The step lines in this side's log, distinct and sorted.</summary>
    public static string StepLines(World world) =>
        string.Join("|", world.Log.Select(l => l.Text).Where(t => t.Contains("'s step calls down")).Distinct().OrderBy(t => t, StringComparer.Ordinal));

    protected override IEnumerator Steps()
    {
        Check("serving", RulesMcpServer.Url != null, RulesMcpServer.ConnectLine);
        if (RulesMcpServer.Url == null) yield break;
        count = TriggerShowCount.Start();
        me = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().gameObject;

        // The two triggers in one draft, then on for the room.
        var bolt = RoomMcpProbe.Post(RoomMcpProbe.Tool("add_trigger", $"{{\"id\":\"bolt-steps\",\"who\":\"each\",\"every\":6,\"do\":\"{Bolts}\"}}"));
        yield return RoomMcpProbe.Until(bolt);
        string body = bolt.Result.body ?? "";
        int at = body.IndexOf("Draft ", StringComparison.Ordinal);
        Check("add_trigger_drafts", at >= 0, body);
        if (at < 0) yield break;
        string draft = body.Substring(at + 6).Split(':')[0];
        var meteor = RoomMcpProbe.Post(RoomMcpProbe.Tool("add_trigger", $"{{\"id\":\"meteor-steps\",\"who\":\"each\",\"every\":12,\"do\":\"{Meteors}\",\"draft\":\"{draft}\"}}"));
        yield return RoomMcpProbe.Until(meteor);
        Check("second_trigger_in_the_draft", (meteor.Result.body ?? "").Contains("meteor-steps fires"), meteor.Result.body);
        var on = RoomMcpProbe.Post(RoomMcpProbe.Tool("apply", $"{{\"mod\":\"{draft}\",\"title\":\"{Title}\"}}"));
        yield return RoomMcpProbe.Until(on);
        yield return Agree("triggers_on", $"Player 1 switched on {Title}");
        if (Theirs == null) yield break;

        // The host walks east: its bolts and meteors show on both.
        StartCoroutine(Film());
        yield return Walked("host", me, "Player 1", () => Walk(grid, me, 1, 0, WalkSteps));
        if (Theirs == null) yield break;
        // The virtual player walks east too.
        yield return Walked("client", null, "Player 2", null);
        if (Theirs == null) yield break;
        filming = false;
        CompareHashes("walks_stayed_in_step");

        // A late joiner, a few seconds after the last firing: its replay fires them all again, and plays none of their shows.
        HashesFrom = Net.NetTick;
        yield return Ask("rejoin");
        yield return Wait(Settle);
        yield return Reach("late_joiner", Net.NetTick, ReplaySeconds);
        if (Theirs == null) yield break;
        Metric("late_joiner_reach_s", Theirs.reachSeconds);
        Metric("late_joiner_strike_history", Net.StrikeHistorySent);
        Check("late_joiner_replayed_the_casts", Theirs.triggered == World.TriggeredCasts && Theirs.triggered > 0, $"{Theirs.triggered} cast there, {World.TriggeredCasts} here");
        Check("late_joiner_plays_no_old_shows", Theirs.showsSince == 0 && Theirs.projectiles == 0 && Theirs.meteors == 0,
            $"{Theirs.showsSince} shows, {Theirs.projectiles} projectiles, {Theirs.meteors} meteors since it joined");

        yield return LongWalk();
        if (Theirs == null) yield break;

        // Reverted: steps fire nothing, and nothing new shows, on either side.
        var off = RoomMcpProbe.Post(RoomMcpProbe.Tool("revert", $"{{\"mod\":\"{Slug}\"}}"));
        yield return RoomMcpProbe.Until(off);
        yield return Agree("reverted", $"Player 1 switched off {Title}");
        if (Theirs == null) yield break;
        int casts = World.TriggeredCasts, theirShows = Theirs.showsSince, shows = grid.TriggeredShown.Count;
        var (projectiles, meteors) = (count.Projectiles, count.Meteors);
        yield return Walk(grid, me, -1, 0, BackSteps);
        yield return Ask("walk", $"-1,0,{BackSteps}");
        yield return Wait(Settle);
        yield return Reach("after_revert", Net.NetTick);
        if (Theirs == null) yield break;
        Check("reverted_fires_nothing", World.TriggeredCasts == casts && Theirs.triggered == casts, $"{casts} casts, then {World.TriggeredCasts} here and {Theirs.triggered} there");
        Check("reverted_shows_nothing_here", grid.TriggeredShown.Count == shows && count.Projectiles == projectiles && count.Meteors == meteors,
            $"{grid.TriggeredShown.Count - shows} shows, {count.Projectiles - projectiles} projectiles, {count.Meteors - meteors} meteors");
        Check("reverted_shows_nothing_there", Theirs.showsSince == theirShows, $"{Theirs.showsSince - theirShows} shows");
        Metric("film_frames", frames);
    }

    // A long walk with the triggers on, then a late joiner (#83): its strike history keeps the stands its replay needs,
    // not every step, and it still fires every cast the host did, at the same ticks (the checksums carry the firings).
    IEnumerator LongWalk()
    {
        int casts = World.TriggeredCasts;
        foreach (var (dx, dy, steps) in Loop) yield return Walk(grid, me, dx, dy, steps, LongStepSeconds);
        yield return Wait(Settle);
        yield return Reach("long_walk", Net.NetTick);
        if (Theirs == null) yield break;
        CompareHashes("late_joiner_stayed_in_step", HashesFrom);
        Metric("long_walk_casts", World.TriggeredCasts - casts);

        HashesFrom = Net.NetTick;
        yield return Ask("rejoin");
        yield return Wait(Settle);
        yield return Reach("long_walk_late_joiner", Net.NetTick, ReplaySeconds);
        if (Theirs == null) yield break;
        Metric("long_walk_late_joiner_reach_s", Theirs.reachSeconds);
        Metric("long_walk_strike_history", Net.StrikeHistorySent);
        Check("long_walk_late_joiner_replayed_the_casts", Theirs.triggered == World.TriggeredCasts && World.TriggeredCasts > casts,
            $"{Theirs.triggered} cast there, {World.TriggeredCasts} here");
    }

    // One walk (the host's when wizard is given, else the virtual player's) and its checks: every cast it fired
    // plays exactly one show on each side (one projectile per bolt, one meteor per meteor), the meteor falls
    // for its delay, and each side's log names the player.
    IEnumerator Walked(string who, GameObject wizard, string player, Func<IEnumerator> walk)
    {
        yield return Ask("report");
        if (Theirs == null) yield break;
        int from = World.TriggeredCasts, theirProjectiles = Theirs.projectiles, theirMeteors = Theirs.meteors;
        var (projectiles, meteors) = (count.Projectiles, count.Meteors);
        if (wizard != null) yield return walk();
        else yield return Ask("walk", $"1,0,{WalkSteps}");
        yield return Wait(Settle);
        yield return Reach(who + "_walked", Net.NetTick);
        if (Theirs == null) yield break;

        var casts = CastsAfter(World, from);
        int bolts = casts.Count(c => c.Contains(":fire-bolt@")), falls = casts.Count(c => c.Contains(":meteor@"));
        Metric($"{who}_bolts", bolts);
        Metric($"{who}_meteors", falls);
        Check($"{who}_fired_both", bolts > 0 && falls > 0, string.Join(" ", casts));
        var here = Shown(grid).Where(s => casts.Contains(s)).ToList();
        var there = Theirs.shown.Where(s => casts.Contains(s)).ToList();
        Check($"{who}_one_show_per_cast_here", here.SequenceEqual(casts), $"cast {string.Join(" ", casts)}; shown {string.Join(" ", here)}");
        Check($"{who}_one_show_per_cast_there", there.SequenceEqual(casts), $"shown there {string.Join(" ", there)}");
        Check($"{who}_projectile_per_bolt_here", count.Projectiles - projectiles == bolts, $"{count.Projectiles - projectiles} projectiles for {bolts} bolts");
        Check($"{who}_meteor_per_meteor_here", count.Meteors - meteors == falls, $"{count.Meteors - meteors} meteors for {falls}");
        Check($"{who}_projectile_per_bolt_there", Theirs.projectiles - theirProjectiles == bolts, $"{Theirs.projectiles - theirProjectiles} projectiles for {bolts} bolts");
        Check($"{who}_meteor_per_meteor_there", Theirs.meteors - theirMeteors == falls, $"{Theirs.meteors - theirMeteors} meteors for {falls}");
        // A meteor's show falls for its delay (6 ticks, a second), not a cast's 1.8 s; seen from the frames it was out.
        var timed = grid.TriggeredShown.Where(s => s.spell == SpellIds.Meteor && s.n > from).Select(s => s.lands - s.tick).ToList();
        Check($"{who}_meteor_shows_for_its_delay", timed.Count > 0 && timed.All(t => t >= MeteorDelay - 1 && t <= MeteorDelay), string.Join(" ", timed));
        Metric($"{who}_meteor_fall_s_here", count.MeteorSeconds);
        Metric($"{who}_meteor_fall_s_there", Theirs.meteorSeconds);
        Check($"{who}_meteor_falls_a_second_here", count.MeteorSeconds > 0.6f && count.MeteorSeconds < 1.4f, $"{count.MeteorSeconds:0.00} s");
        Check($"{who}_meteor_falls_a_second_there", Theirs.meteorSeconds > 0.6f && Theirs.meteorSeconds < 1.4f, $"{Theirs.meteorSeconds:0.00} s");
        string lines = StepLines(World);
        Check($"{who}_log_names_the_player_here", lines.Contains(player + "'s step calls down Fire Bolt") && lines.Contains(player + "'s step calls down Meteor") && !lines.Contains("solo's") && !lines.Contains("p0's") && !lines.Contains("p1's"), lines);
        Check($"{who}_log_names_the_player_there", Theirs.stepLog.Contains(player + "'s step calls down Meteor"), Theirs.stepLog);
    }

    // Frames from behind and above the host's wizard as it walks, at 720p, rendered off screen (no window needed).
    IEnumerator Film()
    {
        if (string.IsNullOrEmpty(ScreenshotDir)) yield break;
        string folder = Path.Combine(ScreenshotDir, "film");
        Directory.CreateDirectory(folder);
        var cam = Camera.main;
        bool flown = cam.TryGetComponent<CinemachineBrain>(out var brain);
        if (flown) brain.enabled = false;
        var target = new RenderTexture(FilmWidth, FilmHeight, 24);
        var page = new Texture2D(FilmWidth, FilmHeight, TextureFormat.RGB24, false);
        filming = true;
        float next = 0f;
        var eye = me.transform.position + new Vector3(-9f, 9f, -17f);
        while (filming)
        {
            yield return null;
            if (Time.unscaledTime < next) continue;
            next = Time.unscaledTime + FrameSeconds;
            var wizard = me.transform.position;
            eye = Vector3.Lerp(eye, wizard + new Vector3(-9f, 9f, -17f), 0.15f);
            cam.transform.position = eye;
            cam.transform.LookAt(wizard + new Vector3(2f, 1f, 0f));
            cam.targetTexture = target;
            cam.Render();
            cam.targetTexture = null;
            RenderTexture.active = target;
            page.ReadPixels(new Rect(0, 0, FilmWidth, FilmHeight), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(folder, $"frame_{frames++:00000}.jpg"), page.EncodeToJPG(88));
        }
        if (flown) brain.enabled = true;
        Destroy(page);
        Destroy(target);
    }
}

/// <summary>
/// Counts the spell shows that come out on this side: each projectile (SpellProjectile) and each meteor
/// (MeteorFall at a meteor's size) once, and how long the last meteors were out (their fall), for triggerview.
/// </summary>
public class TriggerShowCount : MonoBehaviour
{
    readonly HashSet<int> seen = new();
    readonly Dictionary<int, float> falling = new();
    readonly List<int> landed = new();
    float fallTotal;
    int fallCount;

    public int Projectiles { get; private set; }
    public int Meteors { get; private set; }
    /// <summary>The mean seconds a meteor was out, 0 before any landed.</summary>
    public float MeteorSeconds => fallCount > 0 ? fallTotal / fallCount : 0f;

    public static TriggerShowCount Start()
    {
        var go = new GameObject("Probe show count");
        DontDestroyOnLoad(go);
        return go.AddComponent<TriggerShowCount>();
    }

    void Update()
    {
        foreach (var p in FindObjectsByType<SpellProjectile>(FindObjectsSortMode.None))
            if (seen.Add(p.GetInstanceID())) Projectiles++;
        var now = FindObjectsByType<MeteorFall>(FindObjectsSortMode.None);
        foreach (var m in now)
            if (m.Size >= 1f && seen.Add(m.GetInstanceID()))
            {
                Meteors++;
                falling[m.GetInstanceID()] = Time.unscaledTime;
            }
        // Out of the sky once it's landed (MeteorFall disables itself) or gone.
        landed.Clear();
        foreach (var kv in falling)
            if (!now.Any(m => m.GetInstanceID() == kv.Key && m.enabled)) landed.Add(kv.Key);
        foreach (int id in landed)
        {
            fallTotal += Time.unscaledTime - falling[id];
            fallCount++;
            falling.Remove(id);
        }
    }
}
