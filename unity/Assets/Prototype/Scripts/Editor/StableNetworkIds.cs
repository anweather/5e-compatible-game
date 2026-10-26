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
    /// <summary>The GUID a generated asset at <paramref name="path"/> gets on every machine.</summary>
    public static string GuidFor(string path)
    {
        var sb = new StringBuilder(32);
        foreach (byte b in Digest("guid:" + path)) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>
    /// The fileID of the NetworkObject on the object named <paramref name="objectName"/> (empty for a
    /// prefab's root) in the file at <paramref name="path"/>: positive and large, like Unity's own.
    /// </summary>
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

    // --- prefabs -------------------------------------------------------------

    /// <summary>
    /// Before a generated prefab is saved at <paramref name="path"/>: it will get its stable GUID.
    /// One saved by an older builder, under some other GUID, is deleted first.
    /// </summary>
    public static void PrepareAsset(string path)
    {
        string want = GuidFor(path);
        string have = AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets);
        if (!string.IsNullOrEmpty(have) && have != want) AssetDatabase.DeleteAsset(path);
        if (File.Exists(path)) return;
        File.WriteAllText(path + ".meta",
            "fileFormatVersion: 2\nguid: " + want + "\nPrefabImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
    }

    /// <summary>
    /// After a network prefab is saved at <paramref name="path"/>: gives its NetworkObject its stable
    /// fileID, and saves the hash Netcode works out from it into the asset. Returns the prefab, reloaded.
    /// </summary>
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

    // --- the open scene ------------------------------------------------------

    /// <summary>
    /// The open scene, already saved at <paramref name="path"/>: gives each of its NetworkObjects its
    /// stable fileID (closing and reopening the scene around the rewrite, so the Editor never sees
    /// an open scene change on disk), then saves the hashes Netcode works out. The id comes from
    /// the object's name, so no two of them may share one.
    /// </summary>
    public static void PinScene(string path)
    {
        string text = null;
        var named = new HashSet<string>();
        foreach (var networkObject in SceneNetworkObjects(path))
        {
            if (PrefabUtility.IsPartOfPrefabInstance(networkObject)) continue; // its id comes from its prefab
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

    // --- checking a built yard -----------------------------------------------

    /// <summary>
    /// What in a built scene and its generated network prefabs isn't on its stable id (one an
    /// older builder made, or one that drifted); empty when everything is. Reads the files, so
    /// the scene needn't be open.
    /// </summary>
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

    // The scene file's NetworkObjects ("--- !u!114 &fileID" blocks of that class), each checked
    // against the name of its GameObject ("--- !u!1 &fileID" blocks).
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

    // --- shared --------------------------------------------------------------

    /// <summary>The fileID of <paramref name="obj"/> in the asset or scene file it's saved in.</summary>
    public static long FileIdOf(Object obj) => (long)GlobalObjectId.GetGlobalObjectIdSlow(obj).targetObjectId;

    // Every reference to one fileID in a Unity YAML file: its "--- !u!114 &id" header and each "{fileID: id}".
    static string Replace(string yaml, long from, long to) =>
        Regex.Replace(yaml, @"(?<=&|fileID: )" + from.ToString(CultureInfo.InvariantCulture) + @"(?!\d)", to.ToString(CultureInfo.InvariantCulture));

    // Netcode works out GlobalObjectIdHash in NetworkObject.OnValidate, which doesn't run for
    // objects made from code; run it, and mark the object to be saved with the hash.
    static void Stamp(NetworkObject networkObject)
    {
        var onValidate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
        onValidate?.Invoke(networkObject, null);
        EditorUtility.SetDirty(networkObject);
    }
}
