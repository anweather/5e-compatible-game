using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Network ids that come out the same on every machine and every rebuild.
//
// Netcode knows a network prefab, and an in-scene NetworkObject, by its
// GlobalObjectIdHash: a hash of Unity's GlobalObjectId, which is the file's
// GUID plus the NetworkObject's fileID inside that file. The thing prefabs
// (Generated/Things/) and the yard scene aren't in git: each machine builds
// them, and Unity picks fileIDs (and a new asset's GUID) at random. So two
// machines disagreed on every hash, and the host refused the other's join
// (ForceSamePrefabs compares a hash of all the prefab hashes); the grid's
// in-scene NetworkObject wouldn't have matched either.
//
// Here both inputs come from the asset's path instead: the GUID through a
// .meta written before the asset is first saved (the yard scene's .meta does
// the same), and the NetworkObject's fileID by rewriting that one id in the
// saved file. Netcode then works out the same hash everywhere. Tracked
// prefabs (NetWizard) don't need this: their ids are in git.
public static class StableNetworkIds
{
    public static string GuidFor(string path)
    {
        var sb = new StringBuilder(32);
        foreach (byte b in Digest("guid:" + path)) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    public static long FileIdFor(string path, string objectName = "")
    {
        long id = BitConverter.ToInt64(Digest("fileID:" + path + ":" + objectName), 0);
        return (id & long.MaxValue) | (1L << 61);
    }

    static byte[] Digest(string text)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(Encoding.UTF8.GetBytes(text));
    }

    public static void PrepareAsset(string path)
    {
        string want = GuidFor(path);
        string have = AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets);
        if (!string.IsNullOrEmpty(have) && have != want) AssetDatabase.DeleteAsset(path);
        if (File.Exists(path)) return;
        File.WriteAllText(path + ".meta",
            "fileFormatVersion: 2\nguid: " + want + "\nPrefabImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
    }

    public static GameObject PinPrefab(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        long have = FileIdOf(prefab.GetComponent<NetworkObject>());
        long want = FileIdFor(path);
        if (have != want)
        {
            File.WriteAllText(path, Replace(File.ReadAllText(path), have, want));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        Stamp(prefab.GetComponent<NetworkObject>());
        EditorUtility.SetDirty(prefab);
        AssetDatabase.SaveAssetIfDirty(prefab);
        return prefab;
    }

    public static void PinScene(string path)
    {
        string text = null;
        var named = new HashSet<string>();
        foreach (var networkObject in SceneNetworkObjects(path))
        {
            if (PrefabUtility.IsPartOfPrefabInstance(networkObject)) continue;
            if (!named.Add(networkObject.name)) throw new InvalidOperationException($"Two NetworkObjects in {path} are called {networkObject.name}; their network ids would clash. Rename one.");
            long have = FileIdOf(networkObject);
            long want = FileIdFor(path, networkObject.name);
            if (have == want) continue;
            text = Replace(text ?? File.ReadAllText(path), have, want);
        }
        if (text != null)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }
        foreach (var networkObject in SceneNetworkObjects(path)) Stamp(networkObject);
        EditorSceneManager.SaveScene(SceneManager.GetSceneByPath(path));
    }

    public static List<string> Unstable(string scenePath, NetworkPrefabsList generated)
    {
        var problems = new List<string>();
        if (generated == null) problems.Add("no generated network prefab list");
        else
            foreach (var entry in generated.PrefabList)
                if (!IsPinned(entry.Prefab)) problems.Add(entry.Prefab != null ? AssetDatabase.GetAssetPath(entry.Prefab) : "a missing prefab");
        if (!File.Exists(scenePath)) problems.Add(scenePath + " (missing)");
        else problems.AddRange(UnstableInScene(scenePath, File.ReadAllText(scenePath)));
        return problems;
    }

    static bool IsPinned(GameObject prefab)
    {
        var networkObject = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
        if (networkObject == null) return false;
        string path = AssetDatabase.GetAssetPath(prefab);
        return AssetDatabase.AssetPathToGUID(path) == GuidFor(path) && FileIdOf(networkObject) == FileIdFor(path);
    }

    internal static IEnumerable<string> UnstableInScene(string path, string yaml)
    {
        var names = new Dictionary<string, string>();
        var networkObjects = new List<(string id, string gameObject)>();
        foreach (var block in yaml.Split(new[] { "\n--- " }, StringSplitOptions.None))
        {
            var header = Regex.Match(block, @"^!u!(\d+) &(\d+)(.*)");
            if (!header.Success || header.Groups[3].Value.Contains("stripped")) continue;
            if (header.Groups[1].Value == "1")
                names[header.Groups[2].Value] = Regex.Match(block, @"\n  m_Name: (.*)").Groups[1].Value;
            else if (block.Contains("m_EditorClassIdentifier: Unity.Netcode.Runtime::Unity.Netcode.NetworkObject"))
                networkObjects.Add((header.Groups[2].Value, Regex.Match(block, @"\n  m_GameObject: \{fileID: (\d+)\}").Groups[1].Value));
        }
        foreach (var (id, gameObject) in networkObjects)
        {
            names.TryGetValue(gameObject, out var name);
            if (id != FileIdFor(path, name ?? "").ToString(CultureInfo.InvariantCulture)) yield return $"{path}: {name}";
        }
    }

    static NetworkObject[] SceneNetworkObjects(string path) =>
        Array.FindAll(Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None),
            n => n.gameObject.scene.path == path);

    public static long FileIdOf(Object obj) => (long)GlobalObjectId.GetGlobalObjectIdSlow(obj).targetObjectId;

    static string Replace(string yaml, long from, long to) =>
        Regex.Replace(yaml, @"(?<=&|fileID: )" + from.ToString(CultureInfo.InvariantCulture) + @"(?!\d)", to.ToString(CultureInfo.InvariantCulture));

    static void Stamp(NetworkObject networkObject)
    {
        var onValidate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
        onValidate?.Invoke(networkObject, null);
        EditorUtility.SetDirty(networkObject);
    }
}
