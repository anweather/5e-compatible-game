using System;
using System.IO;

// Where the chunks' ground comes from, now that the scene holds none (docs/design/maps-as-data.md, phase 6): on, the
// default, draws the map's ground/ if it has one, else its land; land draws its land alone, even with a ground/. A player
// takes -ground-builder land; the open Editor, which a shell can't pass a flag to, reads Temp/ground-builder;
// GROUND_BUILDER works on either.
public static class GroundSwitch
{
    public const string On = "on", Land = "land", Arg = "-ground-builder", Env = "GROUND_BUILDER", EditorFile = "Temp/ground-builder";

    /// <summary>on (the map's ground/ if it has one, else its land) or land (its land, even with a ground/).</summary>
    public static string Mode => Read(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable(Env), File.Exists(EditorFile) ? File.ReadAllText(EditorFile) : null);

    /// <summary>The mode from a command line, then the environment, then the Editor's file; anything but land is on.</summary>
    public static string Read(string[] args, string env, string file)
    {
        int at = Array.IndexOf(args, Arg);
        string asked = at >= 0 && at + 1 < args.Length ? args[at + 1] : !string.IsNullOrWhiteSpace(env) ? env : file;
        return asked?.Trim() == Land ? Land : On;
    }
}
