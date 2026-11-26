using System.IO;
using UnityEditor;
using UnityEngine;

// Helpers the scene builders share for the assets they generate and the
// Asset Store pack files they use.
public static class EditorAssets
{
    public static T LoadOrCreate<T>(string path, System.Func<T> create) where T : Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;
        var made = create();
        AssetDatabase.CreateAsset(made, path);
        return made;
    }

    public static Material LitMaterial(string path, Color color, float smoothness, Color? emission = null)
    {
        var mat = LoadOrCreate(path, () => new Material(Shader.Find(UrpMaterials.LitShader)));
        UrpMaterials.Configure(mat, color, smoothness, emission);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    public static void EnsureFolderFor(string assetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
        AssetDatabase.Refresh();
    }

    public static GameObject LoadPrefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
}

public static class PackPaths
{
    public const string StarterAssets = "Assets/Starter Assets/Runtime/ThirdPersonController/Prefabs";

    public const string Effects = "Assets/UnityTechnologies/ParticlePack/EffectExamples";
    public const string FireFx = Effects + "/Fire & Explosion Effects/Prefabs";
    public const string SmokeFx = Effects + "/Smoke & Steam Effects/Prefabs";
    public const string MiscFx = Effects + "/Misc Effects/Prefabs";
    public const string MagicFx = Effects + "/Magic Effects/Prefabs";
    public const string WaterFx = Effects + "/Water Effects/Prefabs";
    public const string GoopFx = Effects + "/Goop Effects/Prefabs";

    public const string TerrainSamples = "Assets/TerrainSampleAssets";
    public const string TerrainDemo = "Assets/TerrainDemoScene_URP/Prefabs";

    public const string FreeSounds = "Assets/Free Pack";
}
