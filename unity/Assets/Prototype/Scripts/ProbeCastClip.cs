using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// The probe's castclip scenario: films a single, a pair, a charged and a cancelled
// cast from the side, first the old way (pose, shake and casting sounds off) and then
// as they are, with sound, for Andrew's verdict. Needs --shots <folder>: a JPEG for each
// frame at 30 fps and sound.wav. It runs on a fixed clock (Time.captureDeltaTime), the
// frames from a RenderTexture and the sound from Unity's own mixer through AudioRenderer,
// so it stays in step and needs no desktop session and no sound device (ClipProbe).
public class CastClipProbe : ClipProbe
{
    CastBeats beats;

    protected override IEnumerator Run()
    {
        beats = wizard.GetComponent<CastBeats>();
        if (string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "castclip films into --shots <folder>");
            yield break;
        }
        Directory.CreateDirectory(ScreenshotDir);
        bool casterWasOn = caster.enabled;
        float shakeWas = GameFeel.Shake;
        caster.enabled = true;
        Place(8, 12);
        var aim = Yard(14, 12);
        aim.y = grid.GroundAt(aim);
        caster.AimOverride = aim;
        wizard.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(aim - wizard.transform.position, Vector3.up));
        var t = wizard.transform;
        Cut(t.position - t.right * 4.2f + t.forward * 1.4f + Vector3.up * 2.3f, t.position + Vector3.up * 0.85f);
        yield return Wait(0.5f);

        StartFilm();

        beats.PoseOn = beats.SoundsOn = false;
        GameFeel.Shake = 0f;
        yield return Casts();
        Metric("after_starts_s", FilmedSeconds);
        beats.PoseOn = beats.SoundsOn = true;
        GameFeel.Shake = shakeWas;
        yield return Wait(1f);
        yield return Casts();
        yield return Wait(0.5f);

        yield return EndFilm();
        caster.AimOverride = null;
        caster.enabled = casterWasOn;
    }

    IEnumerator Casts()
    {
        caster.LoadElement(ElementIds.Fire);
        yield return Neutral(0.6f);
        caster.LoadElement(ElementIds.Water);
        yield return Wait(0.35f);
        caster.LoadElement(ElementIds.Fire);
        yield return Neutral(0.6f);
        caster.LoadElement(ElementIds.Fire, held: true);
        yield return Wait(2.6f);
        caster.LetGo();
        yield return Neutral(0.6f);
        caster.LoadElement(ElementIds.Earth, held: true);
        yield return Wait(1.1f);
        caster.DropCast();
        yield return Neutral(0.8f);
    }

    IEnumerator Neutral(float after)
    {
        yield return Wait(0.3f);
        for (float t = 0f; beats.Now != Beat.Neutral && t < 8f; t += Time.deltaTime) yield return null;
        yield return Wait(after);
    }
}

// Films a probe scenario to --shots: a JPEG a frame at 30 fps and sound.wav, on a fixed clock, for ffmpeg.
public abstract class ClipProbe : ProbeScenario
{
    protected const int Width = 1280, Height = 720, Fps = 30;

    /// <summary>Whether frames and sound are kept; while false the game runs on, for a cut, and they're dropped.</summary>
    protected bool Keeping { get; set; } = true;

    protected float FilmedSeconds => frames / (float)Fps;

    Camera lens;
    RenderTexture target;
    int frames, saved;
    bool filming;
    readonly List<Task> writing = new();
    readonly List<float> sound = new();

    protected void StartFilm()
    {
        lens = Camera.main;
        target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        lens.targetTexture = target;
        Time.captureDeltaTime = 1f / Fps;
        Check("sound_renders_offline", AudioRenderer.Start(), "AudioRenderer.Start");
        Metric("sound_rate", AudioSettings.outputSampleRate);
        filming = true;
        StartCoroutine(Frames());
    }

    protected IEnumerator EndFilm()
    {
        filming = false;
        yield return null;
        yield return null;
        AudioRenderer.Stop();
        Time.captureDeltaTime = 0f;
        lens.targetTexture = null;
        AsyncGPUReadback.WaitAllRequests();
        target.Release();
        Metric("seconds", FilmedSeconds);
        float peak = Save(Path.Combine(ScreenshotDir, "sound.wav"));
        Metric("sound_peak", peak);
        Metric("sound_seconds", sound.Count / (2f * AudioSettings.outputSampleRate));
        var all = Task.WhenAll(writing.ToArray());
        yield return new WaitUntil(() => all.IsCompleted);
        Check("frames_saved", saved > 100, $"{saved} of {frames} frames");
        Check("sound_heard", peak > 0.01f, $"peak {peak:0.000}");
    }

    IEnumerator Frames()
    {
        var end = new WaitForEndOfFrame();
        while (filming)
        {
            yield return end;
            int samples = AudioRenderer.GetSampleCountForCaptureFrame();
            if (samples > 0)
            {
                var block = new NativeArray<float>(samples * 2, Allocator.Temp);
                if (AudioRenderer.Render(block) && Keeping)
                    foreach (var s in block) sound.Add(s);
                block.Dispose();
            }
            if (!Keeping) continue;
            string path = Path.Combine(ScreenshotDir, $"frame_{frames++:00000}.jpg");
            AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError) return;
                var pixels = request.GetData<byte>().ToArray();
                saved++;
                writing.Add(Task.Run(() => File.WriteAllBytes(path, ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_SRGB, Width, Height, 0, 88))));
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
