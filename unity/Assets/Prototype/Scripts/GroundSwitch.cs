using System;
using System.IO;
using System.Linq;

// Whether the chunks' ground comes from the run-time builder (GroundBuilder) or the scene's baked terrains: the baked ones
// until the PC has judged the built ones (docs/design/maps-as-data.md, phase 5). A player takes -ground-builder on or
// land; the open Editor, which a shell can't pass a flag to, reads Temp/ground-builder; GROUND_BUILDER works on either.
public static class GroundSwitch
{
    public const string Off = "off", On = "on", Land = "land", Arg = "-ground-builder", Env = "GROUND_BUILDER", EditorFile = "Temp/ground-builder";

    /// <summary>off, on (the map's ground/ if it has one, else its land) or land (its land, even with a ground/).</summary>
    public static string Mode => Read(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable(Env), File.Exists(EditorFile) ? File.ReadAllText(EditorFile) : null);

    /// <summary>The mode from a command line, then the environment, then the Editor's file; anything else is off.</summary>
    public static string Read(string[] args, string env, string file)
    {
        int at = Array.IndexOf(args, Arg);
        string asked = at >= 0 && at + 1 < args.Length ? args[at + 1] : !string.IsNullOrWhiteSpace(env) ? env : file;
        asked = asked?.Trim();
        return new[] { On, Land }.Contains(asked) ? asked : Off;
    }
}
