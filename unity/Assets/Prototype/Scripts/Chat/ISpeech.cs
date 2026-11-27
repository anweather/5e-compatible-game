using System;

/// <summary>What a recogniser is doing.</summary>
public enum SpeechState { Idle, Starting, Listening, Unavailable }

/// <summary>What comes out of a recogniser: a guess at the words so far, a phrase it's sure of, or the end of a hold.</summary>
public enum SpeechHeard { Partial, Final, Done }

// The port between the game and whatever turns speech into text (J-061). GameVoice is all the game
// knows of it; each recogniser is an adapter, picked by SpeechChoice, and this file names none of
// them. An adapter's thread is its own business: everything comes out through Take, on the main
// thread, as ClaudeChat's answers do. Done always follows a Start, however it went, so a hold ends;
// Stop may take its time before that (a recogniser that works on the whole recording does its work
// then). What an adapter needs of the game beyond this is a sign the seam is wrong.
public interface ISpeech : IDisposable
{
    /// <summary>Listening, idle, or unable to; Start always tries again, as a setting may have changed.</summary>
    SpeechState State { get; }

    /// <summary>Why it's unavailable, in a line the player can act on; null otherwise.</summary>
    string Why { get; }

    /// <summary>What it's listening through, by name, for the player to check; null if it can't say.</summary>
    string Source { get; }

    /// <summary>Where the player changes what it listens through, for when nothing was heard; null if nowhere.</summary>
    string ChangeSource { get; }

    /// <summary>How loud the sound coming in is now, 0 to 1; 0 if it can't say.</summary>
    float Level { get; }

    /// <summary>What the player is told once before first using it, such as where their voice goes; null for nothing.</summary>
    string Notice { get; }

    /// <summary>Starts listening.</summary>
    void Start();

    /// <summary>Stops listening. Any last words, then Done, come through Take.</summary>
    void Stop();

    /// <summary>The next thing heard, on the main thread; false when there's nothing yet.</summary>
    bool Take(out (SpeechHeard heard, string text) line);
}
