using System.Linq;
using Chem;
using UnityEngine;

// What the player has in the Hold between runs (docs/design/the-hold.md, phase 5): a page in the Esc menu's Game menu with their
// points, what's unlocked, what's next and how many points away, and a run's end on the HUD: the wave reached, the points earned
// and anything newly unlocked.
public partial class ProgressKeeper
{
    const int MenuOrder = 40;
    const string PageId = "hold-progress";
    const float SummarySeconds = 30f, SummaryWidth = 620f, SummaryTop = 170f;

    bool showPage;
    bool sawRun;
    int pointsBefore, unlocksBefore;
    string summary = "";
    float summaryAt = float.NegativeInfinity;

    /// <summary>The last run's end as the HUD shows it; empty before one ends (the probe).</summary>
    public string Summary => summary;

    void AddPage()
    {
        PauseMenu.AddSection(DrawInMenu, MenuOrder, PauseMenu.Submenu.Game);
        PauseMenu.AddPage(PageId, new PauseMenu.Page { Title = "The Hold: your progress", Draw = DrawPage });
    }

    void RemovePage()
    {
        PauseMenu.RemoveSection(DrawInMenu);
        PauseMenu.RemovePage(PageId);
    }

    void DrawInMenu()
    {
        if (grid != null && grid.World?.HoldSite != null && PauseMenu.Button("The Hold: your progress")) showPage = true;
    }

    /// <summary>The page's lines: points, then every unlock with whether it's yours or how far away (the probe too).</summary>
    public string PageText()
    {
        var world = grid != null ? grid.World : null;
        var caster = EventLog.TrackedCaster;
        if (world?.HoldSite == null || caster == null) return "";
        string me = caster.CastPlayer;
        var lines = new System.Text.StringBuilder();
        lines.Append(FromTheSave ? $"Points: {world.PointsOf(me) ?? file.Points}" : $"A dev build: everything is unlocked. F7 plays your save ({file.Points} points)");
        if (world.NextUnlock(me) is { } next) lines.Append($"\nNext: {next.Name}, {Away(world.PointsAway(me, next))} away");
        else lines.Append("\nEverything is unlocked");
        foreach (var row in world.Rules.Sim.Hold.Unlocks)
        {
            int away = world.PointsAway(me, row);
            lines.Append($"\n{(away == 0 ? "✓" : "  ")} {row.Name} ({row.Kind}){(away == 0 ? "" : $": {Away(away)} away")}");
        }
        lines.Append($"\nEach wave you survive is {Away(world.Rules.Sim.Hold.PointsPerWave)}. A moment earns none.");
        return lines.ToString();
    }

    static string Away(int points) => $"{points} {(points == 1 ? "point" : "points")}";

    void DrawPage()
    {
        foreach (var line in PageText().Split('\n')) GUILayout.Label(line);
        if (HoldMoments.Offered && PauseMenu.Button(FromTheSave ? "Play with everything unlocked (F7)" : "Play with your own unlocks (F7)")) PlayFromTheSave(!fromTheSave);
    }

    // A run starting and ending, from what the core says: the end's summary is worked out once, as it happens.
    void WatchRun(World world, string me)
    {
        if (showPage)
        {
            showPage = false;
            PauseMenu.Instance.ShowPage(PageId);
        }
        if (world.HoldRunning && !sawRun)
        {
            sawRun = true;
            pointsBefore = world.PointsOf(me) ?? World.AllPoints;
            unlocksBefore = world.UnlocksOf(me).Count();
            summary = "";
        }
        else if (!world.HoldRunning && sawRun)
        {
            sawRun = false;
            summary = EndText(world, me);
            summaryAt = Time.unscaledTime;
        }
    }

    string EndText(World world, string me)
    {
        var h = world.Hold;
        if (h == null) return "";
        var lines = new System.Text.StringBuilder($"The keep fell in wave {h.Wave}: you held {h.Wave - 1} {(h.Wave - 1 == 1 ? "wave" : "waves")}");
        int earned = (world.PointsOf(me) ?? World.AllPoints) - pointsBefore;
        if (h.Moment != null) lines.Append("\nA moment earns no points");
        else if (!FromTheSave) lines.Append("\nA dev build has everything: F7 plays your save and earns into it");
        else lines.Append($"\n+{Away(earned)}: {world.PointsOf(me)} in all");
        foreach (var row in world.UnlocksOf(me).Skip(unlocksBefore)) lines.Append($"\nUnlocked: {row.Name} ({row.Kind})");
        if (FromTheSave && world.NextUnlock(me) is { } next) lines.Append($"\nNext: {next.Name}, {Away(world.PointsAway(me, next))} away");
        if (world.RunOf(me) is { Picks: { Count: > 0 } picks })
            lines.Append("\nThis run: ").Append(string.Join(", ", picks.Select(id => world.Rules.Sim.Hold.Upgrade(id)?.Name ?? id)));
        lines.Append("\nStand on the plate to go again");
        return lines.ToString();
    }

    void DrawSummary(World world)
    {
        if (summary.Length == 0 || world.HoldRunning || Time.unscaledTime - summaryAt > SummarySeconds) return;
        float x = HudScale.Width / 2f - SummaryWidth / 2f;
        float height = small.CalcHeight(new GUIContent(summary), SummaryWidth - 24f) + 20f;
        var before = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(x, SummaryTop, SummaryWidth, height), Texture2D.whiteTexture);
        GUI.color = before;
        var lines = summary.Split('\n');
        GUI.Label(new Rect(x, SummaryTop + 6f, SummaryWidth, 30f), $"<b>{lines[0]}</b>", title);
        GUI.Label(new Rect(x + 12f, SummaryTop + 38f, SummaryWidth - 24f, height - 40f), string.Join("\n", lines.Skip(1)), small);
    }
}
