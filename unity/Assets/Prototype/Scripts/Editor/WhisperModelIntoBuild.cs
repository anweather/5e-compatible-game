using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// The Whisper model isn't in git (StreamingAssets is ignored): every player build fetches it into
// StreamingAssets/Whisper once, from whisper.cpp's model page, and checks it against the hash here, so
// a player always ships the same bytes. A build that can't get it fails, rather than ship no voice.
// Prototype > Fetch Whisper Model does the same for an Editor that wants to run with it.
class WhisperModelIntoBuild : IPreprocessBuildWithReport
{
    const string Url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + SpeechChoice.ModelFile;
    const string Sha256 = "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002";

    public int callbackOrder => 1;

    public void OnPreprocessBuild(BuildReport report)
    {
        try
        {
            Fetch();
        }
        catch (Exception e)
        {
            throw new BuildFailedException($"No Whisper model for the build: {e.Message}");
        }
    }

    [MenuItem("Prototype/Fetch Whisper Model")]
    public static void Fetch()
    {
        string path = SpeechChoice.ModelPath;
        if (File.Exists(path) && Hash(path) == Sha256) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string part = path + ".part";
        Debug.Log($"Fetching {Url}");
        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        using (var from = client.GetStreamAsync(Url).GetAwaiter().GetResult())
        using (var to = File.Create(part))
            from.CopyTo(to);
        string got = Hash(part);
        if (got != Sha256)
        {
            File.Delete(part);
            throw new InvalidDataException($"{SpeechChoice.ModelFile} has hash {got}, not {Sha256}");
        }
        File.Delete(path);
        File.Move(part, path);
    }

    static string Hash(string path)
    {
        using var sha = SHA256.Create();
        using var file = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }
}
