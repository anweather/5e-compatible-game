using System.IO;
using UnityEditor;
using Unity.Cinemachine;
using UnityEditor.SceneManagement;
using UnityEngine;

// Prototype > Build Chemistry Yard: a scene whose chemistry runs on
// ../rules/*.json through Chem.World. Re-run it any time for a fresh copy.
public static class ChemYardBuilder
{
    const string ScenePath = "Assets/Prototype/Scenes/ChemistryYard.unity";
    const string StarterAssets = "Assets/Starter Assets/Runtime/ThirdPersonController/Prefabs";

    // Same legend as the web sandbox: , grass  " brush  T tree  . dirt  o oil
    // w pond  _ pit  = wooden bridge  m metal  ~ puddle  % bedrock  @ start
    const string Map =
        "%%%%%%%%%%%%%%%%%%%%%%%%%%\n" +
        "%,,,,,T,,,,,,,,..........%\n" +
        "%,,T,,,,,\"\",,,,,...oo....%\n" +
        "%,,,,,,,,\"\",,,,,..oooo...%\n" +
        "%,,,,,,,,,,,,,,,..wwww...%\n" +
        "%,T,,,,,,,,,,,,..wwwwww..%\n" +
        "%,,,,,,,,,,,,,,..wwwwwwmm%\n" +
        "%,,,,,,,,,,,,,,...wwww..m%\n" +
        "%,,,,T,,,,,,,,,.........m%\n" +
        "%_____=____________.....m%\n" +
        "%_____=____________.....m%\n" +
        "%,,,,,,,,,,,,,,,,,,.....m%\n" +
        "%,,T,,,\"\"\",,,,,,,,..~~..m%\n" +
        "%,,,,,,,,,,,,@,,,,..~~...%\n" +
        "%,,,,,,,,,,,T,,,,,.......%\n" +
        "%%%%%%%%%%%%%%%%%%%%%%%%%%";

    [MenuItem("Prototype/Build Chemistry Yard")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Directory.CreateDirectory("Assets/Prototype/Scenes");
        AssetDatabase.Refresh();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var grid = new GameObject("ChemGrid").AddComponent<ChemGrid>();
        grid.map = Map;

        // Find the @ to place the wizard; cell (x, y) sits at world (x, 0, H-1-y) times the cell size.
        var rows = Map.Split('\n');
        var start = Vector3.zero;
        for (int y = 0; y < rows.Length; y++)
        {
            int x = rows[y].IndexOf('@');
            if (x >= 0) start = new Vector3(x, 0.1f, rows.Length - 1 - y) * grid.cellSize;
        }

        // The wizard is the Starter Assets third-person character (restore it with
        // Prototype > Restore Asset Packs), with our spellcasting added.
        var armature = AssetDatabase.LoadAssetAtPath<GameObject>(StarterAssets + "/PlayerArmature.prefab");
        var mainCamera = AssetDatabase.LoadAssetAtPath<GameObject>(StarterAssets + "/MainCamera.prefab");
        var followCamera = AssetDatabase.LoadAssetAtPath<GameObject>(StarterAssets + "/PlayerFollowCamera.prefab");
        if (armature == null || mainCamera == null || followCamera == null)
        {
            Debug.LogError("Starter Assets are missing. Run Prototype > Restore Asset Packs, then build again.");
            return;
        }
        Object.DestroyImmediate(Camera.main.gameObject);
        PrefabUtility.InstantiatePrefab(mainCamera);

        var player = (GameObject)PrefabUtility.InstantiatePrefab(armature);
        player.name = "Wizard";
        player.transform.position = start;
        player.AddComponent<SpellCaster>().grid = grid;

        var follow = (GameObject)PrefabUtility.InstantiatePrefab(followCamera);
        var cameraRoot = player.transform.Find("PlayerCameraRoot");
        follow.GetComponent<CinemachineCamera>().Target.TrackingTarget = cameraRoot;

        var sun = Object.FindFirstObjectByType<Light>();
        if (sun != null) sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        new GameObject("EventLog").AddComponent<EventLog>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == ScenePath)) scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"Built {ScenePath}. Press Play.");
    }
}
