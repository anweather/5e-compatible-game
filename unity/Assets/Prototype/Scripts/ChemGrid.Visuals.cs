using System.Collections.Generic;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// How the grid looks. The valley's chunk terrains are the ground: the cells
// shape and paint them (ValleyChemistry). Each frame Render() walks the cells
// and, for any cell whose state changed, updates what stands on it: its
// dressing (CellDressing: brambles, a stump, planks, crags), its invisible
// blocker (CellBlockers), its water or ice (one surface over all the wet
// cells: CellWaterMesh), oil or grease (one film: CellSlickMesh), and its fire, gas and
// charge effects (ChemGrid.Effects.cs).
// Reshaped ground eases to its new height (ChemGrid.Shaping.cs). A small pool
// of flickering lights follows the biggest fires (FireLightPool). Cubes stand
// in for any effect prefab that isn't assigned (for example before the Asset
// Store packs are restored).
public partial class ChemGrid
{
    [Header("Effects (optional; cubes stand in when missing)")]
    public GameObject fireSmall;
    public GameObject fireMedium;
    public GameObject fireLarge;
    public GameObject smokeFx;
    public GameObject steamFx;
    public GameObject sparksFx;
    public int maxFireLights = 16;
    [Tooltip("Optional: the material of the cells' water (vertex alpha fades its edges). A plain see-through blue stands in.")]
    public Material waterMaterial;
    [Tooltip("Optional: the material of oil and grease films (tinted by vertex colours). One is made at runtime if missing.")]
    public Material slickMaterial;
    [Tooltip("Optional: rock models a raised wall is built of, one per square, sized to cover its face (CellDressing). Without them, rough shapes stand in.")]
    public GameObject[] wallRocks;
    [Tooltip("Optional: smaller stones set on a raised wall's top to break its line.")]
    public GameObject[] wallStones;

    // What is currently shown for one cell, so a cell is only redrawn when it changes.
    sealed class View
    {
        public GameObject dressing, cover, fire, gas, spark;
        public string terrain, coverId;
        public int height, elevation;
        public int key = int.MinValue;
        public int fireLevel;
        public string gasKind;
        public bool charged;
        public bool wet;
    }

    View[] views;
    readonly MaterialCache materials = new();
    CellDressing dressings;
    CellBlockers blockers;
    CellWaterMesh waterSurface;
    CellSlickMesh slicks;
    FireLightPool fireLights;

    void InitVisuals()
    {
        views = new View[World.W * World.H];
        for (int i = 0; i < views.Length; i++) views[i] = new View();
        dressings ??= new CellDressing(this, materials);
        blockers = new CellBlockers(this);
        waterSurface?.Destroy();
        waterSurface = new CellWaterMesh(this, waterMaterial != null ? waterMaterial : materials.Water(),
            materials.Lit("Ice", new Color(0.80f, 0.93f, 1f), 0.97f));
        slicks?.Destroy();
        slicks = new CellSlickMesh(this, slickMaterial != null ? slickMaterial : materials.Slick());
        shaping.Clear();
        ForgetFx();
        fireLights = new FireLightPool(this, maxFireLights);
    }

    /// <summary>The top of the water drawn on a cell (CellWaterMesh), or null where none is drawn.</summary>
    public float? DrawnWaterTop(int x, int y)
    {
        float top = waterSurface.Top(x, y, float.NaN);
        return float.IsNaN(top) ? null : top;
    }

    // Every cell once, for a new world: the sleeping chunks' trees and walls get their blockers too.
    void RenderAll()
    {
        for (int y = 0; y < World.H; y++)
        {
            for (int x = 0; x < World.W; x++) RenderCell(x, y);
        }
        FinishRender();
    }

    // Each frame: the simulated chunks' cells (a sleeping chunk doesn't change; ChunkStreaming).
    void Render()
    {
        foreach (var (cx, cy) in World.ActiveChunks)
        {
            int x0 = cx << Chunks.Shift, y0 = cy << Chunks.Shift;
            int x1 = Mathf.Min(World.W, x0 + Chunks.Size), y1 = Mathf.Min(World.H, y0 + Chunks.Size);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) RenderCell(x, y);
        }
        FinishRender();
    }

    readonly System.Diagnostics.Stopwatch finishTimer = new();

    /// <summary>Of RenderMs, the milliseconds the last frame spent on the water, slicks and easing ground.</summary>
    public double SurfacesMs { get; private set; }

    void FinishRender()
    {
        finishTimer.Restart();
        waterSurface.Rebuild();
        slicks.Rebuild();
        materials.ScrollWater(Time.time);
        AnimateShaping();
        RecycleFx();
        SurfacesMs = finishTimer.Elapsed.TotalMilliseconds;
    }

    void RenderCell(int x, int y)
    {
        int index = y * World.W + x;
        var c = World.Cells[index];
        var v = views[index];
        int key = Key(c);
        if (key == v.key && v.terrain == c.T && v.height == c.Height) return;
        v.key = key;

        if (v.terrain != c.T || v.height != c.Height || v.coverId != c.Cover) ChangeGround(v, c, x, y);
        float groundTop = GroundTop(x, y, c);
        float ground = CellDressing.IsDressed(c.T) ? groundTop : groundTop + CellDressing.Look(c.T).height;
        var center = CellCenter(x, y);
        bool lake = valley != null && valley.IsLake(c, x, y);

        ground = RenderWater(v, c, x, y, ground, groundTop, lake);
        RenderCover(v, c, x, y, center, ground);
        RenderFire(v, c, x, y, center, ground + ValleyChemistry.FireHeight(c.T), c.T == TerrainIds.Tree ? 1.8f : 1f);
        RenderGas(v, c, center, ground, GasDrawnAt(x, y));
        // A lightning strike across the lake charges thousands of cells; on the lake, sparks on
        // one cell in 25 read the same without thousands of effects.
        if (!lake || (x % 5 == 0 && y % 5 == 0)) RenderCharge(v, c, center, ground);
    }

    // A new terrain, height or cover: the terrain is reshaped (eased, see ChemGrid.Shaping.cs)
    // and repainted, and the cell's dressing and blocker rebuilt where its ground stands now.
    void ChangeGround(View v, Cell c, int x, int y)
    {
        int elevation = World.ElevationUnits(c);
        bool reshaped = v.terrain != null && v.elevation != elevation;
        bool rebuild = v.terrain != c.T || v.height != c.Height;
        if (valley != null) valley.CellChanged(x, y, v.terrain, c.T, reshaped);
        if (rebuild)
        {
            // A rock wall growing taller rises from its old top, not from the flat ground under it.
            float shown = GroundTop(x, y, c);
            if (reshaped) shown = StandsAlone(c) && v.dressing != null ? v.dressing.transform.position.y : ShownGround(x, y, c);
            if (v.dressing != null) Destroy(v.dressing);
            v.dressing = dressings.Build(c, x, y, shown);
            blockers.Place(x, y, c, shown);
            if (reshaped) StartShaping(v, x, y, shown);
        }
        if (reshaped)
        {
            waterSurface.GroundChanged();
            slicks.GroundChanged();
        }
        v.terrain = c.T;
        v.height = c.Height;
        v.elevation = elevation;
        v.coverId = c.Cover;
    }

    // A hash of everything drawn for a cell apart from its terrain.
    static int Key(Cell c)
    {
        unchecked
        {
            int k = c.Water;
            k = k * 31 + (c.Frozen ? 1 : 0);
            k = k * 31 + (c.Cover?.GetHashCode() ?? 0);
            k = k * 31 + c.Fire;
            k = k * 31 + (c.Gas?.GetHashCode() ?? 0);
            k = k * 31 + c.GasAmt;
            k = k * 31 + (c.Charge > 0 ? 1 : 0);
            return k;
        }
    }

    // --- water and ice ----------------------------------------------------------

    // Water joins the wet cells' surface (CellWaterMesh); the lake's own surface stands for its
    // cells, except where they freeze: lake ice joins the ice, at the lake's surface. Returns the
    // height that covers and effects sit on.
    float RenderWater(View v, Cell c, int x, int y, float ground, float groundTop, bool lake)
    {
        bool wet = c.Water > 0 && (!lake || c.Frozen);
        if (wet || v.wet) waterSurface.Changed(x, y, wet);
        v.wet = wet;
        if (lake) return c.Water > 0 ? Mathf.Max(ground, valley.lakeLevel) : ground;
        return wet ? Mathf.Max(ground, waterSurface.Top(x, y, groundTop + WaterDepth(c))) : ground;
    }

    float WaterDepth(Cell c) => c.Water / (float)World.Rules.Sim.WaterUnitsPerStep * stepHeight;

    /// <summary>The lake's surface if this cell is lake (its water is drawn by the lake), else negative infinity.</summary>
    public float LakeLevelAt(int x, int y, Cell c) =>
        valley != null && valley.IsLake(c, x, y) ? valley.lakeLevel : float.NegativeInfinity;

    /// <summary>Height of the water surface in a cell, as drawn (its ground when the cell is dry).</summary>
    public float WaterSurfaceY(int x, int y)
    {
        var c = World.At(x, y);
        if (c == null) return 0f;
        float top = GroundTop(x, y, c) + WaterDepth(c);
        return valley != null && c.Water > 0 && valley.IsLake(c, x, y) ? Mathf.Max(top, valley.lakeLevel) : top;
    }

    // --- covers ------------------------------------------------------------------

    // Oil and grease are a glossy film over the cells (CellSlickMesh); ash is the terrain's own
    // charred paint (CellPaint), or a grey sheet without a valley.
    void RenderCover(View v, Cell c, int x, int y, Vector3 center, float ground)
    {
        slicks.Changed(x, y, c.Cover);
        if (c.Cover != CoverIds.Ash || valley != null)
        {
            Hide(v.cover);
            return;
        }
        var film = Ensure(ref v.cover, "Cover", center);
        film.transform.position = new Vector3(center.x, ground + 0.02f, center.z);
        film.transform.localScale = new Vector3(0.96f * cellSize, 0.03f, 0.96f * cellSize);
        film.GetComponent<Renderer>().sharedMaterial = materials.Lit("Cover-ash", new Color(0.35f, 0.34f, 0.33f), 0.05f);
    }

    // --- stand-in cubes ------------------------------------------------------------

    // A cell's stand-in cube, made (or remade, if the slot holds a particle effect) and shown.
    GameObject Ensure(ref GameObject go, string name, Vector3 at)
    {
        if (go == null || go.GetComponent<ParticleSystem>() != null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            Primitives.RemoveCollider(go);
        }
        go.SetActive(true);
        return go;
    }

    // Hides a stand-in cube; particle effects are retired instead (see Retire).
    static void Hide(GameObject go)
    {
        if (go != null && go.activeSelf && go.GetComponent<ParticleSystem>() == null) go.SetActive(false);
    }

    void OnDestroy()
    {
        materials.Destroy();
        waterSurface?.Destroy();
        slicks?.Destroy();
        if (plainDust != null) Destroy(plainDust);
    }
}
