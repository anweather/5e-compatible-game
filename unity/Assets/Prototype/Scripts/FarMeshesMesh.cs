using UnityEngine;

// The far ring's plain mesh arrays (FarMeshes.Data, in Prototype.Plans) made into a Unity Mesh: the one step of the far
// ring's meshes that needs the engine, kept in the game so the plan builds and tests in dotnet.
public static class FarMeshesMesh
{
    public static Mesh ToMesh(this FarMeshes.Data data, string name)
    {
        var mesh = new Mesh { name = name };
        if (data.Vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(data.Vertices);
        mesh.SetNormals(data.Normals);
        mesh.SetColors(data.Colors);
        mesh.SetTriangles(data.Triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
