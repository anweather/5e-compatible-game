using UnityEngine;

// The land as the world sees it (docs/design/architecture-review.md step 10): the faces of the land's parts that the
// grid, the dressing, the water and the props ask of them, so World never names the land. Each part sets its face
// where it sets its own Instance and clears it where it clears that (MapTerrains, FarRing, Ocean, WaterCurrents); a
// face whose component has been destroyed reads as none, as the Instance did. How far the terrains draw their detail
// (DetailMetres) is the same for every map, so it lives here.
public interface ITerrainFace
{
    TerrainData Template { get; }

    Terrain TerrainAt(Vector3 p);
}

public interface IFarLandFace
{
    int KnownChunks { get; }
}

public interface ISeaFace
{
    void Recolour(Color tint);
}

public interface ICurrentsFace
{
    Vector3 At(Vector3 p);
}

public static class LandFaces
{
    public const float DetailMetres = 80f;

    static ITerrainFace terrains;
    static IFarLandFace farLand;
    static ISeaFace sea;
    static ICurrentsFace currents;

    public static ITerrainFace Terrains
    {
        get => Live(terrains);
        set => terrains = value;
    }

    public static IFarLandFace FarLand
    {
        get => Live(farLand);
        set => farLand = value;
    }

    public static ISeaFace Sea
    {
        get => Live(sea);
        set => sea = value;
    }

    public static ICurrentsFace Currents
    {
        get => Live(currents);
        set => currents = value;
    }

    static T Live<T>(T face) where T : class => face is Object part && part == null ? null : face;
}
