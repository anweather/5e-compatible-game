using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// The defense field north of the yard (docs/BACKLOG.md 17; DefenseField runs
// it, World.Defense.cs is the game): a straight dirt path 60 m long, west to
// east, from a stone gate where the raiders come in to a flag with a lives
// banner, open meadow either side for the players to wall, dig and flood, and
// a start plate with a sign by the flag.
//
// It stands on the meadow 60 m north of the yard, where nothing else is: the
// village is 30 m and more to the west and south-west (around (-85, 45)), the
// first cabin 70 m south, the coast's path runs east from the yard's north-east
// corner toward the bridge (well south and east of it), the court, the river
// and the bridge are 60 m and more east, the tower and the lake far south, and
// the foothills only start climbing some 65 m further north. The path runs
// along one row of squares, so the raiders' way on open ground is a straight
// line. The field's rectangle is kept clear of trees, rocks and grass
// (NearDefenseField), and its path squares are dirt (OnPath, PathCells).
// Built from primitives and lit materials, like the tower and the court.
public static class DefenseFieldBuilder
{
    const string Generated = "Assets/Prototype/Generated";

    /// <summary>The gate's square centre and the flag's (x, z), on one row of 1.5 m squares, 60 m apart.</summary>
    public static readonly Vector2 Gate = new Vector2(-25.5f, 85.5f), Flag = new Vector2(34.5f, 85.5f);

    /// <summary>How far either side of the path the field reaches (m), and past its ends.</summary>
    public const float HalfWidth = 14f, EndRoom = 6f;

    // The start plate: south-west of the flag, off the path; its radius.
    static readonly Vector2 Plate = Flag + new Vector2(-5f, -6f);
    const float PlateRadius = 1.4f;

    /// <summary>Whether a point is within <paramref name="margin"/> of the field's rectangle (inside it with a negative one).</summary>
    public static bool NearDefenseField(Vector2 p, float margin) =>
        p.x > Gate.x - EndRoom - margin && p.x < Flag.x + EndRoom + margin && Mathf.Abs(p.y - Gate.y) < HalfWidth + margin;

    /// <summary>Whether a point is on the path's dirt: the row of squares from the gate to the flag.</summary>
    public static bool OnPath(Vector2 p) => p.x > Gate.x - 1f && p.x < Flag.x + 1f && Mathf.Abs(p.y - Gate.y) < 0.75f;

    /// <summary>The path's squares (world cells), painted as the dirt they are.</summary>
    public static IEnumerable<(int x, int y)> PathCells()
    {
        var layout = ValleyTerrain.Layout;
        foreach (var (x, y) in layout.Cells())
        {
            var (cx, cz) = layout.CellCenter(x, y);
            if (OnPath(new Vector2(cx, cz))) yield return (x, y);
        }
    }

    sealed class Looks
    {
        public Material stone, wood, flag, plate, banner;
    }

    /// <summary>Builds the field, named "Defense field", with its DefenseField.</summary>
    public static DefenseField Build(ChemGrid grid)
    {
        var looks = MakeMaterials();
        var root = new GameObject("Defense field").transform;
        root.position = At(Gate);
        var field = root.gameObject.AddComponent<DefenseField>();
        field.grid = grid;
        field.gate = Marker(root, "Defense gate square", At(Gate));
        field.flag = Marker(root, "Defense flag square", At(Flag));
        GateArch(root, looks);
        field.banner = FlagPole(root, looks);
        field.startPlate = StartPlate(root, looks);
        field.plateRadius = PlateRadius;
        field.kinds = new[] { RaiderIds.BoneHound, RaiderIds.AshCrab, RaiderIds.GiantSpider };
        field.species = new[] { Species("BoneHound"), Species("AshCrab"), Species("GiantSpider") };
        field.sizes = new[] { 1.2f, 1.6f, 1.4f };
        var shader = Shader.Find("Punchy/Creature");
        field.creatureMaterial = EditorAssets.LoadOrCreate(PropBuilder.Generated + "/Creature.mat", () => new Material(shader) { name = "Creature" });
        return field;
    }

    static Punchy.Fauna.CreatureSpecies Species(string name)
    {
        var species = AssetDatabase.LoadAssetAtPath<Punchy.Fauna.CreatureSpecies>("Assets/Prototype/ThirdParty/Punchy/Fauna/" + name + ".asset");
        if (species == null) Debug.LogWarning("Defense field: no species " + name);
        return species;
    }

    static Vector3 At(Vector2 p)
    {
        var at = new Vector3(p.x, 0f, p.y);
        at.y = ValleyTerrain.GroundAt(at);
        return at;
    }

    static Transform Marker(Transform root, string name, Vector3 at)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, true);
        t.position = at;
        return t;
    }

    // A rough stone arch over the path's west end: two pillars either side of it and a lintel.
    static void GateArch(Transform root, Looks looks)
    {
        var foot = At(Gate);
        const float gap = 2.2f, tall = 4.2f;
        foreach (float side in new[] { -1f, 1f })
        {
            var p = new Vector3(foot.x, 0f, foot.z + side * gap);
            p.y = ValleyTerrain.GroundAt(p);
            Solid(root, PrimitiveType.Cube, "Defense gate pillar", p + Vector3.up * (tall / 2f - 0.3f), new Vector3(1.1f, tall, 1.1f), looks.stone);
        }
        Solid(root, PrimitiveType.Cube, "Defense gate lintel", foot + Vector3.up * (tall - 0.1f), new Vector3(1.3f, 0.9f, gap * 2f + 1.6f), looks.stone);
    }

    // The flag at the path's east end: a pole, a red flag flying west toward the gate, and the lives on a board below.
    static TextMesh FlagPole(Transform root, Looks looks)
    {
        var foot = At(Flag);
        const float pole = 6f;
        Solid(root, PrimitiveType.Cylinder, "Defense flagpole", foot + Vector3.up * (pole / 2f), new Vector3(0.2f, pole / 2f, 0.2f), looks.wood);
        Piece(root, PrimitiveType.Cube, "Defense flag", foot + new Vector3(-1.1f, pole - 0.7f, 0f), new Vector3(2f, 1.2f, 0.05f), looks.flag);
        var board = Piece(root, PrimitiveType.Cube, "Defense banner board", foot + new Vector3(-0.2f, 2.4f, 0f), new Vector3(0.08f, 1.3f, 2.2f), looks.banner);
        // The words on the board's west face, toward the gate and the players.
        var words = new GameObject("Defense banner words");
        words.transform.SetParent(board.transform.parent, true);
        words.transform.SetPositionAndRotation(board.transform.position + new Vector3(-0.06f, 0f, 0f), Quaternion.LookRotation(Vector3.right));
        // Fitted to the longest it gets in a game (the lives and the wave), inside the board.
        return SignWords.Write(words, "LIVES 10", 0.05f, 1.9f, 1.1f, "LIVES 10\nWAVE 10/10");
    }

    // The start plate south-west of the flag, with a sign; returns the middle of the plate's top.
    static Vector3 StartPlate(Transform root, Looks looks)
    {
        var at = At(Plate) + Vector3.up * 0.2f;
        var plate = Piece(root, PrimitiveType.Cylinder, "Defense start plate", at + Vector3.down * 0.15f, new Vector3(PlateRadius * 2f, 0.15f, PlateRadius * 2f), looks.plate);
        plate.AddComponent<MeshCollider>();
        var signAt = At(Plate + new Vector2(-2.6f, 0.4f));
        var board = new GameObject("Defense sign").transform;
        board.SetParent(root, true);
        // Facing the path (north), where players come from.
        board.SetPositionAndRotation(signAt, Quaternion.LookRotation(Vector3.back));
        // A wider, taller board, and the words in short lines that fit it.
        foreach (float x in new[] { -1.2f, 1.2f })
            Local(board, PrimitiveType.Cylinder, new Vector3(x, 0.9f, 0f), new Vector3(0.12f, 0.9f, 0.12f), looks.wood);
        Local(board, PrimitiveType.Cube, new Vector3(0f, 1.75f, 0f), new Vector3(2.8f, 1.4f, 0.08f), looks.wood);
        var words = new GameObject("Defense sign words");
        words.transform.SetParent(board, false);
        words.transform.localPosition = new Vector3(0f, 1.75f, -0.06f);
        SignWords.Write(words, "THE DEFENSE FIELD\nStand on the plate\nto call the raiders.\nWall, dig, flood, burn:\nkeep them from the flag!", 0.03f, 2.5f, 1.2f);
        return at;
    }

    // A drawn piece the wizard bumps into (its own mesh as the collider).
    static void Solid(Transform root, PrimitiveType type, string name, Vector3 at, Vector3 scale, Material mat)
    {
        var go = Piece(root, type, name, at, scale, mat);
        go.AddComponent<MeshCollider>();
    }

    static GameObject Piece(Transform root, PrimitiveType type, string name, Vector3 at, Vector3 scale, Material mat)
    {
        var go = Primitives.Visual(type, name);
        go.transform.SetParent(root, true);
        go.transform.position = at;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static void Local(Transform parent, PrimitiveType type, Vector3 at, Vector3 scale, Material mat)
    {
        var go = Piece(parent, type, "Defense sign part", parent.position, scale, mat);
        go.transform.SetLocalPositionAndRotation(at, Quaternion.identity);
    }

    static Looks MakeMaterials() => new Looks
    {
        stone = Lit("DefenseStone", new Color(0.4f, 0.38f, 0.36f), 0.1f),
        wood = Lit("DefenseWood", new Color(0.42f, 0.29f, 0.18f), 0.1f),
        flag = Lit("DefenseFlag", new Color(0.85f, 0.1f, 0.1f), 0.2f, new Color(0.3f, 0.02f, 0.02f)),
        plate = Lit("DefensePlate", new Color(0.85f, 0.55f, 0.18f), 0.3f, new Color(0.25f, 0.12f, 0.02f)),
        banner = Lit("DefenseBanner", new Color(0.2f, 0.16f, 0.12f), 0.1f),
    };

    static Material Lit(string name, Color color, float smoothness, Color? emission = null) =>
        EditorAssets.LitMaterial($"{Generated}/{name}.mat", color, smoothness, emission);
}
