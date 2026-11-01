using System;
using System.IO;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// A Linux x86_64 player (Mono, Vulkan only), in the same release as Windows (J-051). No Editor
// window: tools/ship-update.sh X.Y.Z --linux-only, or by hand
// -batchmode -nographics -quit -buildTarget Linux64 -executeMethod LinuxBuild.FromCommandLine
// -rebuild-yard -build-version X.Y.Z -expect-commit abc1234 -expect-net-config 123,
// into Builds/Linux/.
//
// The host turns away a player whose stamp differs (BuildVersion.Mismatch), and a stamp is
// the commit checked out. -expect-commit refuses another commit, and -expect-net-config a
// different Netcode hash before the long shader compile. A tag older than 3fe8f5f has no copy
// of this file: check that tag out and add this file to it, untracked, which WriteStamp doesn't
// count as a change, as a commit holding it couldn't join the tag's players.
//
// Vulkan and the version are set only for this build and put back afterwards, like UpdateBuild's.
public static class LinuxBuild
{
    const BuildTarget Target = BuildTarget.StandaloneLinux64;
    const string Folder = "Builds/Linux";
    const string Exe = "SpellChemistryValley.x86_64";
    const string Scene = "Assets/Prototype/Scenes/ChemistryYard.unity";

    public static void FromCommandLine()
    {
        bool ok = false;
        try
        {
            ok = BuildFolder();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        Debug.Log($"HEADLESS BUILD: {(ok ? "OK" : "FAILED")}");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool BuildFolder()
    {
        if (!AssetPackRestore.AllPresent) return Fail("Asset Store packs are missing: run Prototype > Restore Asset Packs first.");

        // Two passes, as PrototypeBuild.BuildYardIfAsked: a first pass's new materials lose their settings.
        if (!AssetDatabase.IsValidFolder("Assets/Prototype/Generated")) AssetDatabase.CreateFolder("Assets/Prototype", "Generated");
        ChemYardBuilder.Build();
        ChemYardBuilder.Build();

        string config = NetConfig().ToString(System.Globalization.CultureInfo.InvariantCulture);
        Debug.Log($"NET CONFIG (computed in the Editor from the yard, not by a player): {config}");
        string wanted = Argument("-expect-net-config");
        if (wanted != null && wanted != config) return Fail($"NET CONFIG is {config}, wanted {wanted}.");

        // What WriteStamp will see, before the long part: Player Settings are this build's own.
        string changes = BuildInfo.RunGit("status --porcelain --untracked-files=no -- . :(exclude)ProjectSettings/ProjectSettings.asset");
        if (!string.IsNullOrEmpty(changes)) return Fail($"tracked files changed, so the stamp would be dirty: {changes}");

        bool wasDefault = PlayerSettings.GetUseDefaultGraphicsAPIs(Target);
        var was = PlayerSettings.GetGraphicsAPIs(Target);
        string wasVersion = PlayerSettings.bundleVersion;
        PackRulesIntoBuild.VersionOnlyInMemory = true;
        try
        {
            PlayerSettings.bundleVersion = Argument("-build-version") ?? wasVersion;
            PlayerSettings.SetUseDefaultGraphicsAPIs(Target, false);
            PlayerSettings.SetGraphicsAPIs(Target, new[] { GraphicsDeviceType.Vulkan });
            string path = $"{Folder}/{Exe}";
            string line = PrototypeBuild.Build(Target, path);
            // The first build after switching platform can fail on an import; once more fixes it.
            if (!line.Contains(": OK")) line = PrototypeBuild.Build(Target, path);
            if (!line.Contains(": OK")) return false;
        }
        finally
        {
            PackRulesIntoBuild.VersionOnlyInMemory = false;
            PlayerSettings.bundleVersion = wasVersion;
            PlayerSettings.SetGraphicsAPIs(Target, was);
            PlayerSettings.SetUseDefaultGraphicsAPIs(Target, wasDefault);
            AssetDatabase.SaveAssets();
        }

        var stamp = JsonUtility.FromJson<BuildInfo.StampFile>(File.ReadAllText($"{Folder}/SpellChemistryValley_Data/StreamingAssets/{BuildInfo.FileName}"));
        Debug.Log($"LINUX STAMP: alpha {stamp.version} ({stamp.commit})");
        string commit = Argument("-expect-commit");
        if (commit != null && commit != stamp.commit) return Fail($"the stamp says {stamp.commit}, wanted {commit}.");
        File.WriteAllText($"{Folder}/CREDITS.txt", Credits.PlainText($"alpha {stamp.version}"));
        return true;
    }

    // The hash NetCommandLine logs, from the scene settings and prefab lists a player loads.
    static ulong NetConfig()
    {
        EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
        var manager = UnityEngine.Object.FindAnyObjectByType<NetworkManager>();
        var config = manager.NetworkConfig;
        config.Prefabs.Initialize();
        // Starting Netcode registers the player prefab too (NetworkPrefabHandler.RegisterPlayerPrefab).
        config.Prefabs.Add(new NetworkPrefab { Prefab = config.PlayerPrefab });
        return config.GetConfig(false);
    }

    static string Argument(string flag)
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, flag);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    static bool Fail(string why)
    {
        Debug.LogError("Linux build: FAILED, " + why);
        return false;
    }
}
