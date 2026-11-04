using System;
using System.IO;
using System.Text;
using NUnit.Framework;

// Speech audio's conversion: any WAV a recording might come as (rate, channels, 16/24-bit PCM or
// float) becomes 16 kHz mono floats, the form Whisper takes.
public class SpeechSamplesTests
{
    const int Pcm = 1, Float = 3;

    // A WAV of this format around these bytes, with an odd-sized chunk before the sound, as some writers add.
    static byte[] Wav(int format, int rate, int channels, int bits, byte[] data)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(0);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)format);
        w.Write((short)channels);
        w.Write(rate);
        w.Write(rate * channels * bits / 8);
        w.Write((short)(channels * bits / 8));
        w.Write((short)bits);
        w.Write(Encoding.ASCII.GetBytes("LIST"));
        w.Write(3);
        w.Write(new byte[] { 1, 2, 3, 0 }); // three bytes and the pad
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(data.Length);
        w.Write(data);
        w.Flush();
        return stream.ToArray();
    }

    // One second of a 440 Hz tone at this rate: the left channel the tone, any others silent.
    static float[] Tone(int rate, int channels)
    {
        var samples = new float[rate * channels];
        for (int i = 0; i < rate; i++) samples[i * channels] = 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / rate);
        return samples;
    }

    static int Crossings(float[] samples)
    {
        int crossings = 0;
        for (int i = 1; i < samples.Length; i++)
            if ((samples[i - 1] < 0f) != (samples[i] < 0f)) crossings++;
        return crossings;
    }

    [Test]
    public void SixteenKilohertzMonoComesThroughAsItIs()
    {
        var tone = Tone(SpeechSamples.Rate, 1);
        var samples = SpeechSamples.FromWav(SpeechSamples.ToWav(tone, SpeechSamples.Rate));
        Assert.AreEqual(tone.Length, samples.Length);
        for (int i = 0; i < tone.Length; i += 97) Assert.AreEqual(tone[i], samples[i], 1f / 16000f);
    }

    [Test]
    public void CdStereoBecomesSixteenKilohertzMono()
    {
        var samples = SpeechSamples.FromWav(SpeechSamples.ToWav(Tone(44100, 2), 44100, 2));
        Assert.AreEqual(SpeechSamples.Rate, samples.Length, 1);
        Assert.AreEqual(0.25f, SpeechSamples.Peak(samples), 0.01f, "the two channels weren't averaged");
        Assert.AreEqual(880, Crossings(samples), 4, "the tone changed pitch");
    }

    [Test]
    public void FloatAt48KilohertzBecomesSixteenKilohertz()
    {
        var tone = Tone(48000, 1);
        var bytes = new byte[tone.Length * 4];
        Buffer.BlockCopy(tone, 0, bytes, 0, bytes.Length);
        var samples = SpeechSamples.FromWav(Wav(Float, 48000, 1, 32, bytes));
        Assert.AreEqual(SpeechSamples.Rate, samples.Length);
        Assert.AreEqual(0.5f, SpeechSamples.Peak(samples), 0.01f);
        Assert.AreEqual(880, Crossings(samples), 4);
    }

    [Test]
    public void TwentyFourBitPcmReads()
    {
        // Half scale, then minus half scale.
        var bytes = new byte[] { 0x00, 0x00, 0x40, 0x00, 0x00, 0xC0 };
        var (samples, rate, channels) = SpeechSamples.ReadWav(Wav(Pcm, 16000, 1, 24, bytes));
        Assert.AreEqual((16000, 1), (rate, channels));
        Assert.AreEqual(new[] { 0.5f, -0.5f }, samples);
    }

    [Test]
    public void SomethingElseSaysWhy()
    {
        Assert.Throws<InvalidDataException>(() => SpeechSamples.FromWav(Encoding.ASCII.GetBytes("not a wav file at all")));
        Assert.Throws<InvalidDataException>(() => SpeechSamples.FromWav(Wav(2, 16000, 1, 4, new byte[8])));
    }
}
