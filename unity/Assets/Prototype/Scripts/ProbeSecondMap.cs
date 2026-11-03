using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chem;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// The second-map test (docs/design/second-map-2026-11-02.md): stills from the air, a walk from one
// edge of a map to the other that logs what the wizard meets, and fire and water on its land.
// Frames come off-screen, so they need no desktop session: the camera rendered by hand in
// LateUpdate into an HDR texture, with Cinemachine's brain off (RowHousesProbe.Look).
public abstract class SecondMapProbe : ProbeScenario
{
    const int Width = 1280, Height = 720, Quality = 88;
    const float FarSight = 4000f;

    (string file, Vector3 from, Vector3 at)? asked;
    bool taken;

    protected List<int> NotedCells => (Notes ?? "").Split(',', System.StringSplitOptions.RemoveEmptyEntries)
        .Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToList();

    protected static void Lens(bool free)
    {
        if (Camera.main.TryGetComponent<Unity.Cinemachine.CinemachineBrain>(out var flown)) flown.enabled = !free;
        if (free) Camera.main.farClipPlane = FarSight;
    }

    protected IEnumerator Snap(string file, Vector3 from, Vector3 at)
    {
        if (string.IsNullOrEmpty(ScreenshotDir)) yield break;
        Directory.CreateDirectory(ScreenshotDir);
        asked = (Path.Combine(ScreenshotDir, file), from, at);
        taken = false;
        while (!taken) yield return null;
    }

    void LateUpdate()
    {
        if (asked is not { } shot) return;
        asked = null;
        var cam = Camera.main;
        var hdr = new RenderTexture(Width, Height, GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormat.D24_UNorm_S8_UInt);
        var flat = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var page = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        cam.transform.SetPositionAndRotation(shot.from, Quaternion.LookRotation(shot.at - shot.from));
        cam.targetTexture = hdr;
        cam.Render();
        cam.targetTexture = null;
        Graphics.Blit(hdr, flat);
        RenderTexture.active = flat;
        page.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        RenderTexture.active = null;
        File.WriteAllBytes(shot.file, page.EncodeToJPG(Quality));
        RenderTexture.ReleaseTemporary(flat);
        Destroy(hdr);
        Destroy(page);
        taken = true;
    }

    protected string CellLine()
    {
        var cell = grid.CellAt(wizard.transform.position);
        var c = World.At(cell.x, cell.y);
        return c == null ? $"{cell.x},{cell.y},outside,0" : $"{cell.x},{cell.y},{c.T},{c.Water}";
    }
}

/// <summary>Stills from the air over the whole map, and at each cell named in notes ("x,y,x,y...").</summary>
public class MapViewProbe : SecondMapProbe
{
    const int OrbitFps = 30;
    const float OrbitDegrees = 0.5f;

    protected override IEnumerator Run()
    {
        // A map's land.json comes from its own bake, as the valley's does (Prototype > Bake Valley Land).
        if (ChemGrid.MapId != null && !File.Exists(Path.Combine(ChemGrid.FindWorldDir(), "land.json"))) Check("land_baked", true, grid.valley.BakeLand());
        Metric("water_cells", World.Cells.Count(c => c.Water > 0));
        foreach (var kind in World.Cells.GroupBy(c => c.T)) Metric("cells_" + kind.Key, kind.Count());
        Lens(true);
        var mid = grid.CellCenter(288, 288);
        yield return Snap("air-whole.jpg", mid + new Vector3(0f, 640f, -520f), mid);
        yield return Snap("air-sw.jpg", mid + new Vector3(-250f, 180f, -420f), mid + new Vector3(-150f, 0f, -150f));
        yield return Snap("air-ne.jpg", mid + new Vector3(420f, 160f, -100f), mid + new Vector3(160f, 40f, 160f));
        for (int f = 0; f < Seconds * OrbitFps; f++)
        {
            var round = Quaternion.Euler(0f, f * OrbitDegrees, 0f) * new Vector3(0f, 460f, -720f);
            yield return Snap($"frame_{f:00000}.jpg", mid + round, mid);
        }
        var start = wizard.transform.position;
        Metric("start_y", start.y);
        Metric("start_ground", grid.GroundAt(start));
        yield return Snap("start.jpg", start + new Vector3(-14f, 7f, -14f), start);
        var spots = NotedCells;
        for (int i = 0; i + 1 < spots.Count; i += 2)
        {
            var at = grid.CellCenter(spots[i], spots[i + 1]);
            at.y = grid.GroundAt(at);
            yield return Snap($"spot-{spots[i]}-{spots[i + 1]}.jpg", at + new Vector3(-30f, 22f, -30f), at);
        }
        Lens(false);
    }
}

/// <summary>
/// Walks the cells in notes ("x,y,x,y..."), the first by teleport, each for as long as it takes. trail.csv
/// holds a line every two seconds; with --shots, a chase frame each half second.
/// </summary>
public class MapWalkProbe : SecondMapProbe
{
    const float Patience = 15f, FrameEvery = 0.5f, LogEvery = 2f, MostOnLeg = 300f, Morning = 7.5f, Chase = 16f, ChaseHigh = 9f;

    readonly StringBuilder trail = new("t,x,z,y,ground,cellx,celly,terrain,water,swim,activeChunks,leg\n");
    readonly HashSet<string> met = new();
    float clock, nextLog, nextFrame, deepest;
    int frame, stuck, underDry, mostActive, switches;

    protected override IEnumerator Run()
    {
        var points = NotedCells;
        PlaceCell(points[0], points[1]);
        yield return Switch(At(Morning));
        yield return Wait(1f);
        Lens(true);
        grid.ChunkSwitched += Counted;
        for (int leg = 2; leg + 1 < points.Count; leg += 2) yield return Leg(leg / 2, grid.CellCenter(points[leg], points[leg + 1]));
        grid.ChunkSwitched -= Counted;
        Lens(false);
        Metric("walk_seconds", clock);
        Metric("deepest_under_ground_m", deepest);
        Metric("frames_under_ground_dry", underDry);
        Metric("stuck", stuck);
        Metric("most_active_chunks", mostActive);
        Metric("chunk_switches", switches);
        Check("terrains_met", met.Count > 0, string.Join(" ", met));
        File.WriteAllText(Path.Combine(ScreenshotDir ?? Application.temporaryCachePath, "trail.csv"), trail.ToString());
    }

    void Counted(ChunkSwitch s) => switches += 1;

    IEnumerator Leg(int leg, Vector3 target)
    {
        var surface = wizard.GetComponent<SurfaceEffects>();
        var walk = puppet.WalkTo(target);
        var mark = wizard.transform.position;
        float since = 0f, onLeg = 0f;
        while (onLeg < MostOnLeg && walk.MoveNext())
        {
            yield return walk.Current;
            onLeg += Time.deltaTime;
            clock += Time.deltaTime;
            since += Time.deltaTime;
            var p = wizard.transform.position;
            float ground = grid.GroundAt(p);
            deepest = Mathf.Max(deepest, ground - p.y);
            if (ground - p.y > 1.5f && !surface.Swimming) underDry++;
            if (clock >= nextLog) Log(p, ground, surface.Swimming, leg);
            if (since >= Patience)
            {
                if ((p - mark).magnitude < 1.5f)
                {
                    stuck++;
                    Check($"stuck_on_leg_{leg}", false, $"{(p - mark).magnitude:0.0} m in {Patience} s at {CellLine()}");
                    break;
                }
                mark = p;
                since = 0f;
            }
            if (clock < nextFrame) continue;
            nextFrame += FrameEvery;
            var back = target - p;
            back.y = 0f;
            yield return Snap($"frame_{frame++:00000}.jpg", p - back.normalized * Chase + Vector3.up * ChaseHigh, p + Vector3.up);
        }
        puppet.Move(Vector2.zero);
        var off = target - wizard.transform.position;
        off.y = 0f;
        Check($"reached_leg_{leg}", off.magnitude < 3f, $"{off.magnitude:0.0} m short after {onLeg:0} s, at {CellLine()}");
    }

    void Log(Vector3 p, float ground, bool swimming, int leg)
    {
        nextLog += LogEvery;
        int active = World.ActiveChunks.Count();
        mostActive = Mathf.Max(mostActive, active);
        string cell = CellLine();
        met.Add(cell.Split(',')[2]);
        trail.Append(System.FormattableString.Invariant($"{clock:0.0},{p.x:0.0},{p.z:0.0},{p.y:0.00},{ground:0.00},{cell},{swimming},{active},{leg}\n"));
    }
}

/// <summary>Fire and water on the map's land, near the cell in notes ("x,y"): Fire Bolt, Create Water, then a Fireball.</summary>
public class MapCastProbe : SecondMapProbe
{
    const int Reach = 8;
    const float Back = 16f, High = 9f;

    IEnumerator Look(string file, Vector3 at)
    {
        Lens(true);
        yield return Snap(file, at + new Vector3(-Back, High, -Back), at);
        Lens(false);
    }

    protected override IEnumerator Run()
    {
        var at = NotedCells;
        PlaceCell(at[0], at[1]);
        yield return Wait(2f);
        int x = at[0] + Reach, y = at[1];
        var look = grid.CellCenter(x, y);
        look.y = grid.GroundAt(look);
        var target = World.At(x, y);
        Metric("ground_units", World.ElevationUnits(target));
        Metric("ground_m", look.y);
        Check("target_is_land", target.Water == 0 && target.T != TerrainIds.Pit, target.T);
        Check("fire_bolt_on_bar", caster.Select(SpellIds.FireBolt));
        caster.Cast(caster.SelectedId, x, y, 0);
        yield return Wait(2f);
        int burning = BurningCells();
        Metric("burning_2s", burning);
        Check("fire_lit", burning > 0, $"{burning} cells");
        yield return Look("fire-a.jpg", look);
        yield return Wait(8f);
        Metric("burning_10s", BurningCells());
        Check("fire_spread", BurningCells() > burning, $"{burning} then {BurningCells()}");
        yield return Look("fire-b.jpg", look);
        int waterWas = World.Cells.Sum(c => c.Water);
        caster.Select(SpellIds.CreateWater);
        caster.Cast(caster.SelectedId, x, y, 0);
        yield return Wait(2f);
        int waterNow = World.Cells.Sum(c => c.Water);
        Metric("water_units_added", waterNow - waterWas);
        Check("water_landed", waterNow != waterWas, $"{waterWas} then {waterNow}");
        yield return Wait(4f);
        yield return Look("water.jpg", look);
        caster.Select(SpellIds.Fireball);
        caster.Cast(caster.SelectedId, x + Reach, y + Reach, 0);
        yield return Wait(3f);
        Metric("burning_after_fireball", BurningCells());
        Check("log", true, string.Join(" | ", LogSince(0).Take(12)));
        yield return Look("fireball.jpg", look);
    }
}
