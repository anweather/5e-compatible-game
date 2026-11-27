using System;

// Speech audio: where a transcriber's recording comes from, the default microphone or a WAV file,
// so a test or the speech check can feed known sound through the same adapter a player uses. All of
// it runs on the main thread, as Unity's Microphone and AudioClip must; what Close hands back is a
// plain array, safe to give to another thread.
public interface ISpeechAudio : IDisposable
{
    string Name { get; }

    string ChangeSource { get; }

    float Level { get; }

    bool Recording { get; }

    string Open();

    void Pump();

    float[] Close();
}
