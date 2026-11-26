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

    public static bool EastWest => periodX > 0f;
    public static bool NorthSouth => periodZ > 0f;

    public static float PeriodX => periodX;
    public static float PeriodZ => periodZ;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => Set(null, 1f);

    public static void Set(World world, float cellSize) =>
        Set(world != null && world.WrapX ? world.W : 0, world != null && world.WrapY ? world.H : 0, cellSize);

    public static void Set(int wrapW, int wrapH, float cellSize)
    {
        w = wrapW;
        h = wrapH;
        periodX = w * cellSize;
        periodZ = h * cellSize;
        frame = -1;
    }

    public static Vector3? EyeAt { get; set; }

    public static Vector3 Eye
    {
        get
        {
            if (EyeAt.HasValue) return EyeAt.Value;
            if (Time.frameCount != frame)
            {
                frame = Time.frameCount;
                var cam = Camera.main;
                if (cam != null) eye = cam.transform.position;
            }
            return eye;
        }
    }

    public static float NearX(float x) => periodX > 0f ? x + periodX * Mathf.Round((Eye.x - x) / periodX) : x;

    public static float NearZ(float z) => periodZ > 0f ? z + periodZ * Mathf.Round((Eye.z - z) / periodZ) : z;

    public static Vector3 Near(Vector3 p) => periodX > 0f || periodZ > 0f ? new Vector3(NearX(p.x), p.y, NearZ(p.z)) : p;

    public static int FoldX(int x) => w > 0 ? Mod(x, w) : x;

    public static int FoldY(int y) => h > 0 ? Mod(y, h) : y;

    public static (int i, int j) FoldChunk((int i, int j) at, int chunksX, int chunksZ) =>
        (w > 0 ? Mod(at.i, chunksX) : at.i, h > 0 ? Mod(at.j, chunksZ) : at.j);

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
