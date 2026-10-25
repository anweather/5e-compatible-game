using System.IO;
using UnityEditor;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor.SceneManagement;
using UnityEngine;

// Prototype > Build Chemistry Yard: a scene whose chemistry runs on
// ../rules/*.json through Chem.World. Re-run it any time for a fresh copy.
public static class ChemYardBuilder
{
    const string ScenePath = "Assets/Prototype/Scenes/ChemistryYard.unity";
    const string StarterAssets = "Assets/Starter Assets/Runtime/ThirdPersonController/Prefabs";
    const string Effects = "Assets/UnityTechnologies/ParticlePack/EffectExamples";
    const string FireFx = Effects + "/Fire & Explosion Effects/Prefabs";
    const string SmokeFx = Effects + "/Smoke & Steam Effects/Prefabs";
    const string MiscFx = Effects + "/Misc Effects/Prefabs";
    const string VolumePath = "Assets/Prototype/Settings/YardVolume.asset";
    const string NetWizardPath = "Assets/Prototype/Prefabs/NetWizard.prefab";

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
        AssignEffects(grid);

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
        var camera = (GameObject)PrefabUtility.InstantiatePrefab(mainCamera);
        camera.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;

        var player = (GameObject)PrefabUtility.InstantiatePrefab(armature);
        player.name = "Wizard";
        player.transform.position = start;
        var controls = player.AddComponent<PlayerControls>();
        var caster = player.AddComponent<SpellCaster>();
        caster.grid = grid;
        caster.controls = controls;
        caster.fireBolt = Load(FireFx + "/FireBall.prefab");

        var follow = (GameObject)PrefabUtility.InstantiatePrefab(followCamera);
        var cameraRoot = player.transform.Find("PlayerCameraRoot");
        follow.GetComponent<CinemachineCamera>().Target.TrackingTarget = cameraRoot;
        follow.AddComponent<CameraZoom>().controls = controls;

        SetUpLighting();
        new GameObject("EventLog").AddComponent<EventLog>();
        SetUpNetwork(grid, BuildNetWizard(armature));

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssignSceneNetworkIds();
        EditorSceneManager.SaveScene(scene, ScenePath);
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == ScenePath)) scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"Built {ScenePath}. Press Play.");
    }

    static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

    // Particle Pack effects; any that are missing leave the grid's cube stand-ins.
    static void AssignEffects(ChemGrid grid)
    {
        grid.fireSmall = Load(FireFx + "/TinyFlames.prefab");
        grid.fireMedium = Load(FireFx + "/MediumFlames.prefab");
        grid.fireLarge = Load(FireFx + "/LargeFlames.prefab");
        grid.smokeFx = Load(SmokeFx + "/SmokeEffect.prefab");
        grid.steamFx = Load(SmokeFx + "/RisingSteam.prefab");
        grid.sparksFx = Load(MiscFx + "/SparksEffect.prefab");
        if (grid.fireSmall == null) Debug.LogWarning("Particle Pack missing: fire and smoke will be cubes. Run Prototype > Restore Asset Packs.");
    }

    // Late-afternoon sun, and a post-processing volume: bloom so fire glows,
    // filmic tonemapping, a little extra saturation and a soft vignette.
    static void SetUpLighting()
    {
        var sun = Object.FindFirstObjectByType<Light>();
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(32f, -40f, 0f);
            sun.color = new Color(1f, 0.88f, 0.74f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(VolumePath));
        AssetDatabase.Refresh();
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumePath);
        }
        Override<Bloom>(profile, b => { b.intensity.Override(0.9f); b.threshold.Override(1f); b.scatter.Override(0.7f); });
        Override<Tonemapping>(profile, t => t.mode.Override(TonemappingMode.ACES));
        Override<ColorAdjustments>(profile, c => { c.postExposure.Override(0.25f); c.saturation.Override(12f); c.contrast.Override(8f); });
        Override<Vignette>(profile, v => { v.intensity.Override(0.22f); v.smoothness.Override(0.5f); });
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        var volume = new GameObject("Post-processing").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
    }

    static void Override<T>(VolumeProfile profile, System.Action<T> set) where T : VolumeComponent
    {
        if (!profile.TryGet<T>(out var component))
        {
            component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
        }
        set(component);
        EditorUtility.SetDirty(component);
    }

    // Multiplayer: the grid's lockstep chemistry (NetChem), a NetworkManager on
    // UDP port NetSession.Port, and the Host / Join panel.
    static void SetUpNetwork(ChemGrid grid, GameObject netWizard)
    {
        grid.gameObject.AddComponent<NetworkObject>();
        grid.gameObject.AddComponent<NetChem>();

        var go = new GameObject("NetworkManager");
        var manager = go.AddComponent<NetworkManager>();
        var transport = go.AddComponent<UnityTransport>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = netWizard,
            ConnectionApproval = true,
        };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = netWizard });

        new GameObject("NetSession").AddComponent<NetSession>();
    }

    // The networked wizard: a variant of the Starter Assets character with
    // owner-authoritative movement and animation. Input, movement and
    // spellcasting start switched off; NetWizard turns them on for the owner.
    static GameObject BuildNetWizard(GameObject armature)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(NetWizardPath));
        AssetDatabase.Refresh();

        var wizard = (GameObject)PrefabUtility.InstantiatePrefab(armature);
        wizard.name = "NetWizard";
        wizard.AddComponent<NetworkObject>();
        var netTransform = wizard.AddComponent<NetworkTransform>();
        netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        netTransform.SyncScaleX = netTransform.SyncScaleY = netTransform.SyncScaleZ = false;
        var animator = wizard.AddComponent<NetworkAnimator>();
        animator.Animator = wizard.GetComponent<Animator>();
        animator.AuthorityMode = NetworkAnimator.AuthorityModes.Owner;

        wizard.AddComponent<PlayerControls>().enabled = false;
        var caster = wizard.AddComponent<SpellCaster>();
        caster.fireBolt = Load(FireFx + "/FireBall.prefab");
        caster.enabled = false;
        wizard.AddComponent<NetWizard>();

        wizard.GetComponent<CharacterController>().enabled = false;
        wizard.GetComponent<PlayerInput>().enabled = false;
        foreach (var name in new[] { "StarterAssetsInputs", "ThirdPersonController", "BasicRigidBodyPush" })
            if (wizard.GetComponent(name) is Behaviour b) b.enabled = false;

        var prefab = PrefabUtility.SaveAsPrefabAsset(wizard, NetWizardPath);
        Object.DestroyImmediate(wizard);
        return prefab;
    }

    // An in-scene NetworkObject needs an id derived from its place in the saved
    // scene. Netcode assigns it in OnValidate, which runs when a scene is edited
    // by hand but not for objects added from code, so run it once the scene has a path.
    static void AssignSceneNetworkIds()
    {
        var onValidate = typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        foreach (var networkObject in Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
        {
            onValidate?.Invoke(networkObject, null);
            EditorUtility.SetDirty(networkObject);
        }
    }
}
