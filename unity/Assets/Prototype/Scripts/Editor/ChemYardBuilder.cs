using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Prototype > Build Chemistry Yard: a scene whose chemistry runs on
// ../rules/*.json through Chem.World. Re-run it any time for a fresh copy.
public static class ChemYardBuilder
{
    const string ScenePath = "Assets/Prototype/Scenes/ChemistryYard.unity";

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

        // Find the @ to place the wizard; cell (x, y) sits at world (x, 0, H-1-y).
        var rows = Map.Split('\n');
        var start = Vector3.zero;
        for (int y = 0; y < rows.Length; y++)
        {
            int x = rows[y].IndexOf('@');
            if (x >= 0) start = new Vector3(x, 1.1f, rows.Length - 1 - y);
        }

        var robe = AssetDatabase.LoadAssetAtPath<Material>("Assets/Prototype/Materials/Wizard.mat");
        var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Wizard";
        player.tag = "Player";
        Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());
        player.transform.position = start;
        player.transform.localScale = Vector3.one * 0.8f;
        if (robe != null) player.GetComponent<Renderer>().sharedMaterial = robe;
        var cc = player.AddComponent<CharacterController>();
        cc.center = Vector3.zero;
        cc.height = 2f;
        cc.radius = 0.4f;
        cc.stepOffset = 0.55f;
        player.AddComponent<PlayerController>().grid = grid;
        player.AddComponent<SpellCaster>().grid = grid;

        var cam = Camera.main.gameObject;
        cam.transform.position = start + new Vector3(0f, 9f, -7f);
        cam.AddComponent<CameraFollow>().target = player.transform;
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
