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
[InitializeOnLoad]
public static class YardAutoBuild
{
    internal const string ScenePath = "Assets/Prototype/Scenes/ChemistryYard.unity";

    static YardAutoBuild()
    {
        AssetPackRestore.Finished += BuildIfMissing;
        BuildWhenFree();
    }

    /// <summary>Checks (and builds if needed) on the next Editor update that isn't importing or compiling.</summary>
    internal static void BuildWhenFree()
    {
        EditorApplication.update -= FirstFreeUpdate;
        EditorApplication.update += FirstFreeUpdate;
    }

    // Once, on the first Editor update that isn't busy importing or compiling.
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
        if (!ClearForBuild()) return;
        Debug.Log($"{ScenePath} is missing; building it.");
        ChemYardBuilder.Build();
    }

    // An automatic build must never stop at a "save changes?" dialog. The yard
    // itself, open after its file was deleted, has nothing to keep, so it's
    // replaced by an empty scene. Any other scene with unsaved changes might be
    // someone's work: then nothing is built and the log says how to build.
    static bool ClearForBuild()
    {
        bool yardOpen = false;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.path == ScenePath) yardOpen = true;
            else if (scene.isDirty)
            {
                Debug.LogWarning($"{ScenePath} is missing, but {scene.name} has unsaved changes: save it, then run Prototype > Build Chemistry Yard.");
                return false;
            }
        }
        if (yardOpen) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        return true;
    }
}

// Notices the yard scene being deleted by anything outside the builder (a pull,
// a checkout) and builds it again once the Editor is free.
class YardDeletedWatcher : AssetPostprocessor
{
    // Unity calls this message with a fixed signature; only the deleted paths matter here.
#pragma warning disable S1172 // Unused method parameter
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
#pragma warning restore S1172
    {
        if (System.Array.IndexOf(deleted, YardAutoBuild.ScenePath) >= 0)
            YardAutoBuild.BuildWhenFree();
    }
}
