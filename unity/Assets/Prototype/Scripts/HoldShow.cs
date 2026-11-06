using Chem;
using UnityEngine;

// What a run shows (docs/design/the-hold.md, phase 2; World.Hold.cs): the plate that starts one, the keep's health over the
// keep, and a HUD line while one goes on or just after. Only the show: the core decides everything, from the stands.
[RequireComponent(typeof(ChemGrid))]
public class HoldShow : MonoBehaviour
{
    const float PlateHeight = 0.08f, BannerRise = 26f, BannerSize = 0.25f, HudRange = 160f, ShowLoss = 30f;
    static readonly Color PlateColor = new(0.8f, 0.68f, 0.25f);

    ChemGrid grid;
    World shownFor;
    GameObject plate;
    TextMesh banner;
    string bannerText;
    GUIStyle style;
    bool sawRun;
    float lostAt = -1f;

    /// <summary>The HUD line, for the probe too; empty with no Hold here.</summary>
    public string HudLine()
    {
        var world = grid != null ? grid.World : null;
        var h = world?.Hold;
        if (world?.HoldSite == null || h == null) return "";
        return h.Phase == HoldPhase.Running
            ? $"Wave {h.Wave}: {world.HoldLeft} left, the keep {Mathf.Max(0, h.Health)}/{world.Rules.Sim.Hold.Health}"
            : $"The keep fell in wave {h.Wave}. Stand on the plate to try again";
    }

    /// <summary>What's coming while a wave waits: its number, theme, boss and how long; empty with a wave under way. For the probe too.</summary>
    public string WarningLine()
    {
        var world = grid != null ? grid.World : null;
        if (world?.HoldSite == null || world.HoldWarning is not { } warning) return "";
        var rows = world.Rules.Sim.Hold;
        string theme = warning.plan.Theme != null ? $": {rows.Theme(warning.plan.Theme)?.Name}" : "";
        string boss = warning.plan.Boss ? ", with a boss" : "";
        return $"Wave {warning.plan.Wave}{theme}{boss}, in {(warning.ticks + world.Rules.Sim.TicksPerSecond - 1) / world.Rules.Sim.TicksPerSecond} s";
    }

    /// <summary>The boss in the field, or null: for the HUD's bar.</summary>
    public static Raider BossOf(World world)
    {
        foreach (var r in world.Raiders)
            if (world.Rules.AdjectiveOf(r.Kind)?.Boss == true) return r;
        return null;
    }

    /// <summary>What a wizard's hands hold and whether they're ready, for the HUD and the probe; empty with its hands off.</summary>
    public string HandsLine(string player)
    {
        var world = grid != null ? grid.World : null;
        if (world?.HoldSite == null || !world.HandsOn(player)) return "";
        var h = world.HandsOf(player);
        string line = $"Left: {h.Holds[HandsDef.Left] ?? "nothing"}{Cooling(world, player, HandsDef.Left)}   Right: {h.Holds[HandsDef.Right] ?? "nothing"}{Cooling(world, player, HandsDef.Right)}";
        return world.InAttuning(player) ? line + "   (attuning: a hand's trigger changes it)" : line;
    }

    static string Cooling(World world, string player, int hand)
    {
        int ticks = world.HandCooling(player, hand);
        return ticks > 0 ? $" ({(ticks + world.Rules.Sim.TicksPerSecond - 1) / world.Rules.Sim.TicksPerSecond}s)" : "";
    }

    void Awake() => grid = GetComponent<ChemGrid>();

    void Update()
    {
        var world = grid.World;
        if (world == null) return;
        if (world != shownFor) Show(world);
        if (world.HoldSite == null) return;
        string warning = WarningLine();
        string text = world.Hold == null ? "" : warning.Length > 0 ? $"THE KEEP {Mathf.Max(0, world.Hold.Health)}\n{warning.ToUpperInvariant()}" : $"THE KEEP {Mathf.Max(0, world.Hold.Health)}\nWAVE {world.Hold.Wave}";
        if (banner != null && text != bannerText) banner.text = bannerText = text;
        if (banner != null && Camera.main != null) banner.transform.rotation = Camera.main.transform.rotation;
        if (world.HoldRunning) sawRun = true;
        else if (sawRun)
        {
            sawRun = false;
            lostAt = Time.time;
        }
    }

    void Show(World world)
    {
        shownFor = world;
        if (plate != null) Destroy(plate);
        if (banner != null) Destroy(banner.gameObject);
        bannerText = null;
        if (world.HoldSite == null) return;
        var (px, py) = world.HoldSite.Plate;
        var at = grid.CellCenter(px, py);
        at.y = grid.GroundAt(at) + PlateHeight / 2f;
        plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        plate.name = "Hold plate";
        Destroy(plate.GetComponent<Collider>());
        plate.transform.SetParent(transform, true);
        plate.transform.position = at;
        plate.transform.localScale = new Vector3(grid.cellSize, PlateHeight / 2f, grid.cellSize);
        var renderer = plate.GetComponent<MeshRenderer>();
        renderer.material = UrpMaterials.NewLit("Hold plate", PlateColor, 0.3f);
        var (hx, hy) = world.HoldSite.Heart;
        var top = grid.CellCenter(hx, hy);
        top.y = grid.GroundAt(top) + BannerRise;
        var go = new GameObject("Hold banner");
        go.transform.SetParent(transform, true);
        go.transform.position = top;
        banner = go.AddComponent<TextMesh>();
        banner.anchor = TextAnchor.MiddleCenter;
        banner.alignment = TextAlignment.Center;
        banner.characterSize = BannerSize;
        banner.fontSize = 48;
    }

    void BossBar(World world)
    {
        var boss = BossOf(world);
        if (boss == null) return;
        const float Width = 520f, Height = 18f, Top = 128f;
        var before = GUI.color;
        var back = new Rect(HudScale.Width / 2f - Width / 2f, Top + 26f, Width, Height);
        GUI.Label(new Rect(back.x, Top, Width, 26f), $"<b>{world.Rules.Entity(boss.Kind)?.Name}</b>", style);
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        GUI.color = new Color(0.72f, 0.12f, 0.52f);
        GUI.DrawTexture(new Rect(back.x + 2f, back.y + 2f, (Width - 4f) * Mathf.Clamp01(boss.Hp / (float)Mathf.Max(1, boss.MaxHp)), Height - 4f), Texture2D.whiteTexture);
        GUI.color = before;
    }

    void OnGUI()
    {
        HudScale.Apply();
        var world = grid.World;
        if (!EventLog.HudVisible || world?.HoldSite == null) return;
        var caster = EventLog.TrackedCaster;
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, richText = true };
        if (caster != null) GUI.Label(new Rect(HudScale.Width / 2f - 400f, 90f, 800f, 36f), HandsLine(caster.CastPlayer), style);
        if (world.Hold == null) return;
        if (!world.HoldRunning && (lostAt < 0f || Time.time - lostAt > ShowLoss)) return;
        var (hx, hy) = world.HoldSite.Heart;
        if (caster == null || Vector3.Distance(caster.transform.position, grid.CellCenter(hx, hy)) > HudRange) return;
        GUI.Label(new Rect(HudScale.Width / 2f - 400f, 54f, 800f, 36f), $"<b>{HudLine()}</b>", style);
        string warning = WarningLine();
        if (warning.Length > 0) GUI.Label(new Rect(HudScale.Width / 2f - 400f, 124f, 800f, 36f), $"<color=#ffd27a><b>{warning}</b></color>", style);
        BossBar(world);
    }
}
