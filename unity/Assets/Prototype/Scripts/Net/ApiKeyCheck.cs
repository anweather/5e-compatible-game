using System;
using System.IO;
using System.Threading;
using Chem.Mcp;

// One check of a key, off the main thread (J-071): a typed key is sent to GET /v1/models and saved only if it works, so
// a bad key never gets as far as the disk; with nothing typed, the saved key is checked again. ApiKeySettings starts one
// and asks each frame whether it's done, so the game never waits on the network.
public sealed class ApiKeyCheck
{
    const int MostSeconds = 20;
    const string Unreadable = "The saved key can't be read on this PC. Remove it and add it again.", Unsaved = "That key works, but it couldn't be saved here.";

    /// <summary>What asks the API: the real wire, or a stand-in in a test.</summary>
    public static Func<IKeyLister> Lister { get; set; } = () => new ApiWire();

    KeyCheck done;

    /// <summary>Whether it's still waiting on the API.</summary>
    public bool Checking { get; private set; }

    /// <summary>Starts on a typed key, or the saved key if nothing is typed. Returns at once.</summary>
    public void Start(string typed)
    {
        if (Checking) return;
        Checking = true;
        var lister = Lister();
        string fresh = typed?.Trim();
        new Thread(() => Volatile.Write(ref done, Run(lister, fresh))) { IsBackground = true, Name = "API key check" }.Start();
    }

    /// <summary>On the main thread: how it came out once it has, and the models are kept for the picker. Null until then.</summary>
    public KeyCheck Take()
    {
        var result = Volatile.Read(ref done);
        if (result == null) return null;
        done = null;
        Checking = false;
        if (result.Answer == KeyAnswer.Good) ApiKey.Models = System.Linq.Enumerable.ToArray(result.Models);
        return result;
    }

    static KeyCheck Run(IKeyLister lister, string fresh)
    {
        bool typed = !string.IsNullOrEmpty(fresh);
        string key = typed ? fresh : ApiKey.Read();
        if (!typed && key == null) return ApiModels.NotSent(Unreadable);
        if (!ApiModels.LooksLikeKey(key)) return ApiModels.NotSent();
        using var stop = new CancellationTokenSource(MostSeconds * 1000);
        var result = lister.Models(key, stop.Token);
        if (!typed || result.Answer != KeyAnswer.Good) return result;
        try
        {
            ApiKey.Save(key);
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ApiModels.NotSent(Unsaved);
        }
    }
}
