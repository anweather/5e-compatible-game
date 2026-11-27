using System;
using UnityEngine;

// Whether this Editor is a Multiplayer Play Mode virtual player, and where the main project is when it is.
// The game's self-starting parts (SoloMap, PlayLog, the rules MCP, ...) stay out of a virtual player, which
// the online probes drive (ProbeLink, in Probes/); the probes also read the main project's folder from here.
public static class VirtualPlayer
{
    public static bool Active => Application.isEditor && MainProject() != null;

    public static string MainProject()
    {
        string data = Application.dataPath.Replace('\\', '/');
        int at = data.IndexOf("/Library/VP/", StringComparison.Ordinal);
        return at < 0 ? null : data.Substring(0, at);
    }
}
