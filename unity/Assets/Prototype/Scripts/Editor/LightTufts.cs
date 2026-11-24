using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Cheaper copies of the terrain pack's grass tufts, for the terrains' detail grass and the brush clumps drawn from the same
// prefabs (CellDressing). A pack tuft is a few dozen to a few hundred cards (a blade or a few, cut out of the texture by
// its alpha), each bent in three to fifteen quads: Grass_B is 158 cards in 1,136 triangles, Grass_C 43 in 1,002, and
// at 120 m of grass that was 6.5 M triangles a frame on the nine (docs/perf/2026-11-23-tufts.md). Here every card is
// kept, at the same place with the same texture, as one quad from its foot to its tip, or two when the card is tall
// and bent; so the tuft is as full as before, and only a card's bend is straightened. The copies are made by the valley's
// build (ValleyBuilder.LoadDetails) into Generated/, out of git, with the pack's own materials.
public static class LightTufts
{
    public const string Folder = "Assets/Prototype/Generated/Ground/Tufts";

    /// <summary>The pack tufts lightened, and how many quads a card keeps; the rest are used as the pack has them.</summary>
    public static readonly IReadOnlyDictionary<string, int> Lightened = new Dictionary<string, int>
    {
        ["Grass_A"] = 1, ["Grass_B"] = 1, ["Grass_C"] = 2, ["Fern_A"] = 2,
    };

    /// <summary>Whether the ground's template (built before LightTufts) still grows a pack tuft that has a light copy.</summary>
    public static bool GroundHasPackTufts()
    {
        var template = AssetDatabase.LoadAssetAtPath<TerrainData>(Template);
        return template != null && template.detailPrototypes.Any(d => d.prototype != null && Lightened.ContainsKey(d.prototype.name)
            && AssetDatabase.GetAssetPath(d.prototype).StartsWith(PackPaths.TerrainSamples, System.StringComparison.Ordinal));
    }

    /// <summary>The ground's template, which the valley's build makes (ValleyBuilder).</summary>
    public const string Template = "Assets/Prototype/Generated/Ground/Template.asset";

    /// <summary>
    /// The light copy of a pack tuft prefab (made or brought up to date in Folder), or the pack's own when it isn't one
    /// of Lightened.
    /// </summary>
    public static GameObject For(GameObject pack)
    {
        if (pack == null || !Lightened.TryGetValue(pack.name, out int segments)) return pack;
        var filter = pack.GetComponent<MeshFilter>();
        var renderer = pack.GetComponent<MeshRenderer>();
        if (filter == null || filter.sharedMesh == null || renderer == null) return pack;
        EditorAssets.EnsureFolderFor($"{Folder}/x");
        string meshPath = $"{Folder}/{pack.name}.asset", prefabPath = $"{Folder}/{pack.name}.prefab";
        var light = Lighten(filter.sharedMesh, segments);
        var mesh = EditorAssets.LoadOrCreate(meshPath, () => new Mesh());
        EditorUtility.CopySerialized(light, mesh);
        mesh.name = pack.name;
        Object.DestroyImmediate(light);
        EditorUtility.SetDirty(mesh);

        var go = new GameObject(pack.name);
        try
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = renderer.sharedMaterials;
            r.shadowCastingMode = renderer.shadowCastingMode;
            r.receiveShadows = renderer.receiveShadows;
            return PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>
    /// A copy of a tuft with each of its cards (the triangles joined by shared vertices) remade as <paramref name="segments"/>
    /// quads (1 or 2) through its own corner (and middle) vertices, found by their texture coordinates; a card already that
    /// small is kept as it is. Only the vertices used are kept.
    /// </summary>
    public static Mesh Lighten(Mesh source, int segments)
    {
        var uv = new List<Vector2>();
        source.GetUVs(0, uv);
        var positions = source.vertices;
        var normals = source.normals;
        int[] tris = source.triangles;
        var kept = new List<int>();
        foreach (var card in Cards(tris, positions.Length))
        {
            if (card.Count <= segments * 6)
            {
                kept.AddRange(card);
                continue;
            }
            var corners = Quads(card.Distinct().ToList(), uv, segments);
            int before = kept.Count;
            for (int s = 0; s + 1 < corners.Count; s++)
            {
                var (l0, r0) = corners[s];
                var (l1, r1) = corners[s + 1];
                if (l0 != l1 && l0 != r1 && l1 != r1) Face(kept, l0, l1, r1, positions, normals);
                if (l0 != r1 && r0 != r1 && l0 != r0) Face(kept, l0, r1, r0, positions, normals);
            }
            if (kept.Count == before) kept.AddRange(card);
        }
        return Compact(source, kept);
    }

    /// <summary>How many cards (triangles joined by shared vertices) a mesh has.</summary>
    public static int CardCount(Mesh mesh) => Cards(mesh.triangles, mesh.vertexCount).Count;

    // The triangles of each card: those joined by shared vertices, in order of their first triangle.
    static List<List<int>> Cards(int[] tris, int vertices)
    {
        var root = Enumerable.Range(0, vertices).ToArray();
        int Find(int a)
        {
            while (root[a] != a) a = root[a] = root[root[a]];
            return a;
        }
        for (int t = 0; t < tris.Length; t += 3)
        {
            root[Find(tris[t + 1])] = Find(tris[t]);
            root[Find(tris[t + 2])] = Find(tris[t]);
        }
        var cards = new Dictionary<int, List<int>>();
        var order = new List<List<int>>();
        for (int t = 0; t < tris.Length; t += 3)
        {
            int r = Find(tris[t]);
            if (!cards.TryGetValue(r, out var card))
            {
                cards[r] = card = new List<int>();
                order.Add(card);
            }
            card.Add(tris[t]);
            card.Add(tris[t + 1]);
            card.Add(tris[t + 2]);
        }
        return order;
    }

    // A card's rows of (left, right) vertices from its foot to its tip: the corners of its patch of texture (the extremes
    // of u ± v), and between them the vertices nearest the middle of each side.
    static List<(int left, int right)> Quads(List<int> verts, List<Vector2> uv, int segments)
    {
        int Most(System.Func<Vector2, float> score) => verts.OrderByDescending(i => score(uv[i])).ThenBy(i => i).First();
        int footLeft = Most(p => -p.x - p.y), footRight = Most(p => p.x - p.y);
        int tipLeft = Most(p => -p.x + p.y), tipRight = Most(p => p.x + p.y);
        var rows = new List<(int, int)> { (footLeft, footRight) };
        for (int s = 1; s < segments; s++)
        {
            float t = s / (float)segments;
            rows.Add((Nearest(verts, uv, Vector2.Lerp(uv[footLeft], uv[tipLeft], t)), Nearest(verts, uv, Vector2.Lerp(uv[footRight], uv[tipRight], t))));
        }
        rows.Add((tipLeft, tipRight));
        return rows;
    }

    static int Nearest(List<int> verts, List<Vector2> uv, Vector2 at) => verts.OrderBy(i => (uv[i] - at).sqrMagnitude).ThenBy(i => i).First();

    // A triangle wound to face the way its vertices' normals do, as the card's own triangles were (the material lights a
    // card's back by its winding).
    static void Face(List<int> into, int a, int b, int c, Vector3[] positions, Vector3[] normals)
    {
        var facing = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
        bool flip = normals.Length > 0 && Vector3.Dot(facing, normals[a] + normals[b] + normals[c]) < 0f;
        into.Add(a);
        into.Add(flip ? c : b);
        into.Add(flip ? b : c);
    }

    // The source's vertices that the triangles use, in their order, with every channel the source has in the source's own
    // vertex layout (its uvs two wide), so the grass draws with the pipeline states it always had (the PSO collections).
    static Mesh Compact(Mesh source, List<int> tris)
    {
        var used = tris.Distinct().OrderBy(i => i).ToList();
        var index = new Dictionary<int, int>();
        for (int n = 0; n < used.Count; n++) index[used[n]] = n;
        List<T> Pick<T>(IList<T> all) => all.Count == 0 ? new List<T>() : used.Select(i => all[i]).ToList();
        var mesh = new Mesh { name = source.name };
        mesh.SetVertexBufferParams(used.Count, source.GetVertexAttributes());
        mesh.SetVertices(Pick(source.vertices));
        if (source.normals.Length > 0) mesh.SetNormals(Pick(source.normals));
        if (source.tangents.Length > 0) mesh.SetTangents(Pick(source.tangents));
        if (source.colors.Length > 0) mesh.SetColors(Pick(source.colors));
        for (int channel = 0; channel < 4; channel++)
        {
            var attribute = UnityEngine.Rendering.VertexAttribute.TexCoord0 + channel;
            if (!source.HasVertexAttribute(attribute)) continue;
            if (source.GetVertexAttributeDimension(attribute) == 2)
            {
                var uvs = new List<Vector2>();
                source.GetUVs(channel, uvs);
                mesh.SetUVs(channel, Pick(uvs));
            }
            else
            {
                var uvs = new List<Vector4>();
                source.GetUVs(channel, uvs);
                mesh.SetUVs(channel, Pick(uvs));
            }
        }
        mesh.SetTriangles(tris.Select(i => index[i]).ToList(), 0);
        mesh.bounds = source.bounds;
        mesh.UploadMeshData(false);
        return mesh;
    }
}
