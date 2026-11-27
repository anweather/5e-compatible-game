using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Sound as a transcriber takes it: 16 kHz mono floats, -1 to 1, which is what Whisper wants. Speech
// audio hands every recording over in this form, so a transcriber never converts. Plain C# with no
// Unity in it, so it runs on any thread and its tests need no Editor state.
public static class SpeechSamples
{
    /// <summary>The rate every recording is handed over at.</summary>
    public const int Rate = 16000;

    const int WavPcm = 1, WavFloat = 3, WavExtensible = 0xFFFE;

    /// <summary>Seconds of these samples at Rate.</summary>
    public static float Seconds(float[] samples) => samples.Length / (float)Rate;

    /// <summary>The loudest sample, 0 to 1.</summary>
    public static float Peak(float[] samples, int from = 0, int count = -1)
    {
        int end = count < 0 ? samples.Length : Math.Min(samples.Length, from + count);
        float peak = 0f;
        for (int i = from; i < end; i++) peak = Math.Max(peak, Math.Abs(samples[i]));
        return peak;
    }

    /// <summary>Interleaved frames of this many channels as one, by their mean.</summary>
    public static float[] Mono(IReadOnlyList<float> interleaved, int channels)
    {
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
        var mono = new float[interleaved.Count / channels];
        for (int frame = 0; frame < mono.Length; frame++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++) sum += interleaved[frame * channels + c];
            mono[frame] = sum / channels;
        }
        return mono;
    }

    /// <summary>Mono samples at one rate as another, by straight lines between them; enough for speech.</summary>
    public static float[] Resample(float[] samples, int from, int to)
    {
        if (from <= 0 || to <= 0) throw new ArgumentOutOfRangeException(nameof(from));
        if (from == to || samples.Length == 0) return samples;
        int length = (int)((long)samples.Length * to / from);
        var output = new float[length];
        double step = (double)from / to;
        for (int i = 0; i < length; i++)
        {
            double at = i * step;
            int before = (int)at;
            int after = Math.Min(before + 1, samples.Length - 1);
            float t = (float)(at - before);
            output[i] = samples[before] + (samples[after] - samples[before]) * t;
        }
        return output;
    }

    /// <summary>A WAV file's sound as a transcriber takes it. Throws InvalidDataException, with why, for one it can't read.</summary>
    public static float[] FromWav(byte[] wav)
    {
        var (interleaved, rate, channels) = ReadWav(wav);
        return Resample(Mono(interleaved, channels), rate, Rate);
    }

    /// <summary>A WAV file's samples as they are: 8, 16, 24 or 32-bit PCM, or 32-bit float, any rate and channels.</summary>
    public static (float[] samples, int rate, int channels) ReadWav(byte[] wav)
    {
        if (wav.Length < 12 || Tag(wav, 0) != "RIFF" || Tag(wav, 8) != "WAVE") throw new InvalidDataException("not a WAV file");
        var format = (code: 0, channels: 0, rate: 0, bits: 0);
        for (int at = 12; at + 8 <= wav.Length;)
        {
            string tag = Tag(wav, at);
            int body = at + 8;
            int size = BitConverter.ToInt32(wav, at + 4);
            if (size < 0 || body + size > wav.Length) size = wav.Length - body; // a writer that left the size open
            if (tag == "fmt " && size >= 16) format = Format(wav, body, size);
            else if (tag == "data")
            {
                if (format.channels < 1 || format.rate < 1) throw new InvalidDataException("no format before the sound");
                return (Decode(wav, body, size, format.code, format.bits), format.rate, format.channels);
            }
            at = body + size + (size & 1);
        }
        throw new InvalidDataException("no sound in it");
    }

    static (int code, int channels, int rate, int bits) Format(byte[] wav, int body, int size)
    {
        int code = BitConverter.ToUInt16(wav, body);
        if (code == WavExtensible && size >= 26) code = BitConverter.ToUInt16(wav, body + 24);
        return (code, BitConverter.ToUInt16(wav, body + 2), BitConverter.ToInt32(wav, body + 4), BitConverter.ToUInt16(wav, body + 14));
    }

    static float[] Decode(byte[] wav, int at, int size, int format, int bits)
    {
        int width = bits / 8;
        if (width < 1) throw new InvalidDataException($"{bits}-bit sound");
        var samples = new float[size / width];
        for (int i = 0; i < samples.Length; i++, at += width)
        {
            samples[i] = (format, bits) switch
            {
                (WavFloat, 32) => BitConverter.ToSingle(wav, at),
                (WavPcm, 8) => (wav[at] - 128) / 128f,
                (WavPcm, 16) => BitConverter.ToInt16(wav, at) / 32768f,
                (WavPcm, 24) => (wav[at] | (wav[at + 1] << 8) | ((sbyte)wav[at + 2] << 16)) / 8388608f,
                (WavPcm, 32) => BitConverter.ToInt32(wav, at) / 2147483648f,
                _ => throw new InvalidDataException($"format {format}, {bits}-bit, isn't one it reads"),
            };
        }
        return samples;
    }

    /// <summary>Samples as a 16-bit PCM WAV file, interleaved if more than one channel.</summary>
    public static byte[] ToWav(float[] samples, int rate, int channels = 1)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        int bytes = samples.Length * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + bytes);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)WavPcm);
        w.Write((short)channels);
        w.Write(rate);
        w.Write(rate * channels * 2);
        w.Write((short)(channels * 2));
        w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(bytes);
        foreach (float s in samples) w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * 32767f));
        w.Flush();
        return stream.ToArray();
    }

    static string Tag(byte[] wav, int at) => Encoding.ASCII.GetString(wav, at, 4);
}
