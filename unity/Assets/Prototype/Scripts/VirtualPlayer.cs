using System;
using UnityEngine;

// Whether this Editor is a Multiplayer Play Mode virtual player, and where the main project is when it is.
// The game's self-starting parts (SoloMap, PlayLog, the rules MCP, ...) stay out of a virtual player, which
// the online probes drive (ProbeLink, in Probes/); the probes also read the main project's folder from here.
public static class VirtualPlayer
{
    /// <summary>True in a Multiplayer Play Mode virtual player (its project is a copy under Library/VP/).</summary>
    public static bool Active => Application.isEditor && MainProject() != null;

    /// <summary>The main project's folder when this is a virtual player (its project is &lt;main&gt;/Library/VP/&lt;id&gt;/), else null.</summary>
    public static string MainProject()
    {
        string data = Application.dataPath.Replace('\\', '/');
        int at = data.IndexOf("/Library/VP/", StringComparison.Ordinal);
        return at < 0 ? null : data.Substring(0, at);
    }
}
