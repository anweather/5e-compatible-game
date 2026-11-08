// Punchy's fauna at one commit, read straight out of its git clone (no checkout): every
// species and limb asset under Assets/Game/Fauna, with GUIDs resolved through the .meta files.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace PunchySync
{
    public sealed class Asset
    {
        public string Name, Path;
        public Dictionary<string, object> Fields;
        public bool IsLimb => Fields.Text("m_EditorClassIdentifier")?.EndsWith("LimbType") ?? Path.Contains("/Limbs/");
    }

    public sealed class Snapshot
    {
        public const string Folder = "Assets/Game/Fauna";

        public string Commit;
        public readonly Dictionary<string, Asset> ByGuid = new(StringComparer.Ordinal);

        public IEnumerable<Asset> Species => ByGuid.Values.Where(a => !a.IsLimb).OrderBy(a => a.Name, StringComparer.Ordinal);
        public IEnumerable<Asset> Limbs => ByGuid.Values.Where(a => a.IsLimb).OrderBy(a => a.Name, StringComparer.Ordinal);

        public string NameOf(object reference)
        {
            var guid = UnityYaml.Guid(reference);
            return guid != null && ByGuid.TryGetValue(guid, out var a) ? a.Name : null;
        }

        public Asset Of(object reference)
        {
            var guid = UnityYaml.Guid(reference);
            return guid != null && ByGuid.TryGetValue(guid, out var a) ? a : null;
        }

        public static Snapshot Read(string clone, string rev)
        {
            var snap = new Snapshot { Commit = Git(clone, "rev-parse", rev).Trim() };
            var paths = Git(clone, "ls-tree", "-r", "--name-only", snap.Commit, "--", Folder)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.EndsWith(".asset") || p.EndsWith(".asset.meta")).ToList();
            var files = Blobs(clone, snap.Commit, paths);
            foreach (var path in paths.Where(p => p.EndsWith(".asset")))
            {
                if (!files.TryGetValue(path + ".meta", out var meta)) continue;
                var guid = UnityYaml.Parse(meta).Text("guid");
                var fields = UnityYaml.Fields(files[path]);
                if (guid == null || fields.Count == 0) continue;
                snap.ByGuid[guid] = new Asset { Name = fields.Text("m_Name") ?? System.IO.Path.GetFileNameWithoutExtension(path), Path = path, Fields = fields };
            }
            return snap;
        }

        // Many files at once through one `git cat-file --batch`.
        static Dictionary<string, string> Blobs(string clone, string commit, List<string> paths)
        {
            var psi = new ProcessStartInfo("git", "cat-file --batch")
            {
                WorkingDirectory = clone,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using var git = Process.Start(psi);
            var writer = new System.Threading.Thread(() =>
            {
                foreach (var p in paths) git.StandardInput.Write($"{commit}:{p}\n");
                git.StandardInput.Close();
            });
            writer.Start();
            var output = new Dictionary<string, string>(StringComparer.Ordinal);
            var stream = git.StandardOutput.BaseStream;
            foreach (var p in paths)
            {
                var header = ReadLine(stream).Split(' ');
                if (header.Length < 3) continue; // "missing"
                int size = int.Parse(header[2]);
                var buffer = new byte[size];
                for (int read = 0; read < size;) read += stream.Read(buffer, read, size - read);
                stream.ReadByte(); // the newline after the blob
                output[p] = Encoding.UTF8.GetString(buffer);
            }
            writer.Join();
            git.WaitForExit();
            return output;
        }

        static string ReadLine(Stream s)
        {
            var bytes = new List<byte>();
            for (int b = s.ReadByte(); b >= 0 && b != '\n'; b = s.ReadByte()) bytes.Add((byte)b);
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        public static string Git(string dir, params string[] args)
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var git = Process.Start(psi);
            var errTask = git.StandardError.ReadToEndAsync();
            string output = git.StandardOutput.ReadToEnd();
            git.WaitForExit();
            if (git.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {errTask.Result.Trim()}");
            return output;
        }
    }
}
