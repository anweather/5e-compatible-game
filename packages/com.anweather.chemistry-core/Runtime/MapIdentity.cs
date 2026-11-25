// Which map a peer has (docs/design/maps-as-data.md, section 2): a map's hash is over every file of its folder, so two
// peers with the same id and the same hash have the same land, cells, things and places. A joining copy offers the id and
// hash of each map it has, and the host turns away a copy without its own.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Chem
{
    /// <summary>Map ids and hashes, and what the host says to a peer without its map.</summary>
    public static class MapIdentity
    {
        const char Between = ',', IdFromHash = ':';
        const string MapFlag = "-map";
        const ulong Offset = 14695981039346656037, Prime = 1099511628211;

        /// <summary>The hash of a map's folder: every file's path under it and its bytes, in path order, whatever the machine; a generated map's inputs.</summary>
        public static string Hash(string worldDir)
        {
            // A map made from a world file is what it's made from: its bake is a function of those (GeneratedMap).
            if (GeneratedMap.IsGenerated(worldDir)) return GeneratedMap.InputHash(worldDir);
            ulong hash = Offset;
            var files = Directory.GetFiles(worldDir, "*", SearchOption.AllDirectories)
                .Select(f => (file: f, name: Path.GetRelativePath(worldDir, f).Replace('\\', '/')))
                .Where(f => !Skipped(f.name))
                .OrderBy(f => f.name, StringComparer.Ordinal);
            foreach (var (file, name) in files)
            {
                hash = Add(hash, System.Text.Encoding.UTF8.GetBytes(name));
                hash = Add(hash, File.ReadAllBytes(file));
            }
            return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }

        // What a machine adds beside a map's files: Unity's .meta files, and dot files such as .DS_Store.
        static bool Skipped(string name) => name.EndsWith(".meta", StringComparison.Ordinal) || Path.GetFileName(name)[0] == '.';

        static ulong Add(ulong hash, byte[] bytes)
        {
            foreach (byte b in bytes) hash = (hash ^ b) * Prime;
            return hash;
        }

        /// <summary>The maps these folders have, for the connection payload: "id:hash,id:hash".</summary>
        public static string Offered(GameFolders folders)
        {
            if (!Directory.Exists(folders.Maps)) return "";
            var maps = Directory.GetDirectories(folders.Maps).Where(d => File.Exists(Path.Combine(d, MapFile.FileName)))
                .OrderBy(d => d, StringComparer.Ordinal);
            return string.Join(Between.ToString(), maps.Select(d => Path.GetFileName(d) + IdFromHash + Hash(d)));
        }

        /// <summary>Why the host turns a joining peer away for its maps, or null when it has the host's.</summary>
        public static string Refusal(string hostId, string hostName, string hostHash, string offered)
        {
            var maps = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string map in (offered ?? "").Split(new[] { Between }, StringSplitOptions.RemoveEmptyEntries))
            {
                int at = map.IndexOf(IdFromHash);
                if (at > 0) maps[map.Substring(0, at)] = map.Substring(at + 1);
            }
            if (!maps.TryGetValue(hostId, out string hash)) return $"The host is on {hostName}, but your copy doesn't have that map.";
            return hash == hostHash ? null : $"The host is on {hostName}, but your copy of that map is different.";
        }

        /// <summary>The map named on the command line by -map, or the default: the host's pick until there's a menu for it.</summary>
        public static string Named(string[] args)
        {
            int at = Array.IndexOf(args, MapFlag);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : GameFolders.DefaultMap;
        }
    }
}
