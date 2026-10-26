using System.IO;
using UnityEditor;
using UnityEngine;

// Helpers the scene builders share for the assets they generate and the
// Asset Store pack files they use.
public static class EditorAssets
{
    /// <summary>The asset at <paramref name="path"/>, or a new one from <paramref name="create"/> saved there.</summary>
    public static T LoadOrCreate<T>(string path, System.Func<T> create) where T : Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;
        var made = create();
        AssetDatabase.CreateAsset(made, path);
        return made;
    }

    /// <summary>A Lit material asset at <paramref name="path"/> (made if missing) with these settings.</summary>
    public static Material LitMaterial(string path, Color color, float smoothness, Color? emission = null)
    {
        var mat = LoadOrCreate(path, () => new Material(Shader.Find(UrpMaterials.LitShader)));
        UrpMaterials.Configure(mat, color, smoothness, emission);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>Makes the folder for an asset path and lets the AssetDatabase see it.</summary>
    public static void EnsureFolderFor(string assetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
        AssetDatabase.Refresh();
    }

    public static GameObject LoadPrefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
}

// Where the Asset Store packs keep what the builders use (see AssetPacks.json;
// Prototype > Restore Asset Packs brings them back). Every builder treats a
// missing file as optional and falls back to something plainer.
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

    /// <summary>Olivier Girardot's free sound pack (only the files AssetPacks.json lists are imported).</summary>
    public const string FreeSounds = "Assets/Free Pack";
}
