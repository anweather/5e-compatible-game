using System.IO;
using Chem;
using UnityEngine;

// The valley's terrain as the world sees it (docs/design/architecture-review.md step 10): what the grid, the dressing
// and the land's water ask of the valley (ChemGrid.valley), so World never names the village. ValleyChemistry is the one
// valley; it fills in every member. The land the map's file says (LandOf), kept once per world size, and how high a
// burning tree's fire sits (FireHeight) are the same for any valley, so they live here.
public abstract class ValleyFace : MonoBehaviour
{
    protected static LandChunks landFromFile;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetLand()
    {
        landFromFile = null;
    }

    public static void ForgetLand() => landFromFile = null;

    public static LandChunks LandOf(World world)
    {
        if (landFromFile == null || landFromFile.W != world.W || landFromFile.H != world.H)
        {
            string path = Path.Combine(ChemGrid.Folders.World, "land.json");
            var map = MapLand.Current;
            landFromFile = map != null && (map.FromFile || map.Land.IsFromSource) && map.Land.W == world.W && map.Land.H == world.H ? map.Land
                : File.Exists(path) ? LandChunks.Read(File.ReadAllText(path), world.W, world.H) : null;
            if (landFromFile == null) Debug.LogWarning($"No land for a {world.W}×{world.H} world in {path}: water won't run down the hills");
        }
        return landFromFile;
    }

    public static float FireHeight(string terrainType) => terrainType switch
    {
        TerrainIds.Tree => 2.6f,
        TerrainIds.LogWall => 1.4f,
        TerrainIds.Deadfall => 0.5f,
        _ => 0f,
    };

    public abstract float LakeLevel { get; }

    public abstract bool FlatUnderWalls { get; }

    public abstract bool GroundMoving { get; }

    public abstract void Bind(World world);

    public abstract void LandMoved(int x, int y, int reach);

    public abstract float BaseHeight(int x, int y);

    public abstract float ShownGround(int x, int y);

    public abstract float GroundAt(Vector3 p);

    public abstract void AdvanceShaping(float dt);

    public abstract bool IsLake(Cell c, int x, int y);

    public abstract float LakeLevelOf(Cell c, int x, int y);

    public abstract void CellChanged(int x, int y, string from, string to, bool reshaped);

    public abstract void PaneCracked(int key, bool cracked);

    public abstract void RecolourWater(Color tint);

    public abstract void RecolourGround(Rules was, Rules now);

    public abstract bool NeedsTreeDrawn(int x, int y);

    public abstract bool TrunkAt(int x, int y, out Vector3 at);

    public abstract bool TreeModel(int x, int y, out GameObject prefab, out Vector3 scale);

    public abstract bool FelledTree(int x, int y, out GameObject prefab, out Vector3 scale);
}
