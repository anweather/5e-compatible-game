using Chem;
using UnityEngine;

// What a run shows (docs/design/the-hold.md, phase 2; World.Hold.cs): the plate that starts one, the keep's health over the
// keep, and a HUD line while one goes on or just after. Only the show: the core decides everything, from the stands.
[RequireComponent(typeof(ChemGrid))]
public class HoldShow : MonoBehaviour
{
    const float PlateHeight = 0.08f, BannerRise = 26f, BannerSize = 0.25f, HudRange = 160f, ShowLoss = 30f;
    static readonly Color PlateColor = new(0.8f, 0.68f, 0.25f);

    const float HurtSeconds = 0.35f;
    static float hurtAt = -1f;

    // Each Play mode starts from these, with the domain reload off too (Enter Play Mode Options).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        hurtAt = -1f;
    }

    ChemGrid grid;
    World shownFor;
    GameObject plate;
    HoldCrystal crystal;

    /// <summary>The keep's crystal at the heart, or null with no Hold here: for the probe.</summary>
    public HoldCrystal Crystal => crystal;
    HoldTowers towers;

    /// <summary>The towers' looks, or null with no Hold here: for the probe.</summary>
    public HoldTowers Towers => towers;
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
        int last = world.Rules.Sim.Hold.Waves;
        string of = last > 0 ? $" of {last}" : "";
        return h.Phase switch
        {
            HoldPhase.Running => $"Wave {h.Wave}{of} · {world.HoldLeft} left · keep {Mathf.Max(0, h.Health)}",
            HoldPhase.Won => "The keep held. The plate starts again",
            _ => $"The keep fell in wave {h.Wave}. The plate starts again",
        };
    }

    /// <summary>The timer to the next wave while one's under way (timed waves), "Next wave in 0:42"; empty during its warning, on the last, or untimed. For the probe too.</summary>
    public string TimerLine()
    {
        var world = grid != null ? grid.World : null;
        if (world?.HoldSite == null || world.HoldWarning != null || world.HoldNextWaveIn is not { } ticks) return "";
        return $"Next wave in {Clock(ticks, world)}";
    }

    static string Clock(int ticks, World world)
    {
        int seconds = (ticks + world.Rules.Sim.TicksPerSecond - 1) / world.Rules.Sim.TicksPerSecond;
        return $"{seconds / 60}:{seconds % 60:00}";
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

    /// <summary>What a wizard's hands hold and whether they're ready, for the probe (the HUD shows it as CastHud's icons); empty with its hands off.</summary>
    public string HandsLine(string player)
    {
        var world = grid != null ? grid.World : null;
        if (world?.HoldSite == null || !world.HandsOn(player)) return "";
        return $"Left: {Holding(world, player, HandsDef.Left)}   Right: {Holding(world, player, HandsDef.Right)}";
    }

    static string Holding(World world, string player, int hand) =>
        world.HandOpen(player, hand) ? $"{world.HandsOf(player).Holds[hand] ?? "nothing"}{Cooling(world, player, hand)}" : "locked";

    /// <summary>At a tower: its element, and how a hand takes it (or what unlocks it).</summary>
    public static string TowerLine(World world, string player)
    {
        var tower = world.TowerOf(player);
        if (tower == null) return "";
        string line = $"The {tower.Element} tower";
        if (!world.ElementOpen(player, tower.Element) && world.UnlockGiving(u => u.Element == tower.Element) is { } row)
        {
            int away = world.PointsAway(player, row);
            return $"The {tower.Element} tower: {tower.Element} in {away} {(away == 1 ? "point" : "points")}";
        }
        return line;
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
        string timer = TimerLine();
        string text = world.Hold == null ? "" : BannerText(world.Hold, warning, timer);
        if (banner != null && text != bannerText) banner.text = bannerText = text;
        if (banner != null && Camera.main != null) banner.transform.rotation = Camera.main.transform.rotation;
        if (world.HoldRunning) sawRun = true;
        else if (sawRun)
        {
            sawRun = false;
            lostAt = Time.time;
        }
    }

    // Over the keep: its health, then what's coming, or the wave and the timer to the next.
    static string BannerText(HoldState hold, string warning, string timer)
    {
        string keep = $"THE KEEP {Mathf.Max(0, hold.Health)}";
        if (warning.Length > 0) return $"{keep}\n{warning.ToUpperInvariant()}";
        return timer.Length > 0 ? $"{keep}\nWAVE {hold.Wave}  {timer.ToUpperInvariant()}" : $"{keep}\nWAVE {hold.Wave}";
    }

    void Show(World world)
    {
        shownFor = world;
        if (plate != null) Destroy(plate);
        if (banner != null) Destroy(banner.gameObject);
        if (crystal != null) Destroy(crystal.gameObject);
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
        var heart = grid.CellCenter(hx, hy);
        heart.y = grid.GroundAt(heart);
        crystal = HoldCrystal.At(transform, heart);
        crystal.Follow(world);
        if (towers == null) towers = new GameObject("Hold towers").AddComponent<HoldTowers>();
        towers.transform.SetParent(transform, false);
        towers.Show(grid, world);
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

    /// <summary>The local wizard took a blow: the screen flashes red for a moment.</summary>
    public static void Hurt() => hurtAt = Time.time;

    static void DrawHurt()
    {
        float left = 1f - (Time.time - hurtAt) / HurtSeconds;
        if (left <= 0f || hurtAt < 0f) return;
        var before = GUI.color;
        GUI.color = new Color(0.8f, 0.05f, 0.02f, 0.38f * left);
        GUI.DrawTexture(new Rect(0f, 0f, HudScale.Width, HudScale.Height), Texture2D.whiteTexture);
        GUI.color = before;
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
        DrawHurt();
        var world = grid.World;
        if (!EventLog.HudVisible || world?.HoldSite == null) return;
        var caster = EventLog.TrackedCaster;
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, richText = true };
        // The hands' icons (CastHud) say what HandsLine says, so it isn't drawn (D16).
        if (caster != null && world.TowerOf(caster.CastPlayer) != null)
            GUI.Label(new Rect(HudScale.Width / 2f - 450f, HudScale.Height - 280f, 900f, 36f), $"<color=#bfe3ff>{TowerLine(world, caster.CastPlayer)}</color>", style);
        if (world.Hold == null) return;
        if (!world.HoldRunning && (lostAt < 0f || Time.time - lostAt > ShowLoss)) return;
        var (hx, hy) = world.HoldSite.Heart;
        if (caster == null || Vector3.Distance(caster.transform.position, grid.CellCenter(hx, hy)) > HudRange) return;
        GUI.Label(new Rect(HudScale.Width / 2f - 400f, 54f, 800f, 36f), $"<b>{HudLine()}</b>", style);
        string warning = WarningLine();
        if (warning.Length > 0) GUI.Label(new Rect(HudScale.Width / 2f - 400f, 124f, 800f, 36f), $"<color=#ffd27a><b>{warning}</b></color>", style);
        else if (TimerLine() is { Length: > 0 } timer) GUI.Label(new Rect(HudScale.Width / 2f - 400f, 90f, 800f, 36f), $"<color=#e8e2cf><b>{timer}</b></color>", style);
        BossBar(world);
    }
}
