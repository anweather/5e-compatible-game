using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// How the clip probes film (castclip, punchclip, dragonclip): a JPEG for each frame at 30 fps and
// sound.wav, into a folder, for ffmpeg. It runs on a fixed clock (Time.captureDeltaTime),
// the frames from a RenderTexture and the sound from Unity's own mixer through
// AudioRenderer, so it stays in step and needs no desktop session and no sound device.
// Filmed with the HUD (the menus and the HUD are IMGUI, drawn over the camera), the frames
// are the screen as it was shown instead.
public sealed class ClipFilm
{
    const int Width = 1280, Height = 720, Fps = 30;

    readonly MonoBehaviour owner;
    readonly string folder;
    readonly bool withHud;
    readonly List<Task> writing = new();
    readonly List<float> sound = new();
    RenderTexture target;
    Camera lens;
    bool filming;

    /// <summary>The frames filmed, and saved so far.</summary>
    public int Frames { get; private set; }
    public int Saved { get; private set; }
    /// <summary>Whether frames and sound are kept; while false the game runs on, for a cut, and they're dropped.</summary>
    public bool Keeping { get; set; } = true;
    /// <summary>The loudest sample in the sound.</summary>
    public float Peak { get; private set; }
    public float Seconds => Frames / (float)Fps;
    public float SoundSeconds => sound.Count / (2f * AudioSettings.outputSampleRate);

    public ClipFilm(MonoBehaviour owner, string folder, bool withHud = false)
    {
        this.owner = owner;
        this.folder = folder;
        this.withHud = withHud;
        Directory.CreateDirectory(folder);
    }

    /// <summary>Starts filming through the main camera. False if the sound can't render.</summary>
    public bool Roll()
    {
        lens = Camera.main;
        target = withHud
            ? new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            : new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        if (!withHud) lens.targetTexture = target;
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
        if (!withHud) lens.targetTexture = null;
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
            TakeSound();
            if (Keeping) TakeFrame();
        }
    }

    void TakeSound()
    {
        int samples = AudioRenderer.GetSampleCountForCaptureFrame();
        if (samples <= 0) return;
        var block = new NativeArray<float>(samples * 2, Allocator.Temp);
        if (AudioRenderer.Render(block) && Keeping)
            foreach (var s in block) sound.Add(s);
        block.Dispose();
    }

    void TakeFrame()
    {
        if (withHud) ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
        string path = Path.Combine(folder, $"frame_{Frames++:00000}.jpg");
        int width = target.width, height = target.height;
        bool upsideDown = withHud && SystemInfo.graphicsUVStartsAtTop;
        AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32, request =>
        {
            if (request.hasError) return;
            var pixels = request.GetData<byte>().ToArray();
            Saved++;
            writing.Add(Task.Run(() =>
            {
                if (upsideDown) Flip(pixels, width * 4);
                File.WriteAllBytes(path, ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_SRGB, (uint)width, (uint)height, 0, 88));
            }, owner.destroyCancellationToken));
        });
    }

    // The screen comes back upside down where textures start at the top.
    static void Flip(byte[] pixels, int rowBytes)
    {
        var row = new byte[rowBytes];
        for (int top = 0, bottom = pixels.Length - rowBytes; top < bottom; top += rowBytes, bottom -= rowBytes)
        {
            System.Buffer.BlockCopy(pixels, top, row, 0, rowBytes);
            System.Buffer.BlockCopy(pixels, bottom, pixels, top, rowBytes);
            System.Buffer.BlockCopy(row, 0, pixels, bottom, rowBytes);
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

// A probe that is a film from start to end (dragonclip): ClipFilm, and the checks any film has.
public abstract class ClipProbe : ProbeScenario
{
    ClipFilm film;

    /// <summary>Whether frames and sound are kept (ClipFilm.Keeping).</summary>
    protected bool Keeping
    {
        get => film.Keeping;
        set => film.Keeping = value;
    }

    protected float FilmedSeconds => film.Seconds;

    protected void StartFilm()
    {
        film = new ClipFilm(this, ScreenshotDir);
        Check("sound_renders_offline", film.Roll(), "AudioRenderer.Start");
        Metric("sound_rate", AudioSettings.outputSampleRate);
    }

    protected IEnumerator EndFilm()
    {
        yield return film.Stop();
        Metric("seconds", film.Seconds);
        Metric("sound_peak", film.Peak);
        Metric("sound_seconds", film.SoundSeconds);
        Check("frames_saved", film.Saved > 100, $"{film.Saved} of {film.Frames} frames");
        Check("sound_heard", film.Peak > 0.01f, $"peak {film.Peak:0.000}");
    }
}
