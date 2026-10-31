using Chem;
using UnityEngine;

// Spell golf's hole in the valley (docs/BACKLOG.md 24; World.Golf): the tee, the
// flag and its hole, the big ball and the HUD's stroke line, all drawn from what
// the core says. The world starts with the ball on the tee (ChemGrid.MapWorld), on
// every machine alike, so there's no command to send. Presentation only; made by
// ChemGrid with the first frame of a world that has a hole, on a child of the grid.
public class GolfCourse : MonoBehaviour
{
    const float BallSize = 0.7f, HoleSize = 1f, TeeSize = 1.3f, PoleTall = 3.2f, FlagLong = 0.9f, FlagHigh = 0.55f;
    // How high a flying ball arcs, in metres, and how far from the course the HUD line shows, in cells.
    const float FlightTop = 2.6f;
    const int NearCells = 16;
    // Under the defense field's line, whose field lies just south of the course.
    const float HudLineTop = 90f;

    ChemGrid grid;
    Transform ball;
    GUIStyle hudStyle;
    Material[] materials;
    bool wasHoled;

    World World => grid.World;

    /// <summary>The course on a new child of <paramref name="grid"/>.</summary>
    public static GolfCourse On(ChemGrid grid)
    {
        var go = new GameObject("Golf course");
        go.transform.SetParent(grid.transform, false);
        var course = go.AddComponent<GolfCourse>();
        course.Lay(grid);
        return course;
    }

    /// <summary>Where the ball is drawn, for the probe.</summary>
    public Vector3 BallAt => ball.position;

    void Lay(ChemGrid on)
    {
        grid = on;
        var g = World.Golf;
        var turf = UrpMaterials.NewLit("Golf tee", new Color(0.42f, 0.68f, 0.3f), 0.15f);
        var dark = UrpMaterials.NewLit("Golf hole", new Color(0.015f, 0.015f, 0.02f), 0f);
        var white = UrpMaterials.NewLit("Golf ball", new Color(0.93f, 0.93f, 0.9f), 0.55f);
        var red = UrpMaterials.NewLit("Golf flag", new Color(0.85f, 0.1f, 0.08f), 0.2f);
        materials = new[] { turf, dark, white, red };

        var tee = Ground(g.TeeX, g.TeeY);
        Part(PrimitiveType.Cube, "Tee", tee + Vector3.up * 0.03f, new Vector3(TeeSize, 0.08f, TeeSize), turf);
        var hole = Ground(g.HoleX, g.HoleY);
        Part(PrimitiveType.Cylinder, "Hole", hole + Vector3.up * 0.03f, new Vector3(HoleSize, 0.02f, HoleSize), dark);
        Part(PrimitiveType.Cylinder, "Flag pole", hole + Vector3.up * (PoleTall / 2f), new Vector3(0.07f, PoleTall / 2f, 0.07f), white);
        Part(PrimitiveType.Cube, "Flag", hole + new Vector3(FlagLong / 2f, PoleTall - FlagHigh / 2f, 0f), new Vector3(FlagLong, FlagHigh, 0.03f), red);
        ball = Part(PrimitiveType.Sphere, "Golf ball", BallSpot(g), Vector3.one * BallSize, white);
    }

    Vector3 Ground(int x, int y)
    {
        var at = grid.CellCenter(x, y);
        at.y = grid.GroundAt(at);
        return at;
    }

    Vector3 BallSpot(GolfState g) => Ground(g.X, g.Y) + Vector3.up * (BallSize / 2f + (g.Carry > 0 ? Flight(g) : 0f));

    // Partway up its arc, by how many cells it has left to fly against a throw's carry.
    float Flight(GolfState g) => FlightTop * Mathf.Sin(Mathf.Clamp01(g.Carry / (World.Rules.Sim.Golf.ThrowCarry * 2f)) * Mathf.PI);

    Transform Part(PrimitiveType shape, string name, Vector3 at, Vector3 size, Material material)
    {
        var part = GameObject.CreatePrimitive(shape);
        part.name = name;
        part.transform.SetParent(transform, true);
        part.transform.position = at;
        part.transform.localScale = size;
        Destroy(part.GetComponent<Collider>());
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part.transform;
    }

    void Update()
    {
        var g = World?.Golf;
        if (g == null) return;
        float speed = grid.cellSize * World.Rules.Sim.TicksPerSecond / World.Rules.Sim.Golf.Every * 1.25f;
        ball.position = Vector3.MoveTowards(ball.position, BallSpot(g), speed * Time.deltaTime);
        ball.gameObject.SetActive(!g.Holed);
        if (g.Holed && !wasHoled) GameSounds.Play(SoundIds.Splash, ball.position);
        wasHoled = g.Holed;
    }

    /// <summary>The HUD line: the strokes so far, or how it ended.</summary>
    public string HudLine()
    {
        var g = World?.Golf;
        if (g == null) return "";
        if (g.Holed) return $"Spell golf: in the hole in {g.Strokes}!";
        if (g.Strokes == 0) return "Spell golf: hit the ball with Gust of Wind, or Raise Earth under it";
        return g.Strokes == 1 ? "Spell golf: 1 stroke" : $"Spell golf: {g.Strokes} strokes";
    }

    bool Near()
    {
        var g = World?.Golf;
        var wizard = WizardRig.FindLocal();
        if (g == null || wizard == null) return false;
        var at = grid.CellAt(wizard.transform.position);
        return at.x >= Mathf.Min(g.TeeX, g.HoleX) - NearCells && at.x <= Mathf.Max(g.TeeX, g.HoleX) + NearCells
            && at.y >= Mathf.Min(g.TeeY, g.HoleY) - NearCells && at.y <= Mathf.Max(g.TeeY, g.HoleY) + NearCells;
    }

    void OnGUI()
    {
        HudScale.Apply();
        if (!EventLog.HudVisible || !Near()) return;
        hudStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, richText = true };
        GUI.Label(new Rect(HudScale.Width / 2f - 400f, HudLineTop, 800f, 36f), $"<b>{HudLine()}</b>", hudStyle);
    }

    void OnDestroy()
    {
        if (materials == null) return;
        foreach (var material in materials) Destroy(material);
    }
}
