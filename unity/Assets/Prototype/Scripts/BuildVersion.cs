using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

// The game's version, as plain data with no Unity in it, so EditMode tests can
// check it (BuildVersionTests): parsing and comparing "X.Y.Z" versions, picking
// the newest alpha-X.Y.Z release out of GitHub's releases list (UpdateCheck),
// and the version handshake a joining player sends the host (NetSession).
// BuildInfo says which version and commit this copy of the game is.
public static class BuildVersion
{
    /// <summary>Where players download the game; also where a version mismatch sends them.</summary>
    public const string ReleasesPage = "https://github.com/anweather/5e-compatible-game/releases";

    /// <summary>GitHub's list of releases, newest first, pre-releases included.</summary>
    public const string ReleasesApi = "https://api.github.com/repos/anweather/5e-compatible-game/releases?per_page=10";

    /// <summary>Release tags are this plus the version: alpha-0.1.0.</summary>
    public const string TagPrefix = "alpha-";

    /// <summary>
    /// Parses "X.Y.Z" (a leading "v" or "alpha-" and anything after a '-' or '+' are
    /// ignored) into its three numbers, or null when it isn't one.
    /// </summary>
    public static int[] Parse(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        string v = version.Trim();
        if (v.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)) v = v.Substring(TagPrefix.Length);
        if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v.Substring(1);
        int cut = v.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0) v = v.Substring(0, cut);
        var parts = v.Split('.');
        if (parts.Length != 3) return null;
        var numbers = new int[3];
        for (int i = 0; i < 3; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return null;
        return numbers;
    }

    /// <summary>Negative when a is older than b, 0 when equal, positive when newer. Unparseable versions sort oldest.</summary>
    public static int Compare(string a, string b)
    {
        var x = Parse(a);
        var y = Parse(b);
        if (x == null || y == null) return (x == null ? 0 : 1) - (y == null ? 0 : 1);
        for (int i = 0; i < 3; i++)
            if (x[i] != y[i]) return x[i].CompareTo(y[i]);
        return 0;
    }

    /// <summary>A published release newer than this copy of the game.</summary>
    public sealed class Release
    {
        public string Version;
        public string Url;
    }

    /// <summary>
    /// Reads GitHub's releases JSON and returns the newest alpha-X.Y.Z release that is
    /// newer than <paramref name="current"/>, or null when there is none (or the JSON
    /// isn't a releases list). Drafts are skipped; pre-releases count.
    /// </summary>
    public static Release NewestRelease(string releasesJson, string current)
    {
        JArray releases;
        try { releases = JArray.Parse(releasesJson); }
        catch (Newtonsoft.Json.JsonException) { return null; }
        Release best = null;
        foreach (var token in releases)
        {
            if (!(token is JObject release)) continue;
            if (release.Value<bool?>("draft") == true) continue;
            string tag = release.Value<string>("tag_name");
            if (tag == null || !tag.StartsWith(TagPrefix, StringComparison.Ordinal) || Parse(tag) == null) continue;
            string version = tag.Substring(TagPrefix.Length);
            if (Compare(version, current) <= 0) continue;
            if (best != null && Compare(version, best.Version) <= 0) continue;
            best = new Release { Version = version, Url = release.Value<string>("html_url") ?? ReleasesPage };
        }
        return best;
    }

    // --- the join handshake -----------------------------------------------------

    /// <summary>Who a player is, version-wise: sent in the connection request, checked by the host.</summary>
    public sealed class Stamp
    {
        public string Version;
        public string Commit;
        /// <summary>Running in the Unity Editor: its commit is whatever is checked out, so only the version counts.</summary>
        public bool Editor;

        public string Label => $"alpha {Version}" + (string.IsNullOrEmpty(Commit) ? "" : $" ({Commit})");
    }

    const string HandshakeTag = "scv1";

    /// <summary>The connection payload: "scv1|version|commit|editor".</summary>
    public static byte[] Encode(Stamp stamp) =>
        System.Text.Encoding.UTF8.GetBytes($"{HandshakeTag}|{stamp.Version}|{stamp.Commit}|{(stamp.Editor ? 1 : 0)}");

    /// <summary>Reads a connection payload, or null when it isn't one (an older or foreign build).</summary>
    public static Stamp Decode(byte[] payload)
    {
        if (payload == null || payload.Length == 0 || payload.Length > 256) return null;
        string text;
        try { text = System.Text.Encoding.UTF8.GetString(payload); }
        catch (ArgumentException) { return null; }
        var parts = text.Split('|');
        if (parts.Length != 4 || parts[0] != HandshakeTag) return null;
        return new Stamp { Version = parts[1], Commit = parts[2], Editor = parts[3] == "1" };
    }

    /// <summary>
    /// Why the host turns a joining player away, or null when they can play together: the
    /// versions must match, and so must the commits unless either side is the Editor.
    /// </summary>
    public static string Mismatch(Stamp host, Stamp client)
    {
        if (client == null)
            return $"Host is on {host.Label}; your copy is an older or different build. Both players need the same version: {ReleasesPage}";
        bool sameVersion = string.Equals(host.Version, client.Version, StringComparison.Ordinal);
        bool sameCommit = host.Editor || client.Editor || string.Equals(host.Commit, client.Commit, StringComparison.Ordinal);
        if (sameVersion && sameCommit) return null;
        string fix = Compare(host.Version, client.Version) > 0
            ? $"Update from {ReleasesPage}"
            : $"The host needs to update from {ReleasesPage}";
        return $"Host is on {host.Label}; you're on {client.Label}. {fix}";
    }

    /// <summary>
    /// What to tell a player whose join failed, from Netcode's DisconnectReason: the host's
    /// own reason when it sent one, otherwise a guess. Netcode refuses a different build
    /// (different network prefabs) before the host's approval runs, without a reason.
    /// </summary>
    public static string JoinFailure(string disconnectReason)
    {
        // Netcode's own texts, not the host's: "[Disconnect Event]..." when the server sent
        // nothing, and "Client-3 disconnected by server." when it refused the config hash.
        bool fromHost = !string.IsNullOrWhiteSpace(disconnectReason)
            && !disconnectReason.StartsWith("[Disconnect Event]", StringComparison.Ordinal)
            && !System.Text.RegularExpressions.Regex.IsMatch(disconnectReason, @"^Client-\d+ disconnected by server\.?$");
        return fromHost
            ? $"Couldn't join: {disconnectReason}"
            : $"Couldn't join: the host refused the connection, probably a different version. Both players need the same build: {ReleasesPage}";
    }
}
