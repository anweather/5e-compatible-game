using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// The offer between waves and the run's picks (docs/design/the-hold.md, phase 5). As a wave is beaten the core offers each wizard
// a few upgrades; this shows them with what each does and the time until one is picked for you, as the next wave comes. 1, 2 and 3
// or a click take one; on a pad the d-pad chooses (left and right) and takes (up), its kit buttons off while the offer's open. During
// a run the wizard's picks so far are listed at the side.
public partial class ProgressKeeper
{
    const float OfferWidth = 640f, OfferTop = 170f, CardHeight = 56f, PicksWidth = 360f, PicksTop = 220f;
    static readonly Color Chosen = new(1f, 0.82f, 0.4f), Plain = new(0.85f, 0.85f, 0.85f);

    int choice;
    string offerFor;
    GUIStyle card, title, small;

    void Picks(SpellCaster caster, RunWizard run, World world)
    {
        bool open = run != null && run.Offer.Count > 0 && world.HoldRunning;
        if (caster.controls != null) caster.controls.OfferPad = open;
        if (!open) return;
        string key = string.Join(",", run.Offer);
        if (key != offerFor)
        {
            offerFor = key;
            choice = 0;
        }
        var keys = Keyboard.current;
        if (keys != null)
        {
            var numbers = new[] { keys.digit1Key, keys.digit2Key, keys.digit3Key };
            for (int i = 0; i < numbers.Length && i < run.Offer.Count; i++)
                if (numbers[i].wasPressedThisFrame) Take(caster, run, i);
        }
        var pad = Gamepad.current;
        if (pad == null || PauseMenu.Instance != null && PauseMenu.Instance.Open) return;
        if (pad.dpad.left.wasPressedThisFrame) choice = (choice + run.Offer.Count - 1) % run.Offer.Count;
        if (pad.dpad.right.wasPressedThisFrame) choice = (choice + 1) % run.Offer.Count;
        if (pad.dpad.up.wasPressedThisFrame) Take(caster, run, choice);
    }

    /// <summary>Takes the offer's nth upgrade, as a key, a click or the pad would (the probe too).</summary>
    public bool Take(SpellCaster caster, RunWizard run, int n)
    {
        if (run == null || n < 0 || n >= run.Offer.Count) return false;
        string id = run.Offer[n];
        if (!caster.PickUpgrade(id)) return false;
        var row = grid.World.Rules.Sim.Hold.Upgrade(id);
        if (row != null) EventLog.Add($"{row.Name}: {row.Words()}");
        return true;
    }

    /// <summary>The offer as the player reads it, one line an upgrade; empty with none open (the probe).</summary>
    public string OfferText()
    {
        var world = grid != null ? grid.World : null;
        var caster = EventLog.TrackedCaster;
        var run = caster != null ? world?.RunOf(caster.CastPlayer) : null;
        if (run == null || run.Offer.Count == 0) return "";
        var lines = new System.Text.StringBuilder(OfferTitle(world));
        for (int i = 0; i < run.Offer.Count; i++) lines.Append('\n').Append(CardText(world, run.Offer[i], i));
        return lines.ToString();
    }

    static string OfferTitle(World world)
    {
        string left = world.HoldWarning is { } warning ? $", picked for you in {(warning.ticks + world.Rules.Sim.TicksPerSecond - 1) / world.Rules.Sim.TicksPerSecond} s" : "";
        return $"Wave {world.Hold.Wave - 1} beaten. Pick an upgrade{left}";
    }

    static string CardText(World world, string id, int i)
    {
        var row = world.Rules.Sim.Hold.Upgrade(id);
        return $"{i + 1}. {row?.Name ?? id}: {row?.Words()}";
    }

    /// <summary>The wizard's picks this run, as the side list shows them; empty with none (the probe).</summary>
    public string PicksText(World world, string player)
    {
        var run = world?.RunOf(player);
        if (run == null || run.Picks.Count == 0) return "";
        var lines = new System.Text.StringBuilder("Your upgrades this run");
        foreach (var id in run.Picks)
        {
            var row = world.Rules.Sim.Hold.Upgrade(id);
            lines.Append("\n• ").Append(row?.Name ?? id).Append(": ").Append(row?.Words());
        }
        return lines.ToString();
    }

    void Styles()
    {
        card ??= new GUIStyle(GUI.skin.button) { fontSize = 18, alignment = TextAnchor.MiddleLeft, wordWrap = true, richText = true, padding = new RectOffset(14, 14, 6, 6) };
        title ??= new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, richText = true };
        small ??= new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = true };
    }

    void OnGUI()
    {
        var world = grid.World;
        var caster = EventLog.TrackedCaster;
        if (world?.HoldSite == null || caster == null || !EventLog.HudVisible) return;
        HudScale.Apply();
        Styles();
        DrawSummary(world);
        if (!world.HoldRunning) return;
        string me = caster.CastPlayer;
        var run = world.RunOf(me);
        DrawPicks(world, me);
        if (run == null || run.Offer.Count == 0) return;
        DrawOffer(world, caster, run);
    }

    void DrawOffer(World world, SpellCaster caster, RunWizard run)
    {
        float x = HudScale.Width / 2f - OfferWidth / 2f;
        var back = new Rect(x - 12f, OfferTop - 8f, OfferWidth + 24f, 44f + run.Offer.Count * (CardHeight + 6f) + 34f);
        var before = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        GUI.color = before;
        GUI.Label(new Rect(x, OfferTop, OfferWidth, 34f), $"<b>{OfferTitle(world)}</b>", title);
        bool pad = caster.controls != null && caster.controls.UsingGamepad;
        for (int i = 0; i < run.Offer.Count; i++)
        {
            var row = world.Rules.Sim.Hold.Upgrade(run.Offer[i]);
            var rect = new Rect(x, OfferTop + 40f + i * (CardHeight + 6f), OfferWidth, CardHeight);
            GUI.color = pad && i == choice ? Chosen : Plain;
            string mark = pad && i == choice ? "▶ " : $"{i + 1}. ";
            if (GUI.Button(rect, $"{mark}<b>{row?.Name ?? run.Offer[i]}</b>   {row?.Words()}", card)) Take(caster, run, i);
            GUI.color = before;
        }
        string hint = pad ? "D-pad left and right to choose, up to take it" : "1, 2 or 3, or click one";
        GUI.Label(new Rect(x, back.yMax - 34f, OfferWidth, 28f), $"<color=#cccccc>{hint}</color>", title);
    }

    void DrawPicks(World world, string me)
    {
        string text = PicksText(world, me);
        if (text.Length == 0) return;
        var rect = new Rect(HudScale.Width - PicksWidth - 16f, PicksTop, PicksWidth, small.CalcHeight(new GUIContent(text), PicksWidth) + 12f);
        var before = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.4f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = before;
        GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f), text, small);
    }
}
