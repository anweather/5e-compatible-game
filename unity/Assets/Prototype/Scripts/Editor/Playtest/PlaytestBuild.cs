using UnityEditor;
using UnityEngine;

// Builds the playtest bot's player in batch mode (tools/playtest/build.ps1,
// playtest/DESIGN.md section 6), in the dedicated playtest-build checkout:
// the yard first (YardAutoBuild doesn't run in batch mode, and the checkout's
// yard is generated, never committed), then the Windows player as
// Prototype > Build > Windows makes it. Exits 0 when the build is good.
public static class PlaytestBuild
{
    public static void Build()
    {
        // A fresh checkout has no Generated folder (it isn't in git): the builders write into it.
        if (!AssetDatabase.IsValidFolder("Assets/Prototype/Generated")) AssetDatabase.CreateFolder("Assets/Prototype", "Generated");
        ChemYardBuilder.Build();
        string line = PrototypeBuild.Build(BuildTarget.StandaloneWindows64, "Builds/Windows/SpellChemistryValley.exe");
        bool ok = line.Contains(": OK");
        Debug.Log($"PLAYTEST BUILD: {(ok ? "OK" : "FAILED")}: {line}");
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
