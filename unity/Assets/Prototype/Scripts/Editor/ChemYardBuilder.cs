using System.IO;
using System.Linq;
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
    const string CastFeelPath = "Assets/Prototype/Settings/CastFeel.asset";
    const string ThingPrefabsPath = "Assets/Prototype/Generated/ThingNetworkPrefabs.asset";

    internal const string Map =
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
        AssignEffects(grid);

        var rows = Map.Split('\n');
        var map = Chem.MapFile.Load(ChemGrid.Folders.World);
        var (spawnX, spawnY) = (map.Spawn.x - map.Origin.x, map.Origin.y - map.Spawn.y);
        var start = new Vector3(spawnX * grid.cellSize, 0.1f, spawnY * grid.cellSize);

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
        var (controls, _, hands) = AddWizardParts(player, grid, BuildSpellVisuals(), playerDriven: true);
        SoundBankBuilder.Build();

        var follow = (GameObject)PrefabUtility.InstantiatePrefab(followCamera);
        var cameraRoot = player.transform.Find(WizardRig.CameraRoot);
        follow.GetComponent<CinemachineCamera>().Target.TrackingTarget = cameraRoot;
        follow.AddComponent<CameraZoom>().controls = controls;
        follow.AddComponent<CameraClearance>();
        var third = follow.GetComponent<CinemachineThirdPersonFollow>();
        var avoid = third.AvoidObstacles;
        avoid.CameraRadius = 0.25f;
        avoid.DampingFromCollision = 0.4f;
        third.AvoidObstacles = avoid;
        hands.view = follow.GetComponent<CinemachineCamera>();
        follow.AddComponent<CinemachineImpulseListener>().SignalCombinationMode = CinemachineImpulseListener.SignalCombinationModes.UseLargest;
        follow.AddComponent<CastCamera>();

        SetUpLighting();
        ValleyBuilder.Dress(grid, rows);
        var things = PropBuilder.BuildPrefabs();
        things.AddRange(ItemBuilder.BuildPrefabs());
        var spawner = new GameObject("Things").AddComponent<ThingSpawner>();
        spawner.grid = grid;
        spawner.prefabs = things;
        new GameObject("EventLog").AddComponent<EventLog>();
        SetUpDragon(grid);
        SetUpNetwork(grid, BuildNetWizard(armature), things);

        WriteSceneMeta();
        EditorSceneManager.SaveScene(scene, ScenePath);
        StableNetworkIds.PinScene(ScenePath);
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == ScenePath)) scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"Built {ScenePath}. Press Play.");
    }

    public static System.Collections.Generic.List<string> UnstableNetworkIds() =>
        StableNetworkIds.Unstable(ScenePath, AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(ThingPrefabsPath));

    const string SceneGuid = "f2663c50dfda24918a8751f8d49c8fbb";

    static void WriteSceneMeta()
    {
        var meta = ScenePath + ".meta";
        if (File.Exists(meta)) return;
        File.WriteAllText(meta,
            "fileFormatVersion: 2\nguid: " + SceneGuid + "\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
    }

    static GameObject Load(string path) => EditorAssets.LoadPrefab(path);

    static void AssignEffects(ChemGrid grid)
    {
        grid.flameShader = Shader.Find("Prototype/Flame");
        grid.flameSheets = new[] { "TinyFlame", "MediumFlame01", "LargeFlame01" }
            .Select(n => AssetDatabase.LoadAssetAtPath<Texture2D>(PackPaths.Effects + "/Fire & Explosion Effects/Textures/" + n + ".tif")).ToArray();
        grid.smokeFx = Load(SmokeFx + "/SmokeEffect.prefab");
        grid.steamFx = Load(SmokeFx + "/RisingSteam.prefab");
        grid.sparksFx = Load(MiscFx + "/SparksEffect.prefab");
        var dust = Load(FireFx + "/DustExplosion.prefab");
        grid.dustMaterial = dust != null ? dust.GetComponent<ParticleSystemRenderer>().sharedMaterial : null;
        if (grid.smokeFx == null) Debug.LogWarning("Particle Pack missing: no flames, and smoke will be cubes. Run Prototype > Restore Asset Packs.");
    }

    static void SetUpLighting()
    {
        var sun = Object.FindAnyObjectByType<Light>();
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
        SetUpSky(sun);
        SetUpFauna();
    }

    static void SetUpDragon(ChemGrid grid)
    {
        var view = new GameObject("Dragon").AddComponent<DragonView>();
        view.grid = grid;
        view.model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prototype/ThirdParty/Quaternius/Dragon.fbx");
        view.shadowShader = Shader.Find("Prototype/DragonShadow");
    }

    static void SetUpSky(Light sun)
    {
        var go = new GameObject("Sky");
        var moon = new GameObject("Moon").AddComponent<Light>();
        moon.transform.SetParent(go.transform, false);
        moon.type = LightType.Directional;
        moon.color = new Color(0.55f, 0.65f, 1f);
        moon.intensity = 0f;
        moon.shadows = LightShadows.None;
        var light = go.AddComponent<SkyLight>();
        light.sun = sun;
        light.moon = moon;
        EditorAssets.EnsureFolderFor(PropBuilder.Generated + "/Rain.mat");
        go.AddComponent<CloudSky>().cloudNoise = EditorAssets.LoadOrCreate(PropBuilder.Generated + "/CloudNoise.asset",
            () => Punchy.Weather.CloudNoise.Create());
        var fx = go.AddComponent<WeatherFx>();
        fx.rainMaterial = Precipitation("Rain", 1f);
        fx.snowMaterial = Precipitation("Snow", 0f);
        fx.boltMaterial = EditorAssets.LoadOrCreate(PropBuilder.Generated + "/LightningBolt.mat",
            () => UrpMaterials.NewUnlitParticles("Lightning bolt", new Color(6f, 6.5f, 8f), additive: true));
        var critters = go.AddComponent<AmbientCritters>();
        critters.fireflyMaterial = EditorAssets.LoadOrCreate(PropBuilder.Generated + "/Fireflies.mat",
            () => new Material(Shader.Find("Punchy/Fireflies")) { name = "Fireflies" });
        critters.butterflyMaterial = EditorAssets.LoadOrCreate(PropBuilder.Generated + "/Butterflies.mat",
            () => new Material(Shader.Find("Punchy/Butterflies")) { name = "Butterflies" });
    }

    static void SetUpFauna()
    {
        new GameObject("Footprints").AddComponent<Footprints>().material = EditorAssets.LoadOrCreate(PropBuilder.Generated + "/Footprint.mat",
            () => UrpMaterials.NewUnlitParticles("Footprint", Color.white, additive: false));
        var fauna = new GameObject("Fauna").AddComponent<ValleyFauna>();
        var shader = Shader.Find("Punchy/Creature");
        fauna.material = EditorAssets.LoadOrCreate(PropBuilder.Generated + "/Creature.mat", () => new Material(shader) { name = "Creature" });
    }

    static Material Precipitation(string name, float shape)
    {
        var shader = Shader.Find("Punchy/Precipitation");
        var material = EditorAssets.LoadOrCreate(PropBuilder.Generated + "/" + name + ".mat", () => new Material(shader) { name = name });
        material.shader = shader;
        material.SetFloat("_Shape", shape);
        EditorUtility.SetDirty(material);
        return material;
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

    static void SetUpNetwork(ChemGrid grid, GameObject netWizard, System.Collections.Generic.List<ThingSpawner.KindPrefab> things)
    {
        grid.gameObject.AddComponent<NetworkObject>();
        grid.gameObject.AddComponent<NetChem>();
        grid.gameObject.AddComponent<NetTag>();
        grid.gameObject.AddComponent<NetCourt>();
        grid.gameObject.AddComponent<NetDemo>();

        var go = new GameObject("NetworkManager");
        var manager = go.AddComponent<NetworkManager>();
        var transport = go.AddComponent<UnityTransport>();
        transport.MaxPacketQueueSize = 512;
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = netWizard,
            ConnectionApproval = true,
        };
        manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = netWizard });
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

    internal static (PlayerControls controls, SpellCaster caster, ItemHolder hands) AddWizardParts(
        GameObject wizard, ChemGrid grid, SpellVisuals visuals, bool playerDriven)
    {
        WizardRig.TuneMovement(wizard);
        var mover = wizard.GetComponent(WizardRig.ControllerComponent);
        if (mover != null)
        {
            EditorUtility.SetDirty(mover);
            if (PrefabUtility.IsPartOfPrefabInstance(mover)) PrefabUtility.RecordPrefabInstancePropertyModifications(mover);
        }
        var controls = wizard.AddComponent<PlayerControls>();
        controls.enabled = playerDriven;
        var caster = wizard.AddComponent<SpellCaster>();
        caster.visuals = visuals;
        caster.enabled = playerDriven;
        wizard.AddComponent<CastPose>();
        wizard.AddComponent<CastBeats>().feel = AssetDatabase.LoadAssetAtPath<CastFeel>(CastFeelPath);
        var surface = wizard.AddComponent<SurfaceEffects>();
        surface.enabled = playerDriven;
        var knockback = wizard.AddComponent<Knockback>();
        var hands = wizard.AddComponent<ItemHolder>();
        wizard.AddComponent<SwimPose>();
        wizard.AddComponent<ThrowPose>();
        wizard.AddComponent<PunchPose>();
        wizard.AddComponent<HandPose>();
        wizard.AddComponent<SitPose>();
        wizard.AddComponent<Sitter>();
        wizard.AddComponent<CurrentRider>();
        wizard.AddComponent<Vitals>();
        wizard.AddComponent<WizardStrike>();
        wizard.AddComponent<WizardDown>();
        wizard.AddComponent<DownedPose>();
        wizard.AddComponent<Kicker>();
        wizard.AddComponent<SprintToggle>();
        wizard.AddComponent<DoubleJump>();
        wizard.AddComponent<WizardStats>();
        wizard.AddComponent<SpellRadialHud>();
        wizard.AddComponent<DodgeRoll>();
        wizard.AddComponent<RollPose>();
        var fade = wizard.AddComponent<OccluderFade>();
        fade.ditherLit = Shader.Find("Prototype/DitherLit");
        fade.silhouette = Shader.Find("Prototype/WizardSilhouette");
        wizard.AddComponent<Shapeshift>();
        var outfit = wizard.AddComponent<WizardOutfit>();
        outfit.material = ValleyBuilder.VillageMaterial();
        outfit.faceShader = Shader.Find("Prototype/WizardFace");
        if (playerDriven)
        {
            caster.grid = grid;
            caster.controls = controls;
            surface.grid = grid;
            knockback.grid = grid;
            hands.controls = controls;
        }
        return (controls, caster, hands);
    }

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

        AddWizardParts(wizard, null, BuildSpellVisuals(), playerDriven: false);
        wizard.AddComponent<NetWizard>();

        WizardRig.SetPlayerDriven(wizard, false);

        var prefab = PrefabUtility.SaveAsPrefabAsset(wizard, NetWizardPath);
        Object.DestroyImmediate(wizard);
        return prefab;
    }

    static SpellVisuals BuildSpellVisuals()
    {
        var visuals = AssetDatabase.LoadAssetAtPath<SpellVisuals>(SpellVisualsPath);
        if (visuals == null)
        {
            EditorAssets.EnsureFolderFor(SpellVisualsPath);
            visuals = ScriptableObject.CreateInstance<SpellVisuals>();
            AssetDatabase.CreateAsset(visuals, SpellVisualsPath);
        }
        var looks = Chem.LookFamilies.Load(ChemGrid.Folders.Rules);
        visuals.families = looks.In("spells").Select(Show).ToList();
        visuals.lookParticle = Shader.Find("Prototype/LookParticle");
        EditorUtility.SetDirty(visuals);
        AssetDatabase.SaveAssets();
        return visuals;
    }

    static SpellVisuals.Entry Show(Chem.LookFamily f) => new SpellVisuals.Entry
    {
        family = f.Name,
        projectile = Fx(f.Projectile),
        impact = Fx(f.Impact),
        afterImpact = Fx(f.AfterImpact),
        impactSeconds = f.Seconds,
        fromSky = f.FromSky,
        storm = f.Storm,
        beam = f.Beam,
        tinted = f.Tinted,
        scaleBy = f.ScaleBy,
        scaleBase = f.Base,
    };

    static GameObject Fx(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var parts = name.Split('/');
        string folder = parts[0] switch
        {
            "Fire" => FireFx,
            "Water" => PackPaths.WaterFx,
            "Goop" => PackPaths.GoopFx,
            _ => MiscFx,
        };
        return Load($"{folder}/{parts[1]}.prefab");
    }
}
