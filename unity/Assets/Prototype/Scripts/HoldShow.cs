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

    void Awake() => grid = GetComponent<ChemGrid>();

    void Update()
    {
        var world = grid.World;
        if (world == null) return;
        if (world != shownFor) Show(world);
        if (world.HoldSite == null) return;
        string text = world.Hold == null ? "" : $"THE KEEP {Mathf.Max(0, world.Hold.Health)}\nWAVE {world.Hold.Wave}";
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

    void OnGUI()
    {
        HudScale.Apply();
        var world = grid.World;
        if (!EventLog.HudVisible || world?.HoldSite == null || world.Hold == null) return;
        if (!world.HoldRunning && (lostAt < 0f || Time.time - lostAt > ShowLoss)) return;
        var caster = EventLog.TrackedCaster;
        var (hx, hy) = world.HoldSite.Heart;
        if (caster == null || Vector3.Distance(caster.transform.position, grid.CellCenter(hx, hy)) > HudRange) return;
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, richText = true };
        GUI.Label(new Rect(HudScale.Width / 2f - 400f, 54f, 800f, 36f), $"<b>{HudLine()}</b>", style);
    }
}
