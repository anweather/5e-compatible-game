using System.IO;
using UnityEditor;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor.SceneManagement;
using UnityEngine;

// Prototype > Build Chemistry Yard: a scene whose chemistry runs on
// ../rules/*.json through Chem.World. Re-run it any time for a fresh copy.
public static class ChemYardBuilder
{
    const string ScenePath = "Assets/Prototype/Scenes/ChemistryYard.unity";
    const string StarterAssets = PackPaths.StarterAssets;
    const string FireFx = PackPaths.FireFx;
    const string SmokeFx = PackPaths.SmokeFx;
    const string MiscFx = PackPaths.MiscFx;
    const string VolumePath = "Assets/Prototype/Settings/YardVolume.asset";
    const string NetWizardPath = "Assets/Prototype/Prefabs/NetWizard.prefab";
    const string SpellVisualsPath = "Assets/Prototype/Settings/SpellVisuals.asset";
    const string ThingPrefabsPath = "Assets/Prototype/Generated/ThingNetworkPrefabs.asset";

    // Same legend as the web sandbox: , grass  " brush  T tree  . dirt  o oil
    // w pond  _ pit  = wooden bridge  m metal  ~ puddle  % bedrock  @ start
    // The edge is open grass: the yard sits in the valley (ValleyBuilder).
    const string Map =
        ",,T,,,,,,,,,,,,,,,,,,,,T,,\n" +
        ",,,,,,T,,,,,,,,,,.......,,\n" +
        ",,,T,,,,,\"\",,,,,...oo...,,\n" +
        ",,,,,,,,,\"\",,,,,..oooo...,\n" +
        ",,,,,,,,,,,,,,,,..wwww...,\n" +
        ",,T,,,,,,,,,,,,..wwwwww..,\n" +
        ",,,,,,,,,,,,,,,..wwwwwwmm,\n" +
        ",,,,,,,,,,,,,,,...wwww..m,\n" +
        ",,,,,T,,,,,,,,,,........m,\n" +
        ",_____=____________.....m,\n" +
        ",_____=____________.....m,\n" +
        ",,,,,,,,,,,,,,,,,,,.....m,\n" +
        ",,,T,,,\"\"\",,,,,,,,..~~..m,\n" +
        ",,,,,,,,,,,,,@,,,,..~~..,,\n" +
        ",,,,,,,,,,,,T,,,,,,....,,,\n" +
        ",,,,,,T,,,,,,,,,,,,,,,,,,,";

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
        var armature = Load(StarterAssets + "/PlayerArmature.prefab");
        var mainCamera = Load(StarterAssets + "/MainCamera.prefab");
        var followCamera = Load(StarterAssets + "/PlayerFollowCamera.prefab");
        if (armature == null || mainCamera == null || followCamera == null)
        {
            Debug.LogError("Starter Assets are missing. Run Prototype > Restore Asset Packs, then build again.");
            return;
        }
        Object.DestroyImmediate(Camera.main.gameObject);
        var camera = (GameObject)PrefabUtility.InstantiatePrefab(mainCamera);
        camera.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;

        var player = (GameObject)PrefabUtility.InstantiatePrefab(armature);
        player.name = WizardRig.Name;
        player.transform.position = start;
        var controls = player.AddComponent<PlayerControls>();
        var visuals = BuildSpellVisuals();
        player.AddComponent<CastPose>();
        player.AddComponent<SurfaceEffects>().grid = grid;
        player.AddComponent<Knockback>().grid = grid;
        var caster = player.AddComponent<SpellCaster>();
        caster.grid = grid;
        caster.controls = controls;
        caster.visuals = visuals;

        var follow = (GameObject)PrefabUtility.InstantiatePrefab(followCamera);
        var cameraRoot = player.transform.Find(WizardRig.CameraRoot);
        follow.GetComponent<CinemachineCamera>().Target.TrackingTarget = cameraRoot;
        follow.AddComponent<CameraZoom>().controls = controls;
        var hands = player.AddComponent<ItemHolder>();
        hands.controls = controls;
        hands.view = follow.GetComponent<CinemachineCamera>();

        SetUpLighting();
        ValleyBuilder.Dress(grid, rows);
        // Props and items aren't placed here: ThingSpawner spawns them from world/things.json.
        var things = PropBuilder.BuildPrefabs();
        things.AddRange(ItemBuilder.BuildPrefabs());
        var spawner = new GameObject("Things").AddComponent<ThingSpawner>();
        spawner.grid = grid;
        spawner.prefabs = things;
        new GameObject("EventLog").AddComponent<EventLog>();
        SetUpNetwork(grid, BuildNetWizard(armature), things);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssignSceneNetworkIds();
        EditorSceneManager.SaveScene(scene, ScenePath);
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == ScenePath)) scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"Built {ScenePath}. Press Play.");
    }

    static GameObject Load(string path) => EditorAssets.LoadPrefab(path);

    // Particle Pack effects; any that are missing leave the grid's cube stand-ins.
    static void AssignEffects(ChemGrid grid)
    {
        grid.fireSmall = Load(FireFx + "/TinyFlames.prefab");
        grid.fireMedium = Load(FireFx + "/MediumFlames.prefab");
        grid.fireLarge = Load(FireFx + "/LargeFlames.prefab");
        grid.smokeFx = Load(SmokeFx + "/SmokeEffect.prefab");
        grid.steamFx = Load(SmokeFx + "/RisingSteam.prefab");
        grid.sparksFx = Load(MiscFx + "/SparksEffect.prefab");
        // The dust thrown up by earth shaping uses the Particle Pack's dust material.
        var dust = Load(FireFx + "/DustExplosion.prefab");
        grid.dustMaterial = dust != null ? dust.GetComponent<ParticleSystemRenderer>().sharedMaterial : null;
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

        EditorAssets.EnsureFolderFor(VolumePath);
        var profile = EditorAssets.LoadOrCreate(VolumePath, ScriptableObject.CreateInstance<VolumeProfile>);
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
    // UDP port NetSession.Port with the wizard and the things as network
// prefabs, and the Host / Join panel.
    static void SetUpNetwork(ChemGrid grid, GameObject netWizard, System.Collections.Generic.List<ThingSpawner.KindPrefab> things)
    {
        grid.gameObject.AddComponent<NetworkObject>();
        grid.gameObject.AddComponent<NetChem>();
        grid.gameObject.AddComponent<NetDemo>();

        var go = new GameObject("NetworkManager");
        var manager = go.AddComponent<NetworkManager>();
        var transport = go.AddComponent<UnityTransport>();
        // A late joiner receives the history and every prop's state at once; the default 128 overflowed.
        transport.MaxPacketQueueSize = 512;
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = netWizard,
            ConnectionApproval = true,
        };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = netWizard });
        // The props and items, which the host spawns from world/things.json (ThingSpawner). A list
        // asset, since NetworkConfig.Prefabs keeps only its lists in the scene, not prefabs added to it.
        manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(ThingPrefabList(things));

        new GameObject("NetSession").AddComponent<NetSession>();
    }

    static NetworkPrefabsList ThingPrefabList(System.Collections.Generic.List<ThingSpawner.KindPrefab> things)
    {
        EditorAssets.EnsureFolderFor(ThingPrefabsPath);
        var list = EditorAssets.LoadOrCreate(ThingPrefabsPath, ScriptableObject.CreateInstance<NetworkPrefabsList>);
        foreach (var old in new System.Collections.Generic.List<NetworkPrefab>(list.PrefabList)) list.Remove(old);
        foreach (var thing in things) list.Add(new NetworkPrefab { Prefab = thing.prefab });
        EditorUtility.SetDirty(list);
        AssetDatabase.SaveAssets();
        return list;
    }

    // The networked wizard: a variant of the Starter Assets character with
    // owner-authoritative movement and animation. Input, movement and
    // spellcasting start switched off; NetWizard turns them on for the owner.
    static GameObject BuildNetWizard(GameObject armature)
    {
        EditorAssets.EnsureFolderFor(NetWizardPath);

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
        caster.visuals = BuildSpellVisuals();
        caster.enabled = false;
        wizard.AddComponent<CastPose>();
        wizard.AddComponent<SurfaceEffects>().enabled = false;
        wizard.AddComponent<Knockback>();
        // Every copy holds what its player holds; NetWizard gives the owner's copy its controls.
        wizard.AddComponent<ItemHolder>();
        wizard.AddComponent<NetWizard>();

        WizardRig.SetPlayerDriven(wizard, false);

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

    // How each spell looks, from the Particle Pack. Missing prefabs leave that part out.
    static SpellVisuals BuildSpellVisuals()
    {
        var visuals = AssetDatabase.LoadAssetAtPath<SpellVisuals>(SpellVisualsPath);
        if (visuals == null)
        {
            EditorAssets.EnsureFolderFor(SpellVisualsPath);
            visuals = ScriptableObject.CreateInstance<SpellVisuals>();
            AssetDatabase.CreateAsset(visuals, SpellVisualsPath);
        }
        visuals.entries = new System.Collections.Generic.List<SpellVisuals.Entry>
        {
            Visual(SpellIds.FireBolt, new Color(1f, 0.5f, 0.15f), FireFx + "/FireBall.prefab", FireFx + "/TinyExplosion.prefab"),
            Visual(SpellIds.RayOfFrost, new Color(0.55f, 0.85f, 1f), null, PackPaths.MagicFx + "/IceLance.prefab"),
            Visual(SpellIds.CreateWater, new Color(0.3f, 0.6f, 1f), null, PackPaths.WaterFx + "/BigSplash.prefab"),
            Visual(SpellIds.Grease, new Color(0.9f, 0.8f, 0.3f), null, PackPaths.GoopFx + "/GoopSpray.prefab"),
            Visual(SpellIds.ShockingGrasp, new Color(1f, 0.95f, 0.4f), null, MiscFx + "/ElectricalSparks.prefab"),
            Visual(SpellIds.GustOfWind, new Color(0.9f, 0.95f, 1f), null, MiscFx + "/SandSwirlsEffect.prefab"),
            Visual(SpellIds.Thunderwave, new Color(0.8f, 0.85f, 1f), null, FireFx + "/DustExplosion.prefab"),
            Visual(SpellIds.Levitate, new Color(0.75f, 0.6f, 1f), null, MiscFx + "/ParticlesLight.prefab"),
            // Earth shaping: the ground itself throws up dust where it moves (ChemGrid.dustFx).
            Visual(SpellIds.MoldEarth, new Color(0.75f, 0.55f, 0.3f), null, null),
            Visual(SpellIds.MoldEarthHeap, new Color(0.75f, 0.55f, 0.3f), null, null),
            Visual(SpellIds.EarthenTrench, new Color(0.7f, 0.5f, 0.28f), null, null),
            Visual(SpellIds.WallOfStone, new Color(0.7f, 0.68f, 0.62f), null, null),
            Visual(SpellIds.MoveEarth, new Color(0.65f, 0.5f, 0.3f), null, null),
        };
        EditorUtility.SetDirty(visuals);
        AssetDatabase.SaveAssets();
        return visuals;
    }

    static SpellVisuals.Entry Visual(string spellId, Color glow, string projectile, string impact) => new SpellVisuals.Entry
    {
        spellId = spellId,
        glow = glow,
        projectile = projectile != null ? Load(projectile) : null,
        impact = impact != null ? Load(impact) : null,
    };
}
