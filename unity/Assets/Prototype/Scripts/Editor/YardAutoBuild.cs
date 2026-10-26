using System.IO;
using UnityEditor;
using UnityEngine;

// The Chemistry Yard scene is generated (ChemYardBuilder), so it isn't in git:
// every rebuild rewrites its internal ids, which only made noise in diffs and
// blocked pulls. ChemYardBuilder writes its .meta with a fixed GUID, so it
// keeps its place in the build settings. When the scene is missing (a fresh clone, or
// after a pull), this builds it, once the Asset Store packs are there; it
// checks again when a pack restore finishes.
[InitializeOnLoad]
public static class YardAutoBuild
{
    const string ScenePath = "Assets/Prototype/Scenes/ChemistryYard.unity";

    static YardAutoBuild()
    {
        AssetPackRestore.Finished += BuildIfMissing;
        EditorApplication.update += FirstFreeUpdate;
    }

    // Once per domain load, on the first Editor update that isn't busy importing or compiling.
    static void FirstFreeUpdate()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= FirstFreeUpdate;
        BuildIfMissing();
    }

    /// <summary>Builds the yard if its scene doesn't exist and the Editor is free to.</summary>
    public static void BuildIfMissing()
    {
        if (File.Exists(ScenePath) || Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetPackRestore.Busy) return; // Finished calls back when it's done
        if (!AssetPackRestore.AllPresent)
        {
            Debug.LogWarning($"{ScenePath} isn't built yet: run Prototype > Restore Asset Packs, and the yard builds when it finishes.");
            return;
        }
        Debug.Log($"{ScenePath} is missing; building it.");
        ChemYardBuilder.Build();
    }
}
