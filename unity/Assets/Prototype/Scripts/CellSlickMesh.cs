using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// Oil and grease on the cells as one glossy film lying on the ground: a mesh
// over the covered cells with a rounded, wobbly outline (ContourSurface) that
// fades out at its edge, oil near black, grease a dull yellow. Which covers wear
// it comes from the rules (IsFilm): any slippery one, and any a mod adds (black
// ice, a mod's own ash over its charred paint) in its look's colour. Mud and glass
// ground wear the same film, thinner: mud a wet, dark brown sheen over its
// darker paint (CellPaint), glass a pale, glassy glaze over sand, brick a
// baked red (its courses are decals, CellCrackMesh). A cover's film wins over its ground's. Rebuilt when a cell's cover or terrain changes.
// Ash is not a film: it is the terrain's charred paint. Ground acid has eaten
// (ChemGrid.WatchAcid) wears a stain in the same film, a dark, sour green, for
// a while (Eaten, Expire); a slick there wins over it. A falling star's target
// mark (Cell.Star) is a hot glow in the same film, over everything else. Frost
// is a pale glaze in it, and the rock lava leaves (Crust) a dark, cindery crust.
// In tiles, as the water is (CellWaterMesh): a change redraws the tiles round it, nearest the
// camera first, within a few milliseconds a frame, so a storm's mud never stalls one.
// A look's glow (look.glow 0 to 1, docs/design/primitives.md P4: a cover's, or a ground terrain's
// under no cover) is a second film in the same tiles over those cells alone, added on top in the
// look's colour times the glow (Strength), through one shared material whose brightness follows the
// lamps (Nightfall.Lamps, as the windows' _LampGlow does): next to nothing by day, full after dark. No
// lights and no material per cell; with no look glowing in the rules nothing of it is made or asked.
public sealed class CellSlickMesh
{
    // How far the film floats over the terrain, how far in from its edge it turns opaque (in
    // blended value), and the outline: 4 lattice points per cell, more wobble than water.
    const float Lift = 0.025f, FadeWidth = 0.35f;
    // A step in the ground (metres) beside a cell that holds the outline still there: a wobble
    // over a pit's lip would drape the film down its side.
    const float Step = 0.1f;
    // Ground steeper than this (rise over run) sheds the film: none clings to a pit's side.
    const float FlatGrade = 0.12f, SteepGrade = 0.35f;
    static readonly ContourSpec Outline = new(4, 0.5f, 0.8f, 47);
    static readonly Color OilColor = new(0.03f, 0.028f, 0.03f, 0.93f);
    static readonly Color GreaseColor = new(0.62f, 0.52f, 0.2f, 0.85f);
    static readonly Color MudColor = new(0.17f, 0.11f, 0.06f, 0.5f);
    static readonly Color GlassColor = new(0.2f, 0.31f, 0.35f, 0.42f);
    static readonly Color EatenColor = new(0.16f, 0.22f, 0.07f, 0.55f);
    static readonly Color StarColor = new(0.95f, 0.2f, 0.03f, 0.8f);
    static readonly Color BrickColor = new(0.27f, 0.095f, 0.06f, 0.95f);
    static readonly Color FrostColor = new(0.8f, 0.9f, 0.97f, 0.62f);
    static readonly Color CrustColor = new(0.06f, 0.05f, 0.05f, 0.82f);
    const string StarFilm = "star", CrustFilm = "crust";

    readonly ChemGrid grid;
    readonly Dictionary<int, string> slicks = new();
    // Cells under a scoped look's film, and its colour on each.
    readonly Dictionary<int, Color> scoped = new();
    const string ScopeFilm = "scoped";
    const float SameFilm = 0.03f;
    // Rock cells that were lava (a look only: the core's cell is plain rock).
    readonly HashSet<int> crusts = new();
    // Cells acid ate, and until when their stain shows.
    readonly Dictionary<int, float> eaten = new();
    readonly List<int> expired = new();
    const int TileCells = 16;
    const double DrawBudgetMs = 2.0;
    const int ExactDrawTiles = 2;
    readonly Material material;
    readonly int tilesX;
    readonly GameObject root;
    readonly Dictionary<int, ContourSurface> tiles = new();
    readonly HashSet<int> dirtyTiles = new();
    readonly List<int> drawOrder = new();
    readonly ChemGrid.FrameBudget drawBudget = new(DrawBudgetMs, ExactDrawTiles);
    // What a tile's Build asks, made once.
    readonly System.Func<int, int, float> filmAt, wobbleAt;
    // The covers' colours in the rules in force.
    Color oil = OilColor, grease = GreaseColor, frost = FrostColor;
    readonly System.Func<float, float, float> heightAt;
    readonly System.Func<float, float, float, Color> shadeAt;
    // ColorAt's channels, made once (Shade asks for each at every point of the film).
    readonly System.Func<int, int, float?> redAt, greenAt, blueAt, alphaAt, topAt, gradeAt;
    // The tile being drawn, a ring wider all round: each cell's film colour and how steep it is
    // (asked of once, by the first point that wants it), as a point asks four cells of everything.
    const int TileRing = 2, TileSide = TileCells + 2 * TileRing;
    readonly Color?[] tileColors = new Color?[TileSide * TileSide];
    readonly float[] tileGrades = new float[TileSide * TileSide];
    readonly bool[] tileAsked = new bool[TileSide * TileSide];
    int tileX, tileY;

    // --- glow ---------------------------------------------------------------------------------
    // How bright a glow shines over its colour: MaxGlow * (1 - e^(-glow / GlowKnee)), so 0.1 is a faint
    // shimmer (0.16), 0.3 a clear glow (0.35), 1 the most (0.53); none comes near the look's full colour, which
    // would wash the cells into a flat cut-out after dark (as a thing's glow of 0.4 does). The glow film's texture
    // (GlowTexture) mottles it between GlowLeast and all of it, so a patch glows unevenly, as moss would.
    const float MaxGlow = 0.55f, GlowKnee = 0.3f, GlowLeast = 0.45f;
    // The mottle: a tiling value noise of GlowLattice cells (and twice as fine, at half weight) over GlowSide texels.
    const int GlowSide = 64, GlowLattice = 8;
    // How much of it shows in full daylight (the lamps out), and the step in light worth a material change.
    const float DayGlow = 0.04f, SameLight = 0.002f;
    // The glow film floats this far over the film under it.
    const float GlowLift = 0.004f;
    // Cells that glow, and the colour each adds (the look's colour times Strength), before the light.
    readonly Dictionary<int, Color> glows = new();
    readonly Dictionary<int, ContourSurface> glowTiles = new();
    readonly Material glowMaterial;
    readonly Texture2D glowTexture;
    // The glow of every cover and ground terrain that glows in the rules in force, and the rules it was read from.
    Dictionary<string, Color> glowLooks = new();
    Rules glowRules;
    float lit = -1f;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    readonly System.Func<int, int, float> glowFilmAt;
    readonly System.Func<float, float, float> glowHeightAt;
    readonly System.Func<float, float, float, Color> glowShadeAt;
    readonly System.Func<int, int, float?> glowRedAt, glowGreenAt, glowBlueAt;

    public CellSlickMesh(ChemGrid grid, Material material)
    {
        this.grid = grid;
        this.material = material;
        glowMaterial = UrpMaterials.NewUnlitParticles("Glow film", Color.black, additive: true);
        glowMaterial.renderQueue = material.renderQueue + 1;
        glowTexture = GlowTexture();
        glowMaterial.SetTexture("_BaseMap", glowTexture);
        glowFilmAt = (x, y) => Glows(x, y) ? 1f : 0f;
        glowHeightAt = (gx, gy) => Height(gx, gy) + GlowLift;
        glowShadeAt = GlowShade;
        glowRedAt = (x, y) => GlowColor(x, y)?.r;
        glowGreenAt = (x, y) => GlowColor(x, y)?.g;
        glowBlueAt = (x, y) => GlowColor(x, y)?.b;
        tilesX = (grid.World.W + TileCells - 1) / TileCells;
        root = new GameObject("Slicks");
        root.transform.SetParent(grid.transform, false);
        filmAt = (x, y) => Has(x, y) ? 1f : 0f;
        wobbleAt = (x, y) => BesideStep(x, y) ? 0f : 1f;
        heightAt = Height;
        shadeAt = Shade;
        topAt = (x, y) => Has(x, y) ? grid.GroundTop(x, y, grid.World.At(x, y)) : null;
        redAt = (x, y) => TileColor(x, y)?.r;
        greenAt = (x, y) => TileColor(x, y)?.g;
        blueAt = (x, y) => TileColor(x, y)?.b;
        alphaAt = (x, y) => TileColor(x, y)?.a;
        gradeAt = (x, y) => TileGrade(x, y);
    }

    // Which covers wear a film, from the rules in force (asked once a cover, again after a switch): a slippery one
    // (oil, grease, frost; a mod's black ice), and one the base rules don't have, a mod's own, in its look's colour,
    // so a cover the view has never seen is drawn, not skipped. Snow stays the land's paint.
    readonly Dictionary<string, bool> filmCovers = new();
    // A mod's own cover's film, where its look gives no colour.
    static readonly Color OwnFilmColor = new(0.5f, 0.5f, 0.5f, 0.85f);

    /// <summary>Whether a cover is drawn as a film, in the rules in force.</summary>
    public bool IsFilm(string cover)
    {
        if (cover == null) return false;
        if (filmCovers.TryGetValue(cover, out bool film)) return film;
        var rules = grid.World.Rules;
        film = rules.Covers.TryGetValue(cover, out var def) &&
               (def.Has("slippery") || def.Look?.Family == "slick" || (!BaseRules.Covers.ContainsKey(cover) && cover != rules.Sim.Weather.SnowCover && cover != rules.Sim.Weather.BankCover));
        filmCovers[cover] = film;
        return film;
    }

    /// <summary>Whether a cover's film floats a sheen on water (CellFxTexture.Oil): a slippery cover that burns (oil, grease), not a glaze (frost).</summary>
    public bool IsSheen(string cover) => IsFilm(cover) && grid.World.Rules.Covers.TryGetValue(cover, out var def) && def.Has("slippery") && def.Burns;

    Rules BaseRules => grid.CurrentRules?.Base ?? grid.World.Rules;

    /// <summary>A cell was redrawn: rock that <paramref name="cooled"/> from lava wears a crust; anything else loses it.</summary>
    public void Crust(int x, int y, Cell c, bool cooled)
    {
        int key = grid.World.Idx(x, y);
        if (cooled && c.T == TerrainIds.Rock) crusts.Add(key);
        else if (c.T != TerrainIds.Rock) crusts.Remove(key);
    }

    /// <summary>True for a terrain drawn with a film (mud, glass, brick).</summary>
    public static bool IsGlazed(string terrain) => terrain == TerrainIds.Mud || terrain == TerrainIds.Glass || terrain == TerrainIds.Brick;

    /// <summary>The film a cell wears: its cover's (oil, grease, frost, a mod's own: IsFilm), else its ground's (mud, glass), else null.</summary>
    public string FilmOf(Cell c)
    {
        if (c.Star > 0) return StarFilm;
        if (IsFilm(c.Cover)) return c.Cover;
        return IsGlazed(c.T) ? c.T : null;
    }

    /// <summary>A cell's cover or terrain changed: the film is rebuilt at the next Rebuild.</summary>
    public void Changed(int x, int y, Cell c)
    {
        int key = grid.World.Idx(x, y);
        var film = FilmOf(c) ?? (crusts.Contains(key) ? CrustFilm : null) ?? (scoped.ContainsKey(key) ? ScopeFilm : null);
        bool had = slicks.TryGetValue(key, out var was);
        if (film != null) slicks[key] = film;
        else slicks.Remove(key);
        if (PerfCounts.On && (had ? was != film : film != null)) PerfCounts.Add("slick.changed." + (film ?? "none_was_" + was));
        if (had ? was != film : film != null) MarkAround(x, y);
        if (glows.Count > 0 || GlowLooks.Count > 0) GlowChanged(key, x, y, c);
    }

    // The glow of every cover, and every ground terrain, whose look glows in the rules in force, as the colour it adds.
    Dictionary<string, Color> GlowLooks
    {
        get
        {
            var rules = grid.World.Rules;
            if (ReferenceEquals(rules, glowRules)) return glowLooks;
            glowRules = rules;
            glowLooks = new Dictionary<string, Color>();
            foreach (var cover in rules.Covers.Values)
                if (cover.Look != null && cover.Look.Glow > 0f) glowLooks["c:" + cover.Id] = GlowOf(cover.Look);
            foreach (var t in rules.Terrain.Values)
                if (t.Look != null && t.Look.Glow > 0f && t.Look.Family == "ground") glowLooks["t:" + t.Id] = GlowOf(t.Look);
            return glowLooks;
        }
    }

    // The glow film's mottle, made once: grey from GlowLeast to 1, tiling (its lattice wraps), with no draw from any RNG.
    static Texture2D GlowTexture()
    {
        var t = new Texture2D(GlowSide, GlowSide, TextureFormat.RGBA32, false, true) { name = "Glow mottle", wrapMode = TextureWrapMode.Repeat };
        var texels = new Color32[GlowSide * GlowSide];
        for (int y = 0; y < GlowSide; y++)
            for (int x = 0; x < GlowSide; x++)
            {
                float n = (2f * Mottle(x, y, GlowLattice, 61) + Mottle(x, y, 2 * GlowLattice, 62)) / 3f;
                byte grey = (byte)Mathf.RoundToInt(255f * Mathf.Lerp(GlowLeast, 1f, n));
                texels[y * GlowSide + x] = new Color32(grey, grey, grey, 255);
            }
        t.SetPixels32(texels);
        t.Apply(false, true);
        return t;
    }

    // Value noise at a texel on a lattice of this many cells across the texture, wrapping at its edge, 0 to 1.
    static float Mottle(int x, int y, int lattice, int seed)
    {
        float fx = x * lattice / (float)GlowSide, fy = y * lattice / (float)GlowSide;
        int x0 = (int)fx, y0 = (int)fy;
        float tx = Mathf.SmoothStep(0f, 1f, fx - x0), ty = Mathf.SmoothStep(0f, 1f, fy - y0);
        float At(int i, int j) => ValueNoise.Hash(i % lattice, j % lattice, seed);
        return Mathf.Lerp(Mathf.Lerp(At(x0, y0), At(x0 + 1, y0), tx), Mathf.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), tx), ty);
    }

    /// <summary>How strongly a glow shines over its colour, before the light (see MaxGlow).</summary>
    public static float Strength(float glow) => glow <= 0f ? 0f : MaxGlow * (1f - Mathf.Exp(-Mathf.Min(glow, 1f) / GlowKnee));

    static Color GlowOf(LookDef look)
    {
        float k = Strength(look.Glow);
        return new Color(look.R * k, look.G * k, look.B * k, 1f);
    }

    // What a cell adds in glow: its cover's (a cover hides its ground's), else its ground terrain's; null for none.
    Color? GlowOfCell(Cell c)
    {
        var looks = GlowLooks;
        if (looks.Count == 0) return null;
        if (c.Cover != null) return looks.TryGetValue("c:" + c.Cover, out var cover) ? cover : null;
        return looks.TryGetValue("t:" + c.T, out var ground) ? ground : null;
    }

    void GlowChanged(int key, int x, int y, Cell c)
    {
        var now = GlowOfCell(c);
        bool had = glows.TryGetValue(key, out var was);
        if (now.HasValue)
        {
            glows[key] = now.Value;
            if (!had || was != now.Value) MarkAround(x, y);
        }
        else if (had)
        {
            glows.Remove(key);
            MarkAround(x, y);
        }
    }

    // The rules switched and some look's glow came, went or changed: every cell is looked over again for it
    // (a ground terrain's glow reaches cells nothing redraws), once that switch.
    void RescanGlow(Dictionary<string, Color> before)
    {
        var after = GlowLooks;
        if (after.Count == before.Count && after.All(kv => before.TryGetValue(kv.Key, out var was) && was == kv.Value)) return;
        var world = grid.World;
        for (int i = 0; i < world.Cells.Length; i++)
            if (world.Cells[i] is Cell c) GlowChanged(i, i % world.W, i / world.W, c);
    }

    /// <summary>The glow drawn at a cell now (its colour times its strength times the light), black for none; for a probe.</summary>
    public Color GlowAt(int x, int y)
    {
        if (!grid.World.InBounds(x, y) || !glows.TryGetValue(grid.World.Idx(x, y), out var g)) return Color.black;
        float k = Light();
        return new Color(g.r * k, g.g * k, g.b * k, 1f);
    }

    /// <summary>How many cells glow, for a probe.</summary>
    public int GlowCells => glows.Count;

    // How much of a glow shows in this light: DayGlow by day, all of it once the lamps are lit.
    float Light() => DayGlow + (1f - DayGlow) * Nightfall.Lamps(grid.World);

    // Once a frame while anything glows: the one glow material takes the light.
    void LightGlow()
    {
        float k = Light();
        if (Mathf.Abs(k - lit) <= SameLight) return;
        lit = k;
        glowMaterial.SetColor(BaseColorId, new Color(k, k, k, 1f));
    }

    /// <summary>
    /// A scoped look's colour over what can't be drawn cell by cell (the land, the lake), as a film on the
    /// cell, or null for none (ChemGrid.GroundFilmAt). A cell with a film of its own keeps that. Before Changed.
    /// </summary>
    public void Scoped(int x, int y, Color? over)
    {
        int key = grid.World.Idx(x, y);
        bool had = scoped.TryGetValue(key, out var was);
        if (over.HasValue)
        {
            scoped[key] = over.Value;
            if (!had || (was - over.Value).maxColorComponent > SameFilm || (over.Value - was).maxColorComponent > SameFilm) MarkAround(x, y);
        }
        else if (had)
        {
            scoped.Remove(key);
            MarkAround(x, y);
        }
    }

    /// <summary>Ground moved at a cell: the film near it follows at the next Rebuild.</summary>
    public void GroundChanged(int x, int y)
    {
        PerfCounts.Add("slick.ground_changed");
        if (slicks.Count > 0 || eaten.Count > 0 || glows.Count > 0) MarkAround(x, y);
    }

    // A cell's change reaches two cells round it (the outline reads one ring out, the wobble another).
    void MarkAround(int x, int y)
    {
        int tx0 = Mathf.Max(0, (x - 2) / TileCells), tx1 = Mathf.Min(tilesX - 1, (x + 2) / TileCells);
        int ty0 = Mathf.Max(0, (y - 2) / TileCells), ty1 = (y + 2) / TileCells;
        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++) dirtyTiles.Add(ty * tilesX + tx);
    }

    /// <summary>Acid ate something at a cell: it wears the eaten stain until <paramref name="until"/> (Time.time).</summary>
    public void Eaten(int x, int y, float until)
    {
        eaten[grid.World.Idx(x, y)] = until;
        MarkAround(x, y);
    }

    /// <summary>Stains past their time go, at the next Rebuild.</summary>
    public void Expire(float now)
    {
        if (eaten.Count == 0) return;
        expired.Clear();
        foreach (var kv in eaten)
            if (kv.Value <= now) expired.Add(kv.Key);
        foreach (int key in expired)
        {
            eaten.Remove(key);
            MarkAround(key % grid.World.W, key / grid.World.W);
        }
    }

    /// <summary>Redraws the tiles near any change since the last time, nearest the camera first, as far as the frame's budget goes.</summary>
    /// <summary>The film colour a cell is drawn in, for a probe.</summary>
    public Color? FilmColorAt(int x, int y) => ColorAt(x, y);

    /// <summary>How many cells wear this film (a cover's id, or a ground's), for a probe.</summary>
    public int FilmCells(string film)
    {
        int n = 0;
        foreach (var f in slicks.Values)
            if (f == film) n++;
        return n;
    }

    /// <summary>The rules switched: the covers' films take the colours in force.</summary>
    public void Recolour()
    {
        oil = grid.CoverColor(CoverIds.Oil, OilColor);
        grease = grid.CoverColor(CoverIds.Grease, GreaseColor);
        frost = grid.CoverColor(CoverIds.Frost, FrostColor);
        filmCovers.Clear();
        RescanGlow(glowLooks);
        var covers = grid.World.Rules.Covers;
        foreach (var (key, film) in slicks)
            if (covers.ContainsKey(film) || film == CoverIds.Oil || film == CoverIds.Grease || film == CoverIds.Frost) MarkAround(key % grid.World.W, key / grid.World.W);
    }

    public void Rebuild(bool busy = false)
    {
        if (glows.Count > 0) LightGlow();
        if (dirtyTiles.Count == 0 || busy) return;
        PerfCounts.Add("slick.rebuilds");
        PerfCounts.Peak("slick.film_cells", slicks.Count + eaten.Count);
        drawBudget.Start();
        drawOrder.Clear();
        drawOrder.AddRange(dirtyTiles);
        var cam = Camera.main;
        if (cam != null && drawOrder.Count > 1)
        {
            var eye = cam.transform.position;
            drawOrder.Sort((a, b) => TileDistance(a, eye).CompareTo(TileDistance(b, eye)));
        }
        foreach (int tile in drawOrder)
        {
            if (drawBudget.Over()) break;
            DrawTile(tile);
            dirtyTiles.Remove(tile);
        }
    }

    // Squared distance on the ground from a point to a tile's middle.
    float TileDistance(int tile, Vector3 eye)
    {
        int cx = (tile % tilesX) * TileCells + TileCells / 2, cy = (tile / tilesX) * TileCells + TileCells / 2;
        var mid = grid.CellCenter(Mathf.Min(cx, grid.World.W - 1), Mathf.Min(cy, grid.World.H - 1));
        float dx = mid.x - eye.x, dz = mid.z - eye.z;
        return dx * dx + dz * dz;
    }

    // A tile's film: made the first time it has any, emptied when it has none.
    void DrawTile(int tile)
    {
        var world = grid.World;
        int x0 = tile % tilesX * TileCells, y0 = tile / tilesX * TileCells;
        var box = new RectInt(x0, y0, Mathf.Min(TileCells, world.W - x0), Mathf.Min(TileCells, world.H - y0));
        if (box.width <= 0 || box.height <= 0) return;
        bool glow = glowTiles.ContainsKey(tile) || (glows.Count > 0 && AnyGlowNear(box));
        if (!tiles.TryGetValue(tile, out var surface))
        {
            if (!AnyNear(box))
            {
                if (glow) DrawGlow(tile, box);
                return;
            }
            surface = new ContourSurface(grid, "Slicks", material, Outline, 4f, tiled: true);
            surface.go.transform.SetParent(root.transform, true);
            tiles[tile] = surface;
        }
        ReadTile(box);
        surface.Build(box, filmAt, heightAt, shadeAt, wobbleAt);
        if (glow) DrawGlow(tile, box);
    }

    // A tile's glow film, over its glowing cells alone, in the films' outline.
    void DrawGlow(int tile, RectInt box)
    {
        if (!glowTiles.TryGetValue(tile, out var surface))
        {
            surface = new ContourSurface(grid, "Glow", glowMaterial, Outline, 4f, tiled: true);
            surface.go.transform.SetParent(root.transform, true);
            glowTiles[tile] = surface;
        }
        ReadTile(box);
        surface.Build(box, glowFilmAt, glowHeightAt, glowShadeAt, wobbleAt);
    }

    bool AnyGlowNear(RectInt box)
    {
        for (int y = box.yMin - 1; y <= box.yMax; y++)
            for (int x = box.xMin - 1; x <= box.xMax; x++)
                if (Glows(x, y)) return true;
        return false;
    }

    bool Glows(int x, int y) => grid.World.InBounds(x, y) && glows.ContainsKey(grid.World.Idx(x, y));

    Color? GlowColor(int x, int y) => grid.World.InBounds(x, y) && glows.TryGetValue(grid.World.Idx(x, y), out var g) ? g : null;

    // The glows' colours (blended where they meet), faded at their edge and on steep ground as the films are.
    Color GlowShade(float gx, float gy, float value) => new(
        ContourSurface.BlendAround(gx, gy, glowRedAt, 0f),
        ContourSurface.BlendAround(gx, gy, glowGreenAt, 0f),
        ContourSurface.BlendAround(gx, gy, glowBlueAt, 0f),
        Fade(gx, gy, value, 1f));

    void ReadTile(RectInt box)
    {
        tileX = box.xMin - TileRing;
        tileY = box.yMin - TileRing;
        System.Array.Clear(tileAsked, 0, tileAsked.Length);
        for (int y = 0; y < TileSide; y++)
            for (int x = 0; x < TileSide; x++) tileColors[y * TileSide + x] = ColorAt(tileX + x, tileY + y);
    }

    // Where in the tile's arrays a cell is, or -1 past them.
    int TileIndex(int x, int y)
    {
        int ix = x - tileX, iy = y - tileY;
        return ix < 0 || iy < 0 || ix >= TileSide || iy >= TileSide ? -1 : iy * TileSide + ix;
    }

    Color? TileColor(int x, int y)
    {
        int at = TileIndex(x, y);
        return at >= 0 ? tileColors[at] : ColorAt(x, y);
    }

    float TileGrade(int x, int y)
    {
        int at = TileIndex(x, y);
        if (at < 0) return Grade(x + 0.5f, y + 0.5f);
        if (!tileAsked[at])
        {
            tileAsked[at] = true;
            tileGrades[at] = Grade(x + 0.5f, y + 0.5f);
        }
        return tileGrades[at];
    }

    // Whether any cell in the box, or the ring round it, wears a film (a tile also draws the edge of one lying just past it).
    bool AnyNear(RectInt box)
    {
        for (int y = box.yMin - 1; y <= box.yMax; y++)
            for (int x = box.xMin - 1; x <= box.xMax; x++)
                if (Has(x, y)) return true;
        return false;
    }

    // On the ground, but never dipping below the slick cells' own level: past a pit's lip the
    // film stays level (and faded, see Shade) instead of bending down, which would catch the
    // light along the bend.
    float Height(float gx, float gy)
    {
        float ground = grid.GroundAt(ContourSurface.FlatOn(grid, gx, gy));
        float level = ContourSurface.BlendAround(gx, gy, topAt, ground);
        float top = Mathf.Max(ground, level);
        if (scoped.Count > 0) top = Mathf.Max(top, grid.DrawnSurfaceAt(gx, gy, top));
        return top + Lift;
    }

    // Whether the ground beside a cell (its 8 neighbours) is a step up or down from it.
    bool BesideStep(int x, int y)
    {
        var world = grid.World;
        if (!world.InBounds(x, y)) return false;
        float top = grid.GroundTop(x, y, world.At(x, y));
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (world.InBounds(x + dx, y + dy) && Mathf.Abs(grid.GroundTop(x + dx, y + dy, world.At(x + dx, y + dy)) - top) > Step)
                    return true;
        return false;
    }

    bool Has(int x, int y) => grid.World.InBounds(x, y) && (slicks.ContainsKey(grid.World.Idx(x, y)) || eaten.ContainsKey(grid.World.Idx(x, y)));

    // The films' colours (blended where they meet), fading out toward the edge and on steep ground.
    Color Shade(float gx, float gy, float value)
    {
        var c = new Color(
            ContourSurface.BlendAround(gx, gy, redAt, 0f),
            ContourSurface.BlendAround(gx, gy, greenAt, 0f),
            ContourSurface.BlendAround(gx, gy, blueAt, 0f),
            ContourSurface.BlendAround(gx, gy, alphaAt, 0f));
        c.a = Fade(gx, gy, value, c.a);
        return c;
    }

    // An alpha faded out toward a film's edge and on steep ground.
    float Fade(float gx, float gy, float value, float a)
    {
        a *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Outline.Iso, Outline.Iso + FadeWidth, value));
        a *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlatGrade, SteepGrade, ContourSurface.BlendAround(gx, gy, gradeAt, 0f)));
        return a;
    }

    // How steep the ground is at a point (grid units): the larger rise over run across a quarter cell.
    // A cell's middle speaks for it, and a point blends the four round it: Shade asked this of every point.
    float Grade(float gx, float gy)
    {
        const float d = 0.125f;
        float run = 2f * d * grid.cellSize;
        float ex = grid.GroundAt(ContourSurface.FlatOn(grid, gx + d, gy)) - grid.GroundAt(ContourSurface.FlatOn(grid, gx - d, gy));
        float ey = grid.GroundAt(ContourSurface.FlatOn(grid, gx, gy + d)) - grid.GroundAt(ContourSurface.FlatOn(grid, gx, gy - d));
        return Mathf.Max(Mathf.Abs(ex), Mathf.Abs(ey)) / run;
    }

    // A cell's film colour, or null for a cell without a film.
    Color? ColorAt(int x, int y)
    {
        if (!Has(x, y)) return null;
        if (!slicks.TryGetValue(grid.World.Idx(x, y), out var film)) return EatenColor;
        return film switch
        {
            CoverIds.Grease => grid.LooksScoped ? grid.CoverColorAt(film, grid.World.Idx(x, y), GreaseColor) : grease,
            TerrainIds.Mud => MudColor,
            TerrainIds.Glass => GlassColor,
            TerrainIds.Brick => BrickColor,
            StarFilm => StarColor,
            CoverIds.Frost => grid.LooksScoped ? grid.CoverColorAt(film, grid.World.Idx(x, y), FrostColor) : frost,
            ScopeFilm => scoped.TryGetValue(grid.World.Idx(x, y), out var over) ? over : Color.clear,
            CrustFilm => CrustColor,
            CoverIds.Oil => grid.LooksScoped ? grid.CoverColorAt(CoverIds.Oil, grid.World.Idx(x, y), OilColor) : oil,
            // A mod's own cover: its look's colour (CoverColorAt reads the rules in force, scoped or not).
            _ => grid.CoverColorAt(film, grid.World.Idx(x, y), OwnFilmColor),
        };
    }

    public void Destroy()
    {
        foreach (var surface in tiles.Values) surface.Destroy();
        foreach (var surface in glowTiles.Values) surface.Destroy();
        Object.Destroy(glowMaterial);
        Object.Destroy(glowTexture);
        Object.Destroy(root);
    }
}
