using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The Chemistry Yard scene is generated (ChemYardBuilder), so it isn't in git:
// every rebuild rewrites its internal ids, which only made noise in diffs and
// blocked pulls. ChemYardBuilder writes its .meta with a fixed GUID, so it
// keeps its place in the build settings. When the scene is missing (a fresh clone, or
// after a pull), this builds it, once the Asset Store packs are there; it
// checks again when a pack restore finishes, and when a refresh finds the
// scene deleted (a pull that removes it doesn't always reload scripts).
// It also rebuilds a yard whose network ids aren't the stable ones
// (StableNetworkIds: one built before them), or other machines can't join it,
// one built before the species were rows, whose villages name no folk, and one whose grass is the pack's heavy tufts.
// After a script reload it skips the check (it reads the 5 MB scene twice) when the scene, the network prefab list, the
// ground template and this code are all as they were when it last passed in this Editor session.
[InitializeOnLoad]
public static class YardAutoBuild
{
    internal const string ScenePath = "Assets/Prototype/Scenes/ChemistryYard.unity";

    static YardAutoBuild()
    {
        AssetPackRestore.Finished += BuildIfMissing;
        BuildWhenFree();
    }

    internal static void BuildWhenFree()
    {
        EditorApplication.update -= FirstFreeUpdate;
        EditorApplication.update += FirstFreeUpdate;
    }

    static void FirstFreeUpdate()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= FirstFreeUpdate;
        if (SessionState.GetString(CheckedKey, "") == CheckedInputs()) return;
        BuildIfMissing();
    }

    const string CheckedKey = "YardAutoBuild.checked";

    internal static string CheckedInputs()
    {
        static string Stamp(string path) =>
            File.Exists(path) ? $"{new FileInfo(path).Length}@{File.GetLastWriteTimeUtc(path).Ticks}" : "none";
        return string.Join("|", Stamp(ScenePath), Stamp(ChemYardBuilder.ThingPrefabsPath), Stamp(LightTufts.Template),
            typeof(YardAutoBuild).Assembly.ManifestModule.ModuleVersionId.ToString());
    }

    public static void BuildIfMissing()
    {
        if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string why = $"{ScenePath} is missing";
        if (File.Exists(ScenePath))
        {
            var unstable = ChemYardBuilder.UnstableNetworkIds();
            bool assetFolk = File.ReadAllText(ScenePath).Contains("residents: {fileID", System.StringComparison.Ordinal);
            bool heavyGrass = LightTufts.GroundHasPackTufts() || LightTufts.GroundGrassCastsShadows();
            if (unstable.Count == 0 && !assetFolk && !heavyGrass)
            {
                SessionState.SetString(CheckedKey, CheckedInputs());
                return;
            }
            if (assetFolk) why = "The yard's villages name their folk by asset, from before rules/fauna.json";
            else if (heavyGrass) why = "The ground's grass is the terrain pack's own heavy tufts or casts shadows, from before LightTufts";
            else why = $"The yard's network ids aren't the same as other machines' ({string.Join(", ", unstable)})";
        }
        if (AssetPackRestore.Busy) return;
        if (!AssetPackRestore.AllPresent)
        {
            Debug.LogWarning($"{ScenePath} isn't built yet: run Prototype > Restore Asset Packs, and the yard builds when it finishes.");
            return;
        }
        if (!ClearForBuild()) return;
        Debug.Log($"{why}; building it.");
        ChemYardBuilder.Build();
        TreeImpostorBaker.BakeMissing();
    }

    static bool ClearForBuild()
    {
        bool yardOpen = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.path == ScenePath) yardOpen = true;
            else if (scene.isDirty)
            {
                Debug.LogWarning($"{ScenePath} needs building, but {scene.name} has unsaved changes: save it, then run Prototype > Build Chemistry Yard.");
                return false;
            }
        }
        if (yardOpen) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        return true;
    }
}

class YardDeletedWatcher : AssetPostprocessor
{
#pragma warning disable S1172
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
#pragma warning restore S1172
    {
        if (System.Array.IndexOf(deleted, YardAutoBuild.ScenePath) >= 0)
            YardAutoBuild.BuildWhenFree();
    }
}
