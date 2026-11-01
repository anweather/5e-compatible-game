using Chem;
using UnityEngine;

// The world's colours from the rules in force, so Claude can re-colour them through the MCP
// (set_look) with no rebuild: an element's colour is the colour of what it makes in the world,
// whichever spell or reaction made it, and a cover's or a gas's is its own. What's drawn from
// art (flames, smoke, the ground's textures, water) keeps its art and is turned or tinted by how
// far the look has moved from the base rules' (Rules: RuleSet.Base), so the rules as they ship
// change nothing at all. What's drawn in a flat colour reads it straight. Only ever a view of
// the rules: the same on every peer, as a look is outside Rules.Fingerprint and comes with the
// rule switch, and nothing here reaches the cells.
public partial class ChemGrid
{
    const float MostTint = 6f, LeastBase = 0.02f;

    LookTint fireLook, emberLook, lavaLook, steamLook, smokeLook;
    GameObject steamInLook, smokeInLook;

    Rules DrawnRules => CurrentRules?.Effective ?? World.Rules;

    /// <summary>The fire's colour in the rules in force, turned from its art.</summary>
    public LookTint FireElementTint => fireLook;

    /// <summary>What takes art drawn in <paramref name="art"/> to an element's colour in force; none if that hasn't moved.</summary>
    public LookTint ElementTint(string element, Color art)
    {
        var was = (CurrentRules?.Base ?? World.Rules).ElementLook(element);
        var now = DrawnRules.ElementLook(element);
        return was == null || now == null || LookDef.Same(was, now) ? default : LookTint.Between(art, ColorOf(now), 1f);
    }

    /// <summary>A cover's colour in the rules in force, or <paramref name="fallback"/> with no look.</summary>
    public Color CoverColor(string cover, Color fallback)
    {
        var look = DrawnRules.Covers.TryGetValue(cover, out var def) ? def.Look : null;
        return look != null ? new Color(look.R, look.G, look.B, fallback.a) : fallback;
    }

    /// <summary>A gas's colour in the rules in force, or <paramref name="fallback"/> with no look.</summary>
    public Color GasColor(string gas, Color fallback)
    {
        var look = DrawnRules.Gases.TryGetValue(gas, out var def) ? def.Look : null;
        return look != null ? new Color(look.R, look.G, look.B, fallback.a) : fallback;
    }

    // What takes the art from the base rules' colour to the colour in force.
    static LookTint TurnBetween(LookDef was, LookDef now, float wash = 0f) =>
        was == null || now == null || LookDef.Same(was, now) ? default : LookTint.Between(ColorOf(was), ColorOf(now), wash);

    static Color ColorOf(LookDef look) => new(look.R, look.G, look.B);

    /// <summary>What multiplies art drawn in the base rules' colour to bring it to the colour in force; white if it hasn't moved.</summary>
    public static Color TintBetween(LookDef was, LookDef now)
    {
        if (was == null || now == null || LookDef.Same(was, now)) return Color.white;
        return new Color(Ratio(was.R, now.R), Ratio(was.G, now.G), Ratio(was.B, now.B));
    }

    static float Ratio(float was, float now) => Mathf.Clamp(now / Mathf.Max(LeastBase, was), 0f, MostTint);

    static LookDef CoverLook(Rules rules, string id) => rules.Covers.TryGetValue(id, out var c) ? c.Look : null;

    static LookDef GasLook(Rules rules, string id) => rules.Gases.TryGetValue(id, out var g) ? g.Look : null;

    /// <summary>The film colour a cell is drawn in, for a probe.</summary>
    public Color? SlickColorAt(int x, int y) => slicks.FilmColorAt(x, y);

    /// <summary>The colour the wet cells' water is drawn in, for a probe.</summary>
    public Color CellWaterColor => waterSurface.Color;

    /// <summary>The valley's land and lake; null in the yard alone.</summary>
    public ValleyChemistry Valley => valley;

    /// <summary>Whether the gas on a cell is showing, in a look of its own.</summary>
    public bool GasInLookAt(int x, int y)
    {
        var v = views[World.Idx(x, y)];
        return v.gas != null && v.gas.activeInHierarchy && v.gasPrefab != null && (v.gasPrefab == steamInLook || v.gasPrefab == smokeInLook);
    }

    // Reads the colours in force. Copies of the gases' effects made for the last ones go.
    void ReadWorldLooks()
    {
        var was = CurrentRules != null ? CurrentRules.Base : World.Rules;
        var now = DrawnRules;
        fireLook = TurnBetween(was.ElementLook(ElementIds.Fire), now.ElementLook(ElementIds.Fire));
        lavaLook = TurnBetween(was.ElementLook(ElementIds.Lava), now.ElementLook(ElementIds.Lava));
        emberLook = TurnBetween(CoverLook(was, CoverIds.Embers), CoverLook(now, CoverIds.Embers));
        steamLook = TurnBetween(GasLook(was, GasIds.Steam), GasLook(now, GasIds.Steam), 1f);
        smokeLook = TurnBetween(GasLook(was, GasIds.Smoke), GasLook(now, GasIds.Smoke), 1f);
        if (steamInLook != null) Destroy(steamInLook);
        if (smokeInLook != null) Destroy(smokeInLook);
    }

    // The look a gas's effect is drawn in: the same prefab, or a copy dressed once for the colour in force.
    GameObject InLook(ref GameObject copy, GameObject prefab, LookTint tint)
    {
        if (prefab == null || !tint.Any) return prefab;
        if (copy != null) return copy;
        copy = Instantiate(prefab, transform);
        copy.name = prefab.name;
        copy.SetActive(false);
        tint.Dress(copy, null);
        return copy;
    }

    /// <summary>What's on a cell of embers or lava burns in: its spell's look, else the cover's or the element's.</summary>
    LookTint MoltenTint(Cell c, int x, int y)
    {
        if (c.Cover != CoverIds.Embers) return lavaLook;
        if (SpellFire(x, y, out var spell)) return spell;
        return emberLook.Any ? emberLook : fireLook;
    }

    // The rules switched: everything drawn from a colour is drawn again from the one in force.
    void RecolourWorld()
    {
        ReadWorldLooks();
        if (views == null) return;
        foreach (var v in views)
            if (v != null && (v.fireLevel > 0 || v.gasKind != null || v.coverId != null)) v.key = int.MinValue;
        fogBank?.Recolour();
        slicks.Recolour();
        thornClumps.Recolour();
        vineClumps.Recolour();
        var was = CurrentRules.Base;
        var now = CurrentRules.Effective;
        var water = TintBetween(was.ElementLook(ElementIds.Water), now.ElementLook(ElementIds.Water));
        waterSurface.Recolour(water);
        if (Ocean.Instance != null) Ocean.Instance.Recolour(water);
        if (valley != null)
        {
            valley.RecolourWater(water);
            valley.RecolourGround(was, now);
        }
    }
}
