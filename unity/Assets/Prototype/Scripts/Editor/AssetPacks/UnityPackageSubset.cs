using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

// Cuts a .unitypackage down to the assets under some folders plus
// everything they reference, so a large sample project can donate a few
// assets (its trees, its skybox) without its scenes and project settings.
//
// A .unitypackage is a gzipped tar with one folder per asset, named by its
// GUID, holding "pathname" (the asset's path), "asset" (its file) and
// "asset.meta". References between assets are "guid: <32 hex>" in the text
// of YAML assets, shader graphs and .meta files.
public static class UnityPackageSubset
{
    const int Block = 512;
    const long MaxScannedBytes = 4 * 1024 * 1024;
    static readonly Regex GuidRef = new Regex("guid\"?\\s*:\\s*\"?([0-9a-f]{32})", RegexOptions.Compiled);

    sealed class AssetInfo
    {
        public string Path;
        public bool HasFile;
        public readonly HashSet<string> References = new HashSet<string>();
    }

    /// <summary>
    /// Writes the subset to <paramref name="destination"/> and returns how many assets it holds.
    /// Assets for which <paramref name="alreadyPresent"/> is true are left out: Unity would write
    /// an imported asset over any existing asset with the same GUID, wherever that one lives.
    /// </summary>
    public static int Create(string source, string destination, IReadOnlyList<string> includePrefixes, Func<string, bool> alreadyPresent)
    {
        var assets = Scan(source);
        var keep = Closure(assets, includePrefixes, alreadyPresent);
        Copy(source, destination, keep);
        return keep.Count;
    }

    // Pass 1: every asset's path and the GUIDs its text refers to.
    static Dictionary<string, AssetInfo> Scan(string source)
    {
        var assets = new Dictionary<string, AssetInfo>();
        foreach (var entry in Read(source))
        {
            if (entry.Guid == null) continue;
            if (!assets.TryGetValue(entry.Guid, out var info)) assets[entry.Guid] = info = new AssetInfo();
            if (entry.Part == "asset") info.HasFile = true;
            if (entry.Part == "pathname") info.Path = Encoding.UTF8.GetString(entry.Data).Split('\n')[0].Trim();
            else if (entry.Data != null) foreach (Match m in GuidRef.Matches(Encoding.UTF8.GetString(entry.Data))) info.References.Add(m.Groups[1].Value);
        }
        return assets;
    }

    // Files under the prefixes and everything they reference, transitively. Folder entries are
    // left out (Unity creates folders from file paths), so folder GUIDs shared between sample
    // packs can't redirect files into another pack's folder.
    static HashSet<string> Closure(Dictionary<string, AssetInfo> assets, IReadOnlyList<string> prefixes, Func<string, bool> alreadyPresent)
    {
        var keep = new HashSet<string>();
        var queue = new Queue<string>();
        foreach (var kv in assets)
        {
            var path = kv.Value.Path;
            if (path == null || path.StartsWith("ProjectSettings/") || path.StartsWith("Packages/")) continue;
            foreach (var prefix in prefixes)
                if (path == prefix || path.StartsWith(prefix + "/")) { queue.Enqueue(kv.Key); break; }
        }
        while (queue.Count > 0)
        {
            var guid = queue.Dequeue();
            if (!assets.TryGetValue(guid, out var info) || info.Path == null || !info.HasFile || alreadyPresent(guid) || !keep.Add(guid)) continue;
            foreach (var r in info.References) queue.Enqueue(r);
        }
        return keep;
    }

    // Pass 2: copy the kept assets' entries into a new package.
    static void Copy(string source, string destination, HashSet<string> keep)
    {
        using var file = File.Create(destination);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        foreach (var entry in Read(source, keepData: guid => keep.Contains(guid)))
            if (entry.Guid != null && keep.Contains(entry.Guid) && entry.Data != null)
                Write(gzip, entry.Name, entry.Data);
        gzip.Write(new byte[Block * 2], 0, Block * 2);
    }

    struct Entry
    {
        public string Name, Guid, Part;
        public byte[] Data;
    }

    // Streams the tar inside the gzip. Without keepData, only small text-like parts are loaded.
    static IEnumerable<Entry> Read(string source, Func<string, bool> keepData = null)
    {
        using var file = File.OpenRead(source);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        var header = new byte[Block];
        while (ReadFully(gzip, header, Block))
        {
            if (header[0] == 0) yield break;
            string name = Field(header, 0, 100);
            string prefix = Field(header, 345, 155);
            if (prefix.Length > 0) name = prefix + "/" + name;
            long size = Convert.ToInt64(Field(header, 124, 12).Trim() is var s && s.Length > 0 ? s : "0", 8);
            char type = (char)header[156];

            var parts = name.TrimStart('.', '/').Split('/');
            var entry = new Entry { Name = name.TrimStart('.', '/') };
            if (type != '5' && parts.Length == 2 && parts[0].Length == 32)
            {
                entry.Guid = parts[0];
                entry.Part = parts[1];
            }

            bool load = entry.Guid != null && (keepData != null
                ? keepData(entry.Guid)
                : entry.Part == "pathname" || entry.Part == "asset.meta" || (entry.Part == "asset" && size <= MaxScannedBytes));
            long padded = (size + Block - 1) / Block * Block;
            if (load)
            {
                entry.Data = new byte[size];
                ReadFully(gzip, entry.Data, (int)size);
                if (keepData == null && entry.Part == "asset" && !LooksLikeText(entry.Data)) entry.Data = null;
                Skip(gzip, padded - size);
            }
            else Skip(gzip, padded);
            yield return entry;
        }
    }

    static bool LooksLikeText(byte[] data)
    {
        int n = Math.Min(data.Length, 512);
        for (int i = 0; i < n; i++) if (data[i] == 0) return false;
        return true;
    }

    static void Write(Stream output, string name, byte[] data)
    {
        var header = new byte[Block];
        var nameBytes = Encoding.UTF8.GetBytes(name);
        Array.Copy(nameBytes, header, Math.Min(nameBytes.Length, 100));
        WriteOctal(header, 100, 8, Convert.ToInt64("644", 8));
        WriteOctal(header, 108, 8, 0);
        WriteOctal(header, 116, 8, 0);
        WriteOctal(header, 124, 12, data.Length);
        WriteOctal(header, 136, 12, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        header[156] = (byte)'0';
        Encoding.ASCII.GetBytes("ustar\0").CopyTo(header, 257);
        header[263] = (byte)'0';
        header[264] = (byte)'0';
        for (int i = 148; i < 156; i++) header[i] = (byte)' ';
        int sum = 0;
        foreach (var b in header) sum += b;
        WriteOctal(header, 148, 7, sum);
        output.Write(header, 0, Block);
        output.Write(data, 0, data.Length);
        int pad = (int)((Block - data.Length % Block) % Block);
        output.Write(new byte[pad], 0, pad);
    }

    static void WriteOctal(byte[] buffer, int offset, int length, long value)
    {
        var text = Convert.ToString(value, 8).PadLeft(length - 1, '0');
        Encoding.ASCII.GetBytes(text).CopyTo(buffer, offset);
        buffer[offset + length - 1] = 0;
    }

    static string Field(byte[] header, int offset, int length)
    {
        int end = offset;
        while (end < offset + length && header[end] != 0) end++;
        return Encoding.UTF8.GetString(header, offset, end - offset);
    }

    static bool ReadFully(Stream s, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = s.Read(buffer, read, count - read);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }

    static void Skip(Stream s, long count)
    {
        var scratch = new byte[64 * 1024];
        while (count > 0)
        {
            int n = s.Read(scratch, 0, (int)Math.Min(scratch.Length, count));
            if (n == 0) return;
            count -= n;
        }
    }
}
