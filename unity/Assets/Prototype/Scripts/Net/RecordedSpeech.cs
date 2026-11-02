using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// A recogniser that plays back a transcript from tools/voice-samples, for the probe: each word
// arrives as a Partial when the sample speaks it, then a Final at the sample's end, so the whole
// hold runs on a real ask's timing. It reads no audio. Unity's dictation hears only the default
// microphone and can't take a file, so this says nothing about whether real dictation works: it
// proves everything on the game's side of the port.
public sealed class RecordedSpeech : ISpeech
{
    readonly Queue<(SpeechHeard heard, string text)> heard = new();
    readonly List<(float at, string word)> words = new();
    readonly string folder;
    float seconds, startedAt;
    int spoken;
    bool finished;

    public RecordedSpeech(string folder) => this.folder = folder;

    /// <summary>The samples' folder, in a checkout.</summary>
    public static string SamplesFolder => Path.Combine(Application.dataPath, "..", "..", "tools", "voice-samples", "out");

    /// <summary>Set by the probe: the sample the next hold plays.</summary>
    public static string Next { get; set; }

    /// <summary>The whole ask, once a sample is loaded.</summary>
    public string Text => string.Join(" ", words.ConvertAll(w => w.word));

    /// <summary>How long the sample runs.</summary>
    public float Seconds => seconds;

    /// <summary>When the last word starts; the sample then has its trailing silence.</summary>
    public float LastWordAt => words.Count == 0 ? 0f : words[words.Count - 1].at;

    public SpeechState State { get; private set; }
    public string Why { get; private set; }
    public string Source => Next;
    public string ChangeSource => null;
    public float Level => 0f;
    public string Notice => null;

    public void Start()
    {
        string file = Path.Combine(folder, Next + ".txt");
        if (!File.Exists(file))
        {
            Why = "No voice sample " + Next + ". Run tools/voice-samples/make.ps1.";
            State = SpeechState.Unavailable;
            heard.Enqueue((SpeechHeard.Done, ""));
            return;
        }
        Load(file);
        Why = null;
        spoken = 0;
        finished = false;
        startedAt = Time.unscaledTime;
        State = SpeechState.Listening;
    }

    void Load(string file)
    {
        words.Clear();
        foreach (string line in File.ReadAllLines(file))
        {
            string[] parts = line.Split(' ');
            if (parts.Length != 2) continue;
            if (parts[0] == "seconds") seconds = float.Parse(parts[1], CultureInfo.InvariantCulture);
            else words.Add((float.Parse(parts[0], CultureInfo.InvariantCulture), parts[1]));
        }
    }

    public void Stop()
    {
        if (State != SpeechState.Listening) return;
        Play();
        if (!finished && spoken > 0) heard.Enqueue((SpeechHeard.Final, Spoken()));
        State = SpeechState.Idle;
        heard.Enqueue((SpeechHeard.Done, ""));
    }

    public bool Take(out (SpeechHeard heard, string text) line)
    {
        if (State == SpeechState.Listening) Play();
        return heard.TryDequeue(out line);
    }

    // The words whose time has come, and at the end of the sample the whole ask.
    void Play()
    {
        float at = Time.unscaledTime - startedAt;
        while (spoken < words.Count && words[spoken].at <= at)
        {
            spoken++;
            heard.Enqueue((SpeechHeard.Partial, Spoken()));
        }
        if (finished || spoken < words.Count || at < seconds) return;
        finished = true;
        heard.Enqueue((SpeechHeard.Final, Spoken()));
    }

    string Spoken() => string.Join(" ", words.GetRange(0, spoken).ConvertAll(w => w.word));

    public void Dispose() => heard.Clear();
}
