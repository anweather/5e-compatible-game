using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// What libwhisper.dll needs that a Windows PC may not have (J-072): Microsoft's C++ runtime and the Vulkan loader, which
// ggml-vulkan.dll imports whether or not the GPU is used. Each Windows build fetches them once from two pinned packages,
// checks those and each file against the hashes here, and puts them beside libwhisper.dll, so they reach a player through
// the install and its updates with nothing to download by hand. They aren't in git: the repo is public and Microsoft's
// terms let them go out with a program, not on their own. A build that can't get them fails, rather than ship a voice
// that won't load. Fetched here, not from this machine, as a Mac cross-builds Windows too.
class VoiceLibsIntoBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    sealed class Package
    {
        public string Name, Url, Sha256;
        public (string entry, string sha256)[] Files;
    }

    const string Crt = "Contents/VC/Redist/MSVC/14.44.35112/x64/Microsoft.VC143.CRT/", OpenMp = "Contents/VC/Redist/MSVC/14.44.35112/x64/Microsoft.VC143.OpenMP/",
        Vulkan = "VulkanRT-X64-1.4.341.0-Components/", VulkanLicence = Vulkan + "VulkanRT-License.txt", Cache = "Library/VoiceLibs";

    // Visual Studio 17.14's redistributable: its id and hash are in Microsoft's channel manifest (aka.ms/vs/17/release/channel).
    // Vulkan's runtime components from LunarG, the version GPU drivers install, signed by Microsoft's hardware programme.
    static readonly Package[] Packages =
    {
        new()
        {
            Name = "Microsoft.VC.14.44.17.14.CRT.Redist.X64.base.vsix",
            Url = "https://download.visualstudio.microsoft.com/download/pr/45d3b8dd-bced-4b37-9974-142f748d710c/4aaf54db0bfc9435f7c3660e1a00237a4b556042bfeea64bde44c2e0194e6ee5/Microsoft.VC.14.44.17.14.CRT.Redist.X64.base.vsix",
            Sha256 = "4aaf54db0bfc9435f7c3660e1a00237a4b556042bfeea64bde44c2e0194e6ee5",
            Files = new[]
            {
                (Crt + "msvcp140.dll", "0f885b509a685d2bbfa652fed26b5fb31d88fbdab0a978c641d1c7b8aa460aa9"),
                (Crt + "vcruntime140.dll", "d5e4d9a3e835fa679450145d6a7d94e36573a509317111904d9b3712c30d9066"),
                (Crt + "vcruntime140_1.dll", "1f2d41c4aa5db0bc33ebf7b66d72943a817d7ce6cbe880502a9403823633093f"),
                (OpenMp + "vcomp140.dll", "55aba23cdcd6484fbb06f4155b8ca75adfce7a881f10afd0c49457165e677164"),
            },
        },
        new()
        {
            Name = "VulkanRT-X64-1.4.341.0-Components.zip",
            Url = "https://sdk.lunarg.com/sdk/download/1.4.341.0/windows/VulkanRT-X64-1.4.341.0-Components.zip",
            Sha256 = "20571dcc8292b80a47d68bbbfa4094d87044041f50f4e2e1f326a01e3732b226",
            Files = new[] { (Vulkan + "x64/vulkan-1.dll", "98bc6e33b384f83e8cc36174ce1ba81357611d0cbe03462e1ca58d0663034039") },
        },
    };

    /// <summary>The libraries a Windows player carries for voice, by file name.</summary>
    public static string[] Names => Packages.SelectMany(p => p.Files).Select(f => Path.GetFileName(f.entry)).ToArray();

    public int callbackOrder => 2;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
        try
        {
            foreach (var package in Packages) Fetch(package);
        }
        catch (Exception e) when (e is IOException or HttpRequestException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new BuildFailedException($"No voice libraries for the build: {e.Message}");
        }
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
        string exe = report.summary.outputPath;
        string folder = Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + "_Data", "Plugins", "x86_64");
        try
        {
            if (!File.Exists(Path.Combine(folder, "libwhisper.dll"))) throw new InvalidDataException($"libwhisper.dll isn't in {folder}");
            foreach (var package in Packages) Unpack(package, folder);
            Debug.Log($"Voice libraries: {string.Join(", ", Names)} -> {folder}");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new BuildFailedException($"No voice libraries for the build: {e.Message}");
        }
    }

    static string Cached(Package package) => Path.Combine(Cache, package.Name);

    static void Fetch(Package package)
    {
        string path = Cached(package);
        if (File.Exists(path) && Hash(File.ReadAllBytes(path)) == package.Sha256) return;
        Directory.CreateDirectory(Cache);
        Debug.Log($"Fetching {package.Url}");
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        byte[] bytes = client.GetByteArrayAsync(package.Url).GetAwaiter().GetResult();
        string got = Hash(bytes);
        if (got != package.Sha256) throw new InvalidDataException($"{package.Name} has hash {got}, not {package.Sha256}");
        File.WriteAllBytes(path, bytes);
    }

    static void Unpack(Package package, string folder)
    {
        using var archive = ZipFile.OpenRead(Cached(package));
        foreach (var (entry, sha256) in package.Files)
        {
            byte[] bytes = Read(archive, entry);
            string got = Hash(bytes);
            if (got != sha256) throw new InvalidDataException($"{entry} has hash {got}, not {sha256}");
            File.WriteAllBytes(Path.Combine(folder, Path.GetFileName(entry)), bytes);
        }
        if (archive.GetEntry(VulkanLicence) != null) File.WriteAllBytes(Path.Combine(folder, Path.GetFileName(VulkanLicence)), Read(archive, VulkanLicence));
    }

    static byte[] Read(ZipArchive archive, string entry)
    {
        var found = archive.GetEntry(entry) ?? throw new InvalidDataException($"{entry} isn't in the package");
        using var from = found.Open();
        using var to = new MemoryStream();
        from.CopyTo(to);
        return to.ToArray();
    }

    static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
}
