using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// How the clip probes film (castclip, punchclip): a JPEG for each frame at 30 fps and
// sound.wav, into a folder, for ffmpeg. It runs on a fixed clock (Time.captureDeltaTime),
// the frames from a RenderTexture and the sound from Unity's own mixer through
// AudioRenderer, so it stays in step and needs no desktop session and no sound device.
public sealed class ClipFilm
{
    const int Width = 1280, Height = 720, Fps = 30;

    readonly MonoBehaviour owner;
    readonly string folder;
    readonly List<Task> writing = new();
    readonly List<float> sound = new();
    RenderTexture target;
    Camera lens;
    bool filming;

    /// <summary>The frames filmed, and saved so far.</summary>
    public int Frames { get; private set; }
    public int Saved { get; private set; }
    /// <summary>The loudest sample in the sound.</summary>
    public float Peak { get; private set; }
    public float Seconds => Frames / (float)Fps;
    public float SoundSeconds => sound.Count / (2f * AudioSettings.outputSampleRate);

    public ClipFilm(MonoBehaviour owner, string folder)
    {
        this.owner = owner;
        this.folder = folder;
        Directory.CreateDirectory(folder);
    }

    /// <summary>Starts filming through the main camera. False if the sound can't render.</summary>
    public bool Roll()
    {
        lens = Camera.main;
        target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        lens.targetTexture = target;
        Time.captureDeltaTime = 1f / Fps;
        bool heard = AudioRenderer.Start();
        filming = true;
        owner.StartCoroutine(Film());
        return heard;
    }

    /// <summary>Stops, saves the sound, and waits for the frames to be written.</summary>
    public IEnumerator Stop()
    {
        filming = false;
        yield return null;
        yield return null;
        AudioRenderer.Stop();
        Time.captureDeltaTime = 0f;
        lens.targetTexture = null;
        AsyncGPUReadback.WaitAllRequests();
        target.Release();
        Peak = Save(Path.Combine(folder, "sound.wav"));
        var all = Task.WhenAll(writing.ToArray());
        yield return new WaitUntil(() => all.IsCompleted);
    }

    IEnumerator Film()
    {
        var end = new WaitForEndOfFrame();
        while (filming)
        {
            yield return end;
            int samples = AudioRenderer.GetSampleCountForCaptureFrame();
            if (samples > 0)
            {
                var block = new NativeArray<float>(samples * 2, Allocator.Temp);
                if (AudioRenderer.Render(block))
                    foreach (var s in block) sound.Add(s);
                block.Dispose();
            }
            string path = Path.Combine(folder, $"frame_{Frames++:00000}.jpg");
            AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError) return;
                var pixels = request.GetData<byte>().ToArray();
                Saved++;
                writing.Add(Task.Run(() => File.WriteAllBytes(path, ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_SRGB, Width, Height, 0, 88)), owner.destroyCancellationToken));
            });
        }
    }

    float Save(string path)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = sound.Count * 2, rate = AudioSettings.outputSampleRate;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + bytes);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(rate);
        w.Write(rate * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(bytes);
        float peak = 0f;
        foreach (var s in sound)
        {
            peak = Mathf.Max(peak, Mathf.Abs(s));
            w.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }
        return peak;
    }
}
