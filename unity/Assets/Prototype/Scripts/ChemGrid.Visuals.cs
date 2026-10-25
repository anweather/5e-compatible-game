using System.Collections.Generic;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// How the grid looks. Each frame Render() walks the cells and, for any cell
// whose state changed, updates that cell's view: its ground tile (built by
// GridTileBuilder), see-through water or glossy ice, a cover film (oil,
// grease, ash), and its fire, gas and charge effects (ChemGrid.Effects.cs).
// A small pool of flickering lights follows the biggest fires
// (FireLightPool). Cubes stand in for any effect prefab that isn't assigned
// (for example before the Asset Store packs are restored).
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
    [Tooltip("Multiplies grass colours, so the yard can match the terrain around it.")]
    public Color grassTint = Color.white;

    // What is currently shown for one cell, so a cell is only redrawn when it changes.
    sealed class View
    {
        public GameObject tile, water, cover, fire, gas, spark;
        public string terrain;
        public int key = int.MinValue;
        public int fireLevel;
        public string gasKind;
        public bool charged;
        public float waterGround, waterTop, waterTarget;
    }

    View[] views;
    readonly HashSet<View> rising = new();
    readonly MaterialCache materials = new();
    GridTileBuilder tiles;
    FireLightPool fireLights;

    void InitVisuals()
    {
        views = new View[World.W * World.H];
        for (int i = 0; i < views.Length; i++) views[i] = new View();
        tiles ??= new GridTileBuilder(this, materials);
        fireLights = new FireLightPool(this, maxFireLights);
    }

    void Render()
    {
        for (int y = 0; y < World.H; y++)
        {
            for (int x = 0; x < World.W; x++) RenderCell(x, y);
        }
        AnimateWater();
    }

    void RenderCell(int x, int y)
    {
        var c = World.At(x, y);
        var v = views[y * World.W + x];
        int key = Key(c);
        if (key == v.key && v.terrain == c.T) return;
        v.key = key;

        // Yard cells are blocks; cells outside the yard are scenery that ValleyChemistry updates.
        bool yard = InYard(x, y);
        if (v.terrain != c.T) ChangeTerrain(v, c, x, y, yard);
        float ground = yard && GridTileBuilder.IsDressed(c.T) ? GroundTop(c) : SurfaceY(x, y);
        var center = CellCenter(x, y);

        ground = RenderWater(v, c, center, ground, yard);
        RenderCover(v, c, center, ground);
        RenderFire(v, c, center, FireGround(c, ground, yard), !yard && c.T == TerrainIds.Tree ? 1.8f : 1f);
        RenderGas(v, c, center, ground, (x + y) % 2 == 0);
        // A lightning strike across the lake charges thousands of cells; outside the
        // yard, sparks on one cell in 25 read the same without thousands of effects.
        if (yard || (x % 5 == 0 && y % 5 == 0)) RenderCharge(v, c, center, ground);
    }

    void ChangeTerrain(View v, Cell c, int x, int y, bool yard)
    {
        if (yard)
        {
            if (v.tile != null) Destroy(v.tile);
            v.tile = tiles.Build(c, x, y);
        }
        else if (valley != null) valley.TerrainChanged(x, y, v.terrain, c.T);
        v.terrain = c.T;
    }

    // Outside the yard, fire burns up in a tree's crown or along a cabin wall.
    float FireGround(Cell c, float ground, bool yard) =>
        yard || valley == null ? ground : ground + ValleyChemistry.FireHeight(c.T);

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

    // A slab from the ground up to the water surface; ice is solid to walk on. Outside the
    // yard the lake's own surface stands for its cells; puddles still show. Returns the
    // height that covers and effects sit on.
    float RenderWater(View v, Cell c, Vector3 center, float ground, bool yard)
    {
        if (c.Water <= 0 || (!yard && c.T == TerrainIds.Pit))
        {
            Hide(v.water);
            return ground;
        }
        float top = Mathf.Max(WaterTop(c, ground, yard), ground + 0.04f);
        bool fresh = v.water == null || !v.water.activeSelf;
        var slab = Ensure(ref v.water, "Water", center, keepCollider: true);
        // New water rises from the ground; level changes ease in (see AnimateWater).
        v.waterGround = ground;
        v.waterTarget = top;
        if (fresh) v.waterTop = ground + 0.02f;
        if (c.Frozen) v.waterTop = top;
        PlaceWater(v, center);
        if (!Mathf.Approximately(v.waterTop, top)) rising.Add(v);
        var slabRenderer = slab.GetComponent<Renderer>();
        slabRenderer.sharedMaterial = c.Frozen ? materials.Lit("Ice", new Color(0.80f, 0.93f, 1f), 0.97f) : materials.Water();
        slabRenderer.shadowCastingMode = c.Frozen ? ShadowCastingMode.On : ShadowCastingMode.Off;
        slab.GetComponent<Collider>().enabled = c.Frozen;
        return top;
    }

    float WaterTop(Cell c, float ground, bool yard)
    {
        float unitsPerStep = World.Rules.Sim.WaterUnitsPerStep;
        return yard
            ? (World.ElevationUnits(c) + c.Water) / unitsPerStep * stepHeight
            : ground + c.Water / unitsPerStep * stepHeight;
    }

    /// <summary>Height of the water surface in a cell, as drawn (its ground when the cell is dry).</summary>
    public float WaterSurfaceY(int x, int y)
    {
        var c = World.At(x, y);
        if (c == null) return 0f;
        bool yard = InYard(x, y);
        return WaterTop(c, yard ? GroundTop(c) : SurfaceY(x, y), yard);
    }

    void PlaceWater(View v, Vector3 center)
    {
        float height = Mathf.Max(0.02f, v.waterTop - v.waterGround);
        v.water.transform.position = new Vector3(center.x, v.waterGround + height / 2f, center.z);
        v.water.transform.localScale = new Vector3(cellSize, height, cellSize);
    }

    // Each frame: ease water levels toward their targets, and drift the ripples.
    void AnimateWater()
    {
        materials.ScrollWater(Time.time);
        if (rising.Count == 0) return;
        float step = 1.2f * Time.deltaTime;
        rising.RemoveWhere(v =>
        {
            if (v.water == null || !v.water.activeSelf) return true;
            v.waterTop = Mathf.MoveTowards(v.waterTop, v.waterTarget, step);
            PlaceWater(v, v.water.transform.position);
            return Mathf.Approximately(v.waterTop, v.waterTarget);
        });
    }

    // --- covers ------------------------------------------------------------------

    void RenderCover(View v, Cell c, Vector3 center, float ground)
    {
        if (c.Cover == null)
        {
            Hide(v.cover);
            return;
        }
        var film = Ensure(ref v.cover, "Cover", center);
        film.transform.position = new Vector3(center.x, ground + 0.02f, center.z);
        film.transform.localScale = new Vector3(0.96f * cellSize, 0.03f, 0.96f * cellSize);
        film.GetComponent<Renderer>().sharedMaterial = materials.Lit("Cover-" + c.Cover, CoverColor(c.Cover), c.Cover == CoverIds.Ash ? 0.05f : 0.9f);
    }

    static Color CoverColor(string cover) => cover switch
    {
        CoverIds.Oil => new Color(0.06f, 0.05f, 0.05f),
        CoverIds.Grease => new Color(0.85f, 0.78f, 0.40f),
        _ => new Color(0.35f, 0.34f, 0.33f),
    };

    // --- stand-in cubes ------------------------------------------------------------

    // A cell's stand-in cube, made (or remade, if the slot holds a particle effect) and shown.
    GameObject Ensure(ref GameObject go, string name, Vector3 at, bool keepCollider = false)
    {
        if (go == null || go.GetComponent<ParticleSystem>() != null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            if (!keepCollider) Primitives.RemoveCollider(go);
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
        tiles?.Destroy();
    }
}
