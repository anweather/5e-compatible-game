using System.IO;

// Speech audio from a WAV file, for tests and the speech check: Open reads the file and Close hands
// over all of it, whatever the hold's length, converted to 16 kHz mono. It plays nothing aloud.
public sealed class WavAudio : ISpeechAudio
{
    readonly string path;
    float[] samples;

    public WavAudio(string path) => this.path = path;

    public string Name => Path.GetFileName(path);
    public string ChangeSource => null;
    public float Level { get; private set; }
    public bool Recording => samples != null;

    public string Open()
    {
        try
        {
            samples = SpeechSamples.FromWav(File.ReadAllBytes(path));
        }
        catch (System.Exception e) when (e is IOException or InvalidDataException or System.UnauthorizedAccessException)
        {
            samples = null;
            return $"Couldn't read the sound file {Name}: {e.Message}.";
        }
        Level = SpeechSamples.Peak(samples);
        return null;
    }

    public void Pump()
    {
    }

    public float[] Close()
    {
        var all = samples ?? new float[0];
        samples = null;
        Level = 0f;
        return all;
    }

    public void Dispose() => samples = null;
}
