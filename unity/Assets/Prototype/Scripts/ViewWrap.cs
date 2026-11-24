using Chem;
using UnityEngine;

// The view across the seams of a world that wraps (docs/design/hills.md, "The view across the seams"): the core folds
// every cell into [0, W) x [0, H), and the view draws each thing at the copy of its place nearest the camera. The
// wizard and the camera walk on in the view's own unfolded coordinates (past the east seam x runs on past W metres), and
// whatever the view puts at a cell (CellCenter), a chunk terrain or a known chunk of the far ring goes at the copy within
// half a world of the camera, so the land over a seam is the land at the other edge, drawn where it meets. Data stays
// keyed by folded cells and chunks; only positions are near. On a world that doesn't wrap every call returns what it's
// given.
public static class ViewWrap
{
    static int w, h;
    static float periodX, periodZ;
    static int frame = -1;
    static Vector3 eye;

    /// <summary>Whether the world in play wraps east-west (and NorthSouth, north-south too).</summary>
    public static bool EastWest => periodX > 0f;
    public static bool NorthSouth => periodZ > 0f;

    /// <summary>The world's period in metres east-west and north-south (0 where it doesn't wrap).</summary>
    public static float PeriodX => periodX;
    public static float PeriodZ => periodZ;

    // Each Play mode starts unwrapped, with the domain reload off too.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => Set(null, 1f);

    /// <summary>A new world in play (ChemGrid's): its wrap, at this cell size.</summary>
    public static void Set(World world, float cellSize)
    {
        w = world != null && world.WrapX ? world.W : 0;
        h = world != null && world.WrapY ? world.H : 0;
        periodX = w * cellSize;
        periodZ = h * cellSize;
        frame = -1;
    }

    /// <summary>The camera's position this frame (read once a frame): what "near" is measured from.</summary>
    public static Vector3 Eye
    {
        get
        {
            if (Time.frameCount != frame)
            {
                frame = Time.frameCount;
                var cam = Camera.main;
                if (cam != null) eye = cam.transform.position;
            }
            return eye;
        }
    }

    /// <summary>A world x moved by whole periods to the copy nearest the camera.</summary>
    public static float NearX(float x) => periodX > 0f ? x + periodX * Mathf.Round((Eye.x - x) / periodX) : x;

    /// <summary>A world z moved by whole periods to the copy nearest the camera.</summary>
    public static float NearZ(float z) => periodZ > 0f ? z + periodZ * Mathf.Round((Eye.z - z) / periodZ) : z;

    /// <summary>A world point at the copy nearest the camera (its height as it is).</summary>
    public static Vector3 Near(Vector3 p) => periodX > 0f || periodZ > 0f ? new Vector3(NearX(p.x), p.y, NearZ(p.z)) : p;

    /// <summary>A cell x folded into the world (as World.FoldX).</summary>
    public static int FoldX(int x) => w > 0 ? Mod(x, w) : x;

    /// <summary>A cell y folded into the world (as World.FoldY).</summary>
    public static int FoldY(int y) => h > 0 ? Mod(y, h) : y;

    /// <summary>
    /// A chunk terrain's (i, j) folded into a layout <paramref name="chunksX"/> x <paramref name="chunksZ"/> chunks (the
    /// map's, one terrain a cell chunk), where the world wraps.
    /// </summary>
    public static (int i, int j) FoldChunk((int i, int j) at, int chunksX, int chunksZ) =>
        (w > 0 ? Mod(at.i, chunksX) : at.i, h > 0 ? Mod(at.j, chunksZ) : at.j);

    /// <summary>Chunks between two chunk indices along an axis of n, the short way where the world wraps that way.</summary>
    public static int ChunkDistance(int a, int b, int n, bool alongX)
    {
        int d = Mathf.Abs(a - b);
        if (alongX ? w <= 0 : h <= 0) return d;
        d %= n;
        return Mathf.Min(d, n - d);
    }

    static int Mod(int a, int n)
    {
        int f = a % n;
        return f < 0 ? f + n : f;
    }
}
