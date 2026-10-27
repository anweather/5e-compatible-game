using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StarterAssets;
using UnityEngine;

// The playtest bot (playtest/DESIGN.md). Start a player build with -playtest
// and it plays on its own, then writes what it found to its run folder and
// quits; without the flag it does nothing. This is phase 1's spike: it
// checks the player can run out of the way (unfocused, minimised or moved
// off-screen, muted) and still render, take screenshots and hear its sound.
// It walks the wizard forward, screenshots from a RenderTexture and from the
// screen, measures them, and writes result.json.
//
// Flags: -playtest-out <dir> (default <persistentDataPath>/playtest/<time>),
// -playtest-seconds <n>, -playtest-offscreen, -playtest-mute. (-playtest-mute
// sets the listener's volume, which silences the bot's own audio checks too;
// tools/playtest mutes the player in the Windows mixer instead.)
public class PlaytestBot : MonoBehaviour
{
    const int Fps = 30;
    const int ShotWidth = 960, ShotHeight = 540;

    string outDir;
    float seconds = 12f;
    bool offscreen, mute;
    PlaytestAudioTap tap;
    GameObject wizard;
    readonly StringBuilder notes = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartIfAsked()
    {
        if (Environment.GetCommandLineArgs().Contains("-playtest"))
            new GameObject("Playtest bot").AddComponent<PlaytestBot>();
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        var args = Environment.GetCommandLineArgs();
        string Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        outDir = Arg("-playtest-out") ?? Path.Combine(Application.persistentDataPath, "playtest", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        if (float.TryParse(Arg("-playtest-seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) seconds = s;
        offscreen = args.Contains("-playtest-offscreen");
        mute = args.Contains("-playtest-mute");
        Directory.CreateDirectory(outDir);
    }

    IEnumerator Start()
    {
        // Game time moves exactly 1/30 s a frame (replays), and the frame rate is capped so the bot doesn't spin the GPU.
        Time.captureFramerate = Fps;
        Application.targetFrameRate = Fps;
        QualitySettings.vSyncCount = 0;
        if (offscreen) PlaytestWindow.MoveOffScreen(notes);

        var listener = FindAnyObjectByType<AudioListener>();
        if (listener != null) tap = listener.gameObject.AddComponent<PlaytestAudioTap>();
        if (mute) AudioListener.volume = 0f;

        // Wait for the yard and the wizard.
        for (int i = 0; i < 300 && wizard == null; i++)
        {
            wizard = WizardRig.FindLocal();
            yield return null;
        }
        float started = Time.realtimeSinceStartup;
        int frame0 = Time.frameCount;
        var from = wizard != null ? wizard.transform.position : Vector3.zero;

        yield return Shoot("start");
        // Walk forward for half the run, then stand.
        yield return Walk(new Vector2(0f, 1f), seconds / 2f);
        var to = wizard != null ? wizard.transform.position : Vector3.zero;
        yield return Shoot("walked");
        float standUntil = Time.time + seconds / 2f;
        while (Time.time < standUntil) yield return null;
        yield return Shoot("end");

        Write(new
        {
            wizardFound = wizard != null,
            walkedMetres = Vector3.Distance(from, to),
            gameSeconds = Time.time,
            realSeconds = Time.realtimeSinceStartup - started,
            frames = Time.frameCount - frame0,
            focused = Application.isFocused,
            screen = $"{Screen.width}x{Screen.height}",
            offscreen,
            mute,
            listenerVolume = AudioListener.volume,
            audioPeak = tap != null ? tap.Peak : -1f,
            audioRms = tap != null ? tap.Rms : -1f,
            audioNaN = tap != null && tap.SawNaN,
            audioFrames = tap != null ? tap.Frames : -1,
            gpu = SystemInfo.graphicsDeviceName,
            notes = notes.ToString(),
        });
        Application.Quit();
    }

    // Holds a move on the wizard's input (as a stick would) for some game seconds, then lets go.
    IEnumerator Walk(Vector2 move, float forSeconds)
    {
        var inputs = wizard != null ? wizard.GetComponent<StarterAssetsInputs>() : null;
        float until = Time.time + forSeconds;
        while (Time.time < until)
        {
            if (inputs != null) inputs.move = move;
            yield return null;
        }
        if (inputs != null) inputs.move = Vector2.zero;
    }

    // A RenderTexture shot from the main camera (doesn't need the window) and a screen shot, each with its mean brightness.
    IEnumerator Shoot(string label)
    {
        var cam = Camera.main;
        if (cam != null)
        {
            var rt = RenderTexture.GetTemporary(ShotWidth, ShotHeight, 24);
            var was = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = was;
            var tex = Read(rt);
            RenderTexture.ReleaseTemporary(rt);
            Save(tex, $"{label}-rt");
        }
        yield return new WaitForEndOfFrame();
        var screen = ScreenCapture.CaptureScreenshotAsTexture();
        Save(screen, $"{label}-screen");
    }

    static Texture2D Read(RenderTexture rt)
    {
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = active;
        return tex;
    }

    void Save(Texture2D tex, string name)
    {
        if (tex == null)
        {
            notes.Append($"{name}: no texture; ");
            return;
        }
        File.WriteAllBytes(Path.Combine(outDir, name + ".jpg"), tex.EncodeToJPG(80));
        var px = tex.GetPixels32();
        double sum = 0;
        int magenta = 0;
        foreach (var p in px)
        {
            sum += (p.r + p.g + p.b) / 3.0;
            if (p.r > 200 && p.b > 200 && p.g < 60) magenta++;
        }
        notes.Append(string.Format(CultureInfo.InvariantCulture, "{0} {1}x{2} brightness {3:0} magenta {4}; ", name, tex.width, tex.height, sum / px.Length, magenta));
        Destroy(tex);
    }

    void Write(object result)
    {
        File.WriteAllText(Path.Combine(outDir, "result.json"), Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
        Debug.Log($"PLAYTEST: wrote {outDir}");
    }
}
