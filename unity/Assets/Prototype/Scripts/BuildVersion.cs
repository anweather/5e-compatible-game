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
    public const string RepoUrl = "https://github.com/anweather/5e-compatible-game";

    public const string ReleasesPage = RepoUrl + "/releases";

    public const string ReleasesApi = "https://api.github.com/repos/anweather/5e-compatible-game/releases?per_page=10";

    public const string TagPrefix = "alpha-";

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

    public static int Compare(string a, string b)
    {
        var x = Parse(a);
        var y = Parse(b);
        if (x == null || y == null) return (x == null ? 0 : 1) - (y == null ? 0 : 1);
        for (int i = 0; i < 3; i++)
            if (x[i] != y[i]) return x[i].CompareTo(y[i]);
        return 0;
    }

    public sealed class Release
    {
        public string Version;
        public string Url;
    }

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

    public sealed class Stamp
    {
        public string Version;
        public string Commit;
        public bool Editor;
        public string Maps = "";

        public string Label => $"alpha {Version}" + (string.IsNullOrEmpty(Commit) ? "" : $" ({Commit})");
    }

    const string HandshakeTag = "scv1";

    const int MostPayload = 1024;

    public static byte[] Encode(Stamp stamp, string maps = "") =>
        System.Text.Encoding.UTF8.GetBytes($"{HandshakeTag}|{stamp.Version}|{stamp.Commit}|{(stamp.Editor ? 1 : 0)}|{maps}");

    public static Stamp Decode(byte[] payload)
    {
        if (payload == null || payload.Length == 0 || payload.Length > MostPayload) return null;
        string text;
        try { text = System.Text.Encoding.UTF8.GetString(payload); }
        catch (ArgumentException) { return null; }
        var parts = text.Split('|');
        if (parts.Length != 5 || parts[0] != HandshakeTag) return null;
        return new Stamp { Version = parts[1], Commit = parts[2], Editor = parts[3] == "1", Maps = parts[4] };
    }

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

    public static string JoinFailure(string disconnectReason)
    {
        bool fromHost = !string.IsNullOrWhiteSpace(disconnectReason)
            && !disconnectReason.StartsWith("[Disconnect Event]", StringComparison.Ordinal)
            && !System.Text.RegularExpressions.Regex.IsMatch(disconnectReason, @"^Client-\d+ disconnected by server\.?$");
        return fromHost
            ? $"Couldn't join: {disconnectReason}"
            : $"Couldn't join: the host refused the connection, probably a different version. Both players need the same build: {ReleasesPage}";
    }
}
