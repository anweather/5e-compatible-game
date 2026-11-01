using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Where the play log's lines go (PlayLog): a file, written on a thread of its own so the game
// never waits for the disk, and the marks' pictures, encoded there too. Opening one trims the
// folder to the last few logs, with their pictures.
public sealed class PlayLogFile
{
    const int KeptLogs = 10, ShotQuality = 75;
    const string Prefix = "play-log-";

    readonly BlockingCollection<Action<StreamWriter>> work = new();
    readonly Thread writer;

    /// <summary>The file's folder, its full path, and its name without the extension.</summary>
    public string Folder { get; }
    public string Path { get; }
    public string Stem { get; }

    public PlayLogFile(string folder, string stem)
    {
        Folder = folder;
        Stem = stem;
        Path = System.IO.Path.Combine(folder, stem + ".csv");
        writer = new Thread(Write) { IsBackground = true, Name = "Play log" };
        writer.Start();
    }

    /// <summary>A line for the file. Returns at once.</summary>
    public void Add(string line)
    {
        work.Add(w =>
        {
            w.WriteLine(line);
            w.Flush();
        });
    }

    /// <summary>A picture to encode and save, as RGBA bytes.</summary>
    public void AddPicture(string path, byte[] pixels, int width, int height, bool upsideDown)
    {
        work.Add(_ =>
        {
            if (upsideDown) Flip(pixels, width, height);
            File.WriteAllBytes(path, ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_SRGB, (uint)width, (uint)height, 0, ShotQuality));
        });
    }

    /// <summary>Writes what's left, and closes the file.</summary>
    public void Close()
    {
        work.CompleteAdding();
        writer.Join();
    }

    void Write()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Trim();
            using var file = new StreamWriter(Path);
            foreach (var job in work.GetConsumingEnumerable()) job(file);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Debug.LogWarning("Play log: " + e.Message); // a full disk costs the log, not the game
        }
    }

    // Leaves room for this one: a log's name sorts by when it began.
    void Trim()
    {
        var logs = Directory.GetFiles(Folder, Prefix + "*.csv").OrderBy(f => f, StringComparer.Ordinal).ToList();
        foreach (var old in logs.Take(Math.Max(0, logs.Count - (KeptLogs - 1))))
        {
            string stem = System.IO.Path.GetFileNameWithoutExtension(old);
            foreach (var shot in Directory.GetFiles(Folder, stem + "-mark-*.jpg")) File.Delete(shot);
            File.Delete(old);
        }
    }

    static void Flip(byte[] pixels, int width, int height)
    {
        int stride = width * 4;
        var row = new byte[stride];
        for (int y = 0; y < height / 2; y++)
        {
            int top = y * stride, bottom = (height - 1 - y) * stride;
            Buffer.BlockCopy(pixels, top, row, 0, stride);
            Buffer.BlockCopy(pixels, bottom, pixels, top, stride);
            Buffer.BlockCopy(row, 0, pixels, bottom, stride);
        }
    }
}
