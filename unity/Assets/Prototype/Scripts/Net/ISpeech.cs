// What a recogniser is doing.
using System;

public enum SpeechState { Idle, Starting, Listening, Unavailable }

public enum SpeechHeard { Partial, Final, Done }

public interface ISpeech : IDisposable
{
    SpeechState State { get; }

    string Why { get; }

    string Source { get; }

    string ChangeSource { get; }

    float Level { get; }

    string Notice { get; }

    void Start();

    void Stop();

    bool Take(out (SpeechHeard heard, string text) line);
}
