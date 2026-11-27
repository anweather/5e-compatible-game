using System.Collections.Generic;
using Chem;
using UnityEngine;

// Scoped and mixed looks on the world (LookScopes, docs/design/world-tools.md): "the fire near me
// green", the fire round the village blue, each player their own colour, a patchwork. Colour comes
// from where a cell is when it's drawn, so a fire leaving a circle changes back; who cast what is
// the view's own note (ChemGrid.LookFire.cs). Only ever a view of the rules, and with no such look on
// nothing here runs: anyScoped is false and the world draws from ChemGrid.WorldLook.cs alone.
// A moving "around" touches only the cells at its edge, a few a frame, and never the rule switch.
public partial class ChemGrid
{
    const int MostMarksAFrame = 160;
    const float GroundFilm = 0.55f, FullFilmApart = 0.25f, LeastApart = 0.02f;
    const float GasSteps = 6f;
    const int MostGasCopies = 24;

    LookScopes scopes;
    bool anyScoped, groundScoped;
    string snowCover;
    bool noteCasters;
    CellLook fireLooks, emberLooks, lavaLooks, waterLooks, ashLooks, snowLooks;
    readonly Dictionary<string, CellLook> gasLooks = new(), coverLooks = new(), groundLooks = new();
    readonly Dictionary<(GameObject, Color), GameObject> gasCopies = new();
    readonly List<PlayerSpot> spots = new();
    readonly Dictionary<string, Vector2Int> spotWas = new();
    readonly Queue<int> toRemark = new();
    readonly HashSet<int> remarking = new();
    int spotsWere;
    LookColour fireArt;
    MaterialPropertyBlock clumpBlock;

    public IReadOnlyList<PlayerSpot> PlayerSpots => spots;

    public bool LooksScoped => anyScoped;

    public static int SlotOf(Caster caster) => caster != null ? caster.Slot : 0;

    static Color ToColor(LookColour c, float a = 1f) => new(c.R, c.G, c.B, a);

    static LookColour Of(Color c) => new(c.r, c.g, c.b);

    void CompileLooks()
    {
        var rules = DrawnRules;
        scopes = LookScopes.Compile(rules, Budgets?.Places ?? WorldPlaces.Empty);
        anyScoped = scopes.Any;
        fireArt = LookColour.Of((CurrentRules?.Base ?? rules).ElementLook(ElementIds.Fire));
        foreach (var copy in gasCopies.Values)
            if (copy != null) Destroy(copy);
        gasCopies.Clear();
        gasLooks.Clear();
        coverLooks.Clear();
        groundLooks.Clear();
        if (!anyScoped)
        {
            spots.Clear();
            noteCasters = groundScoped = false;
            return;
        }
        fireLooks = scopes.Of("elements", ElementIds.Fire);
        lavaLooks = scopes.Of("elements", ElementIds.Lava);
        waterLooks = scopes.Of("elements", ElementIds.Water);
        emberLooks = scopes.Of("covers", CoverIds.Embers);
        ashLooks = scopes.Of("covers", CoverIds.Ash);
        snowCover = rules.Sim.Weather.SnowCover;
        snowLooks = scopes.Of("covers", snowCover ?? CoverIds.Snow);
        noteCasters = fireLooks.ByPlayers || emberLooks.ByPlayers;
        foreach (var id in rules.Gases.Keys) noteCasters |= NoteLook(gasLooks, "gases", id);
        foreach (var id in rules.Covers.Keys) noteCasters |= NoteLook(coverLooks, "covers", id);
        groundScoped = waterLooks.Scopes > 0 || ashLooks.Scopes > 0 || snowLooks.Scopes > 0;
        foreach (var (_, table, id) in CellPaint.LayerLooks)
        {
            if (table != "terrain") continue;
            var look = scopes.Of(table, id);
            if (look.Scopes == 0) continue;
            groundLooks[id] = look;
            groundScoped = true;
        }
    }

    bool NoteLook(Dictionary<string, CellLook> looks, string table, string id)
    {
        var look = scopes.Of(table, id);
        if (!look.Plain) looks[id] = look;
        return look.ByPlayers;
    }

    void FollowLooks()
    {
        if (!anyScoped) return;
        ReadSpots();
        for (int marked = 0; marked < MostMarksAFrame && toRemark.Count > 0; marked++)
        {
            int index = toRemark.Dequeue();
            remarking.Remove(index);
            if (views[index] != null) views[index].key = int.MinValue;
            PerfCounts.Add("scope.marked");
        }
    }

    void ReadSpots()
    {
        PlayerCells();
        spots.Clear();
        foreach (var w in wizards)
        {
            if (w == null) continue;
            var cell = CellAt(w.transform.position);
            int slot = SlotOf(w);
            string name = Peers.NameOf(slot);
            spots.Add(new PlayerSpot(name, cell.x, cell.y, slot));
            if (spotWas.TryGetValue(name, out var was) && was != cell) MarkEdges(name, was, cell);
            spotWas[name] = cell;
        }
        if (spots.Count == spotsWere) return;
        spotsWere = spots.Count;
        MarkAllDrawn();
    }

    void MarkEdges(string player, Vector2Int was, Vector2Int now)
    {
        foreach (var (who, within) in scopes.Followers)
        {
            if (who != player) continue;
            int reach = within + LookScopes.EdgeCells;
            if ((was - now).sqrMagnitude > 4 * reach * reach)
            {
                MarkDisc(was, was, now, within, reach);
                MarkDisc(now, was, now, within, reach);
            }
            else MarkDisc(new Vector2Int((was.x + now.x) / 2, (was.y + now.y) / 2), was, now, within, reach, Mathf.Max(Mathf.Abs(was.x - now.x), Mathf.Abs(was.y - now.y)) / 2 + 1);
        }
    }

    void MarkDisc(Vector2Int middle, Vector2Int was, Vector2Int now, int within, int reach, int more = 0)
    {
        int inner = within * within, outer = reach * reach, side = reach + more;
        for (int y = middle.y - side; y <= middle.y + side; y++)
            for (int x = middle.x - side; x <= middle.x + side; x++)
            {
                if (!World.InBounds(x, y)) continue;
                int dw = (x - was.x) * (x - was.x) + (y - was.y) * (y - was.y);
                int dn = (x - now.x) * (x - now.x) + (y - now.y) * (y - now.y);
                if ((dw <= inner && dn <= inner) || (dw > outer && dn > outer)) continue;
                Remark(World.Idx(x, y));
            }
    }

    void Remark(int index)
    {
        var c = World.CellAt(index);
        if (c == null || !LookReaches(c)) return;
        if (remarking.Add(index)) toRemark.Enqueue(index);
    }

    bool LookReaches(Cell c) =>
        groundScoped || (c.Fire > 0 && !fireLooks.Plain) || (c.Cover == CoverIds.Embers && !emberLooks.Plain)
        || (c.Gas != null && gasLooks.ContainsKey(c.Gas)) || (c.Cover != null && coverLooks.ContainsKey(c.Cover));

    void MarkAllDrawn()
    {
        lookStamp++;
        if (groundScoped) groundStamp = lookStamp;
    }

    int lookStamp, groundStamp;
    int[] chunkLooks = System.Array.Empty<int>();

    void CatchUpLooks(int n, int cx, int cy)
    {
        if (chunkLooks[n] == lookStamp) return;
        using var marked = MarkDrawnMarker.Auto();
        bool ground = groundStamp > chunkLooks[n];
        chunkLooks[n] = lookStamp;
        int x0 = cx << Chunks.Shift, x1 = Mathf.Min(World.W, x0 + Chunks.Size);
        int y0 = cy << Chunks.Shift, y1 = Mathf.Min(World.H, y0 + Chunks.Size);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var v = views[y * World.W + x];
                if (v != null && (ground || v.fireLevel > 0 || v.gasKind != null || v.fogged || v.coverId != null)) v.key = int.MinValue;
            }
    }

    LookColour? NoteOn(int index, out int? caster)
    {
        caster = null;
        if (litBy.Count == 0 || !litBy.TryGetValue(index, out var note)) return null;
        if (note.Caster >= 0) caster = note.Caster;
        if (note.Colour.HasValue) return note.Colour;
        return note.Spell != null && Showman != null && Showman.FireLookOf(note.Spell, out _, out var glow) ? Of(glow) : null;
    }

    LookTint ScopedFireTint(int x, int y)
    {
        var own = NoteOn(World.Idx(x, y), out var caster);
        if (own == null && caster == null && fireLooks.Plain) return fireLook;
        if (caster == null && SpellFire(x, y, out var chosen)) return chosen;
        var colour = fireLooks.At(x, y, spots, own, caster);
        return LookTint.Between(ToColor(fireArt), ToColor(colour));
    }

    public Color GasColorAt(string gas, int index, Color fallback)
    {
        if (!anyScoped || !gasLooks.TryGetValue(gas, out var look)) return GasColor(gas, fallback);
        var own = NoteOn(index, out var caster);
        return ToColor(look.At(index % World.W, index / World.W, spots, caster != null ? own : null, caster), fallback.a);
    }

    public Color CoverColorAt(string cover, int index, Color fallback)
    {
        if (!anyScoped || !coverLooks.TryGetValue(cover, out var look)) return CoverColor(cover, fallback);
        var own = NoteOn(index, out var caster);
        return ToColor(look.At(index % World.W, index / World.W, spots, caster != null ? own : null, caster), fallback.a);
    }

    GameObject GasShowAt(string kind, GameObject plain, GameObject prefab, int x, int y)
    {
        if (!anyScoped || prefab == null || kind == null || !gasLooks.TryGetValue(kind, out var look)) return prefab;
        var art = ColorOf(GasLook(CurrentRules.Base, kind));
        var own = NoteOn(World.Idx(x, y), out var caster);
        var now = ToColor(look.At(x, y, spots, caster != null ? own : null, caster));
        var rounded = new Color(Mathf.Round(now.r * GasSteps) / GasSteps, Mathf.Round(now.g * GasSteps) / GasSteps, Mathf.Round(now.b * GasSteps) / GasSteps);
        var tint = LookTint.Between(art, rounded, 1f);
        if (!tint.Any) return plain;
        if (gasCopies.TryGetValue((plain, rounded), out var copy) && copy != null) return copy;
        if (gasCopies.Count >= MostGasCopies) return prefab;
        copy = Instantiate(plain, transform);
        copy.name = plain.name;
        copy.SetActive(false);
        tint.Dress(copy, null);
        gasCopies[(plain, rounded)] = copy;
        return copy;
    }

    void TintClump(GameObject clump, string cover, int x, int y)
    {
        if (clump == null || !clump.TryGetComponent<Renderer>(out var r)) return;
        if (!anyScoped || !coverLooks.ContainsKey(cover))
        {
            if (r.HasPropertyBlock()) r.SetPropertyBlock(null);
            return;
        }
        clumpBlock ??= new MaterialPropertyBlock();
        UrpMaterials.SetColor(r, clumpBlock, CoverColorAt(cover, World.Idx(x, y), Color.white));
    }

    Color? GroundFilmAt(Cell c, int x, int y)
    {
        var look = GroundLookOf(c);
        if (look == null || look.Scopes == 0) return null;
        var at = look.At(x, y, spots);
        var plain = look.Colour;
        float apart = Mathf.Max(Mathf.Abs(at.R - plain.R), Mathf.Max(Mathf.Abs(at.G - plain.G), Mathf.Abs(at.B - plain.B)));
        if (apart < LeastApart) return null;
        return ToColor(at, GroundFilm * Mathf.Clamp01(apart / FullFilmApart));
    }

    CellLook GroundLookOf(Cell c)
    {
        if (c.Water > 0 && !c.Frozen) return waterLooks;
        if (c.Cover == CoverIds.Ash) return ashLooks;
        if (c.Cover != null && c.Cover == snowCover) return snowLooks;
        return groundLooks.TryGetValue(c.T, out var look) ? look : null;
    }
}
