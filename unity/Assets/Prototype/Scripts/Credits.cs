// The credits and legal attribution, in one place: the Esc menu's Credits
// section (GameInfoMenu) shows them, and the release build writes them to
// CREDITS.txt in the zip (PrototypeBuild.Release). The SRD paragraph is the
// exact preamble CC-BY-4.0 asks for (docs/RESEARCH.md section 1); it is the
// one place the game names Wizards of the Coast.
public static class Credits
{
    public const string Srd =
        "This work includes material from the System Reference Document 5.2.1 (\"SRD 5.2.1\") by Wizards of the Coast LLC, " +
        "available at https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the Creative Commons Attribution 4.0 " +
        "International License, available at https://creativecommons.org/licenses/by/4.0/legalcode.";

    public const string Game =
        "Spell Chemistry Valley is an independent 5E-compatible game. It is not affiliated with, endorsed, " +
        "sponsored or approved by Wizards of the Coast.";

    /// <summary>The Unity Asset Store packs in the build (unity/AssetPacks.json), all by Unity Technologies.</summary>
    public static readonly string[] Assets =
    {
        "Starter Assets: First Person + Third Person Character Controllers, by Unity Technologies",
        "Particle Pack (Starter Assets), by Unity Technologies",
        "Terrain Sample Asset Pack, by Unity Technologies",
        "Unity URP Terrain Sample Project, by Unity Technologies",
    };

    public const string Engine = "Made with Unity. Unity is a trademark of Unity Technologies.";

    /// <summary>Libraries in the build whose licences ask to be credited.</summary>
    public const string Libraries = "Velopack (updates in place), MIT License. Copyright (c) 2021 Caelan Sayler, (c) 2024 Velopack Ltd.";

    /// <summary>Other people's code in the game, used with their permission (unity/Assets/Prototype/ThirdParty).</summary>
    public const string Contributions =
        "The sky and clouds, fireflies and butterflies adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy), used with permission.";

    /// <summary>All of it as plain text, for CREDITS.txt (Windows line endings: it's read in Notepad).</summary>
    public static string PlainText(string versionLabel)
    {
        const string nl = "\r\n";
        var text = new System.Text.StringBuilder();
        text.Append("Spell Chemistry Valley, ").Append(versionLabel).Append(nl).Append(nl);
        text.Append(Game).Append(nl).Append(nl);
        text.Append("RULES").Append(nl).Append(Srd).Append(nl).Append(nl);
        text.Append("ASSETS").Append(nl);
        foreach (var a in Assets) text.Append("- ").Append(a).Append(nl);
        text.Append("Used under the Unity Asset Store End User License Agreement.").Append(nl).Append(nl);
        text.Append("ENGINE").Append(nl).Append(Engine).Append(nl).Append(nl);
        text.Append("LIBRARIES").Append(nl).Append(Libraries).Append(nl).Append(nl);
        text.Append("WITH THANKS").Append(nl).Append(Contributions).Append(nl);
        return text.ToString();
    }
}
