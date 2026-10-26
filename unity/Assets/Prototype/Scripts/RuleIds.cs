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
    public const string Levitate = "levitate";
    public const string MoldEarth = "mold-earth";
    public const string MoldEarthHeap = "mold-earth-heap";
    public const string EarthenTrench = "earthen-trench";
    public const string WallOfStone = "wall-of-stone";
    public const string MoveEarth = "move-earth";
    public const string MistyStep = "misty-step";

    /// <summary>The spell bar a new wizard starts with (the Elements page), in key order 1-8.</summary>
    public static string[] DefaultBar() =>
        new[] { FireBolt, RayOfFrost, CreateWater, Grease, ShockingGrasp, GustOfWind, Thunderwave, Levitate };

    /// <summary>The second page of the bar (Earth), in key order 1-5: earth shaping.</summary>
    public static string[] EarthBar() =>
        new[] { MoldEarth, MoldEarthHeap, EarthenTrench, WallOfStone, MoveEarth };

    /// <summary>The third page of the bar (Arcane), in key order: magic that moves you or things.</summary>
    public static string[] ArcaneBar() =>
        new[] { MistyStep };
}

/// <summary>Spell shapes and effects (a spell's <c>shape</c> and <c>effect</c>), as in rules/spells.json.</summary>
public static class SpellShapes
{
    public const string Line = "line";
    public const string Burst = "burst";
    public const string LevitateEffect = "levitate";
    public const string TeleportEffect = "teleport";
}

/// <summary>Element ids (what a spell or a reaction brings to a cell), as in rules/reactions.json.</summary>
public static class ElementIds
{
    public const string Fire = "fire";
}

/// <summary>Entity ids, as in rules/entities.json.</summary>
public static class EntityIds
{
    public const string Wizard = "wizard";
    public const string Crate = "crate";
    public const string Barrel = "barrel";
    public const string Boulder = "boulder";
}

/// <summary>Item ids, as in rules/items.json (the yard's set pieces; ItemBuilder).</summary>
public static class ItemIds
{
    public const string GlowBallAmber = "glow-ball-amber";
    public const string GlowBallAzure = "glow-ball-azure";
    public const string GlowBallRose = "glow-ball-rose";
    public const string BullseyeLantern = "bullseye-lantern";
    public const string Spyglass = "spyglass";
    public const string SendingStone = "sending-stone";
    public const string DancingLights = "dancing-lights";
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
    public const string Rock = "rock";
    public const string Wood = "wood";
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
