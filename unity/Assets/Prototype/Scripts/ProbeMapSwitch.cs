using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's mapswitch scenario (docs/design/maps-as-data.md, phase 8): the valley, then the Drowned Chain, then the valley
// again, in one Play session, the way the Esc menu's "Map when you host" switches (ChemGrid.PlayMap: the map's folders,
// then the scene loaded again on them, which tears down and builds again everything the map owns). On each map: the world,
// chunks, sea level and spawn are that map's, its things are spawned, and its land equals a fresh start on it (the cells as
// MapWorld lays them from the folder, and the land as land.json has it). Back on the valley, the land is the same as at
// the start of the run and nothing leaked: GameObjects, meshes, textures, materials and terrain data counted at the start
// and after (unused assets unloaded first). Each switch's longest frame and time to the first drawn frame are reported.
public class MapSwitchProbe : ProbeScenario
{
    const string Valley = GameFolders.DefaultMap, Chain = "drowned-chain";
    const float MostSwitchSeconds = 60f;

    protected override IEnumerator Run()
    {
        Check("starts_on_the_valley", ChemGrid.MapId == Valley, $"on {ChemGrid.MapId}: empty unity/{ChemGrid.EditorMapFile}");
        if (ChemGrid.MapId != Valley) yield break;
        // The probe outlives the scene's reloads.
        DontDestroyOnLoad(gameObject);
        string startLand = LandPrint();
        CheckMap("valley_start", null);
        yield return Census();
        var before = counted;

        yield return SwitchTo(Chain, "to_chain");
        if (ChemGrid.MapId != Chain) yield break;
        CheckMap("chain", loaded);
        yield return WalkFor(MapLand.Current.Map.Spawn.x + 16 - grid.yardCells.x, MapLand.Current.Map.Spawn.y - grid.yardCells.y, 4f);

        yield return SwitchTo(Valley, "to_valley");
        if (ChemGrid.MapId != Valley) yield break;
        CheckMap("valley_back", loaded);
        string backLand = LandPrint();
        Check("valley_land_as_at_the_start", backLand == startLand, $"{startLand} then {backLand}");
        yield return Census();
        var after = counted;
        foreach (var kind in before.Keys)
        {
            int grew = after[kind] - before[kind];
            Metric($"leak_{kind}", grew);
            Metric($"count_{kind}", after[kind]);
        }
        // A few of the scene's own objects come and go with the clock (a gull, a ripple); a leak of a map's chunks or
        // places is hundreds.
        Check("no_objects_leaked", after["gameobjects"] - before["gameobjects"] <= 8, Delta(before, after, "gameobjects"));
        Check("no_meshes_leaked", after["meshes"] - before["meshes"] <= 4, Delta(before, after, "meshes"));
        Check("no_textures_leaked", after["textures"] - before["textures"] <= 2, Delta(before, after, "textures"));
        Check("no_materials_leaked", after["materials"] - before["materials"] <= 4, Delta(before, after, "materials"));
        Check("no_terrains_leaked", after["terrains"] - before["terrains"] <= 0, Delta(before, after, "terrains"));
    }

    static string Delta(Dictionary<string, int> before, Dictionary<string, int> after, string kind) =>
        $"{before[kind]} before, {after[kind]} after";

    // Loads a map the way the Esc menu does and waits for its first drawn frame, measuring the frames between.
    IEnumerator SwitchTo(string id, string name)
    {
        var was = ChemGrid.Current?.Grid;
        float from = Time.realtimeSinceStartup, longest = 0f;
        int frames = 0;
        loaded = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnLoaded;
        ChemGrid.PlayMap(id);
        while (Time.realtimeSinceStartup - from < MostSwitchSeconds)
        {
            yield return null;
            frames++;
            longest = Mathf.Max(longest, Time.unscaledDeltaTime);
            var now = ChemGrid.Current?.Grid;
            if (now != null && now != was && now.ChunksLeftToDraw == 0 && WizardRig.FindLocal() != null) break;
        }
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnLoaded;
        float seconds = Time.realtimeSinceStartup - from;
        Rebind();
        Metric($"{name}_seconds", seconds);
        Metric($"{name}_longest_frame_ms", longest * 1000f);
        Metric($"{name}_frames", frames);
        Check($"{name}_switched", grid != null && grid != was && ChemGrid.MapId == id, $"on {ChemGrid.MapId} after {seconds:0.0} s ({frames} frames, the longest {longest * 1000f:0} ms)");
        // A frame or two for the things and the wizard to settle on the new ground.
        yield return Wait(1f);
    }

    // The new world's cells as the scene has built them, before its first tick (after every Awake, before any Update).
    (string print, int tick)? loaded;

    void OnLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        var world = ChemGrid.Current?.Grid.World;
        if (world != null) loaded = (CellPrint(world, true), world.Tick);
    }

    // The map on screen is the one switched to: the world, chunks, sea, spawn, things and land are its own; its cells as
    // they were loaded (null at the run's own start, which is a fresh start) equal a fresh start's.
    void CheckMap(string name, (string print, int tick)? atLoad)
    {
        var land = MapLand.Current;
        var map = land.Map;
        var folder = ChemGrid.Folders.World;
        Check($"{name}_world_is_the_maps", map.Id == ChemGrid.MapId && World.W == map.W && World.H == map.H, $"{map.Id}: {World.W} x {World.H}, map {map.W} x {map.H}");
        var terrains = FindAnyObjectByType<MapTerrains>();
        int chunks = terrains != null ? terrains.Chunks.Length : 0, wanted = land.Layout.ChunksX * land.Layout.ChunksZ;
        int allChunks = FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        Check($"{name}_chunks_are_the_maps", chunks == wanted && allChunks == wanted, $"{chunks} chunks of {wanted}, {allChunks} terrains in the scene");
        var ocean = Ocean.Instance;
        Check($"{name}_sea_level_is_the_maps", ocean == null || Mathf.Abs(ocean.level - land.WaterLevel) < 1e-4f, $"sea {(ocean != null ? ocean.level : float.NaN)}, map {land.WaterLevel}");

        var spawn = grid.CellCenter(map.Spawn.x, map.Spawn.y);
        var at = wizard.transform.position;
        float off = new Vector2(at.x - spawn.x, at.z - spawn.z).magnitude, above = at.y - grid.GroundAt(at);
        Metric($"{name}_spawn_off_m", off);
        Check($"{name}_starts_at_the_spawn", off < 3f && above > -0.5f && above < 1.5f, $"{off:0.0} m off the spawn, {above:0.00} m over the ground");

        var fromFile = WorldThings.Load(folder);
        var spawner = FindAnyObjectByType<ThingSpawner>();
        int live = spawner != null ? spawner.Live.Count() : 0;
        Metric($"{name}_things", live);
        Check($"{name}_things_are_the_maps", World.ThingsNow.Count == fromFile.Count && live == World.ThingsNow.Count, $"{live} spawned, {World.ThingsNow.Count} in the world, {fromFile.Count} in its things.json");

        // A fresh start on this map: its cells as MapWorld lays them from the folder, and its land as land.json has it.
        if (atLoad is { } at0)
        {
            var fresh = ChemGrid.MapWorld(World.Rules, ChemGrid.CellMap(), grid.seed, out _);
            string freshCells = CellPrint(fresh, true);
            Check($"{name}_cells_are_a_fresh_start", at0.print == freshCells && at0.tick == 0, $"{at0.print} at tick {at0.tick}, fresh {freshCells}");
        }
        string landPath = Path.Combine(folder, "land.json");
        var fileLand = File.Exists(landPath) ? LandFile.Read(File.ReadAllText(landPath), map.W, map.H) : null;
        string mapLand = Print(land.Land), freshLand = Print(fileLand);
        Metric($"{name}_land_hash", Fnv(land.Land));
        Check($"{name}_land_is_a_fresh_start", mapLand == freshLand, $"{mapLand}, land.json {freshLand}");
    }

    // The land as played: each cell's raised height (not its terrain, which the weather may have turned to mud by now),
    // then the map's land.
    string LandPrint() => $"{ChemGrid.MapId} {CellPrint(World, false)} {Print(MapLand.Current?.Land)}";

    static string CellPrint(World world, bool terrain)
    {
        uint h = 2166136261;
        foreach (var c in world.Cells)
        {
            if (terrain)
                foreach (char ch in c.T) h = (h ^ ch) * 16777619;
            h = (h ^ (uint)c.Height) * 16777619;
        }
        return h.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }

    static string Print(int[] land) => land == null ? "none" : Fnv(land).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);

    static uint Fnv(int[] values)
    {
        uint h = 2166136261;
        if (values == null) return h;
        foreach (int v in values) h = (h ^ (uint)v) * 16777619;
        return h;
    }

    Dictionary<string, int> counted;

    // What's alive, after unused assets have gone: what a switch and back must leave as it found it.
    IEnumerator Census()
    {
        // Unity's own sweep: what nothing holds goes, so what's left is held (a static, a DontDestroyOnLoad).
        yield return Resources.UnloadUnusedAssets();
        counted = new Dictionary<string, int>
        {
            ["gameobjects"] = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
            ["meshes"] = Resources.FindObjectsOfTypeAll<Mesh>().Length,
            ["textures"] = Resources.FindObjectsOfTypeAll<Texture>().Length,
            ["materials"] = Resources.FindObjectsOfTypeAll<Material>().Length,
            ["terrains"] = Resources.FindObjectsOfTypeAll<TerrainData>().Length,
        };
    }
}
