using UnityEngine;

// What a generated mesh should be, for the closeup probe and the tests (issue #47).
public static class MeshChecks
{
    /// <summary>
    /// The share of triangles wound against their own normals, by area: their front faces point in, so a
    /// camera outside sees through them to the far side. 0 for a mesh that's right way out.
    /// </summary>
    public static float BackwardShare(Mesh mesh)
    {
        var points = mesh.vertices;
        var normals = mesh.normals;
        if (normals.Length != points.Length) return 0f;
        float backward = 0f, total = 0f;
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            var triangles = mesh.GetTriangles(sub);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                var face = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                float area = face.magnitude;
                total += area;
                if (Vector3.Dot(face, normals[a] + normals[b] + normals[c]) < 0f) backward += area;
            }
        }
        return total > 0f ? backward / total : 0f;
    }
}
