// The ids from rules/*.json that the Unity code refers to by name, in one
// place. The rules themselves (what a spell does, how a material burns) stay
// in the JSON; these are only the names, so a typo is a compile error rather
// than a spell that silently does nothing.

/// <summary>Spell ids, as in rules/spells.json.</summary>
public static class SpellIds
{
    public const string FireBolt = "fire-bolt";
    public const string RayOfFrost = "ray-of-frost";
    public const string CreateWater = "create-water";
    public const string Grease = "grease";
    public const string ShockingGrasp = "shocking-grasp";
    public const string GustOfWind = "gust-of-wind";
    public const string Thunderwave = "thunderwave";

    /// <summary>The spell bar a new wizard starts with, in key order 1-6.</summary>
    public static string[] DefaultBar() =>
        new[] { FireBolt, RayOfFrost, CreateWater, Grease, ShockingGrasp, GustOfWind };
}

/// <summary>Terrain ids (a cell's <c>T</c>), as in rules/materials.json.</summary>
public static class TerrainIds
{
    public const string Grass = "grass";
    public const string Brush = "brush";
    public const string Tree = "tree";
    public const string Stump = "stump";
    public const string Rubble = "rubble";
    public const string Dirt = "dirt";
    public const string Pit = "pit";
    public const string Metal = "metal";
    public const string LogWall = "logwall";
}

/// <summary>Cover ids (a film on a cell), as in rules/materials.json.</summary>
public static class CoverIds
{
    public const string Oil = "oil";
    public const string Grease = "grease";
    public const string Ash = "ash";
}

/// <summary>Gas ids, as in rules/materials.json.</summary>
public static class GasIds
{
    public const string Steam = "steam";
    public const string Smoke = "smoke";
}
