using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Prototype > Build Test Scene: lays out a greybox field so nothing has to
// be placed by hand. Re-run it any time to get a fresh copy of the scene.
public static class PrototypeSceneBuilder
{
    const string Root = "Assets/Prototype";
    const string ScenePath = Root + "/Scenes/Fireline.unity";
    const int Size = 20;

    [MenuItem("Prototype/Build Test Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Scenes");
        AssetDatabase.Refresh();

        var grass = Mat("Grass", new Color(0.36f, 0.62f, 0.27f));
        var dirt = Mat("Dirt", new Color(0.55f, 0.42f, 0.28f));
        var burnt = Mat("Burnt", new Color(0.12f, 0.11f, 0.10f));
        var water = Mat("Water", new Color(0.20f, 0.45f, 0.80f), smoothness: 0.9f);
        var crate = Mat("Crate", new Color(0.72f, 0.50f, 0.25f));
        var fire = Mat("Fire", new Color(1f, 0.45f, 0.1f), emission: new Color(1f, 0.4f, 0.05f) * 4f);
        var robe = Mat("Wizard", new Color(0.45f, 0.30f, 0.75f));
        var bolt = Mat("Bolt", new Color(1f, 0.6f, 0.2f), emission: new Color(1f, 0.5f, 0.1f) * 6f);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // The field: grass everywhere, a pond, and a dirt path that acts as a firebreak.
        var field = new GameObject("Field").transform;
        var pond = new Vector2(4f, 4f);
        for (int x = 0; x < Size; x++)
        for (int z = 0; z < Size; z++)
        {
            var pos = new Vector3(x - Size / 2 + 0.5f, 0f, z - Size / 2 + 0.5f);
            bool isWater = Vector2.Distance(new Vector2(pos.x, pos.z), pond) < 3f;
            bool isPath = !isWater && (x == 6 || x == 7) && z < 14;

            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.transform.SetParent(field);
            tile.transform.localScale = new Vector3(1f, 0.2f, 1f);
            tile.transform.position = pos + Vector3.down * (isWater ? 0.2f : 0.1f);
            var renderer = tile.GetComponent<Renderer>();

            if (isWater)
            {
                tile.name = $"Water {x},{z}";
                renderer.sharedMaterial = water;
                tile.AddComponent<Water>();
            }
            else if (isPath)
            {
                tile.name = $"Dirt {x},{z}";
                renderer.sharedMaterial = dirt;
            }
            else
            {
                tile.name = $"Grass {x},{z}";
                renderer.sharedMaterial = grass;
                var b = tile.AddComponent<Burnable>();
                b.displayName = "grass";
                b.fireMaterial = fire;
                b.burntMaterial = burnt;
            }
        }

        // A few crates to burn down.
        foreach (var p in new[] { new Vector3(-3.5f, 0.4f, 2.5f), new Vector3(-2.5f, 0.4f, 2.5f), new Vector3(1.5f, 0.4f, -4.5f) })
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Crate";
            box.transform.position = p;
            box.transform.localScale = Vector3.one * 0.8f;
            box.GetComponent<Renderer>().sharedMaterial = crate;
            var b = box.AddComponent<Burnable>();
            b.displayName = "crate";
            b.fuel = 8f;
            b.destroyWhenBurnt = true;
            b.fireMaterial = fire;
        }

        // The wizard.
        var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Wizard";
        player.tag = "Player";
        Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());
        player.transform.position = new Vector3(-7f, 1.1f, -7f);
        player.GetComponent<Renderer>().sharedMaterial = robe;
        var cc = player.AddComponent<CharacterController>();
        cc.center = Vector3.zero;
        cc.height = 2f;
        cc.radius = 0.4f;
        player.AddComponent<PlayerController>();
        var line = player.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.widthMultiplier = 0.12f;
        line.sharedMaterial = bolt;
        line.shadowCastingMode = ShadowCastingMode.Off;
        player.AddComponent<FireBolt>();

        // Camera, light, HUD.
        var cam = Camera.main.gameObject;
        cam.transform.position = player.transform.position + new Vector3(0f, 9f, -7f);
        cam.AddComponent<CameraFollow>().target = player.transform;
        var sun = Object.FindFirstObjectByType<Light>();
        if (sun != null) sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        new GameObject("EventLog").AddComponent<EventLog>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log($"Built {ScenePath}. Press Play.");
    }

    static Material Mat(string name, Color color, float smoothness = 0.2f, Color? emission = null)
    {
        string path = $"{Root}/Materials/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
