using System;

// Speech audio: where a transcriber's recording comes from, the default microphone or a WAV file,
// so a test or the speech check can feed known sound through the same adapter a player uses. All of
// it runs on the main thread, as Unity's Microphone and AudioClip must; what Close hands back is a
// plain array, safe to give to another thread.
public interface ISpeechAudio : IDisposable
{
    /// <summary>What it records from, by name, for the player to check.</summary>
    string Name { get; }

    /// <summary>Where the player changes it, for when nothing was heard; null if nowhere.</summary>
    string ChangeSource { get; }

    /// <summary>How loud the sound coming in is now, 0 to 1.</summary>
    float Level { get; }

    /// <summary>Whether it's recording.</summary>
    bool Recording { get; }

    /// <summary>Starts recording: null if it did, otherwise why not, in a line the player can act on.</summary>
    string Open();

    /// <summary>Copies what has come in since the last call; every frame while recording.</summary>
    void Pump();

    /// <summary>Stops recording and hands over all of it, 16 kHz mono (SpeechSamples).</summary>
    float[] Close();
}
