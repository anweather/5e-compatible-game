using UnityEngine;
using UnityEngine.InputSystem;

// Talking to Claude in the game (J-061): hold H, or both sticks in on a pad; the chat box opens,
// fills with the words as they come, and letting go sends them. All it knows of what hears the
// player is ISpeech, and it makes one only at the first hold, so nothing listens before that.
// RulesMcpServer adds it beside GameChat.
[RequireComponent(typeof(GameChat))]
public class GameVoice : MonoBehaviour
{
    const string ToldKey = "speech.told", NothingHeard = "Nothing heard";
    const string Starting = "Getting ready, a moment...", Listening = "Listening...", Finishing = "Waiting for the last words...";

    GameChat chat;
    ISpeech speech;
    string said = "", guess = "";
    bool talking, held, dropped, released;

    public static GameVoice Instance { get; private set; }

    public ISpeech Speech => speech;

    public bool Talking => talking;

    void Awake()
    {
        Instance = this;
        chat = GetComponent<GameChat>();
        QuickExit.BeforeExit += Drop;
    }

    void OnDestroy()
    {
        QuickExit.BeforeExit -= Drop;
        Drop();
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        while (speech != null && speech.Take(out var line)) Heard(line.heard, line.text);
        if (talking)
        {
            chat.TalkHint = released ? Finishing : speech.State == SpeechState.Starting ? Starting : Listening;
            chat.Level = speech.Level;
            chat.TalkSource = speech.Source;
        }
        if (talking && !dropped && !chat.Open) Cancel();
        if (held && !HoldDown())
        {
            held = false;
            Release();
        }
        else if (HoldPressed() && Press())
        {
            held = true;
        }
    }

    static bool HoldDown()
    {
        var pad = Gamepad.current;
        return (Keyboard.current != null && Keyboard.current.hKey.isPressed)
            || (pad != null && pad.leftStickButton.isPressed && pad.rightStickButton.isPressed);
    }

    static bool HoldPressed()
    {
        var pad = Gamepad.current;
        return (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame)
            || (pad != null && pad.leftStickButton.isPressed && pad.rightStickButton.isPressed
                && (pad.leftStickButton.wasPressedThisFrame || pad.rightStickButton.wasPressedThisFrame));
    }

    public bool Press()
    {
        if (talking || !chat.CanOpen) return false;
        VoiceTrace.Begin();
        VoiceTrace.Line("press");
        speech ??= SpeechChoice.Make();
        TellOnce(speech.Notice);
        said = guess = "";
        dropped = released = false;
        talking = true;
        chat.SetOpen(true);
        chat.Talking = true;
        chat.TalkHint = Starting;
        speech.Start();
        VoiceTrace.Line("press done");
        return true;
    }

    public void Release()
    {
        if (!talking) return;
        VoiceTrace.Line("release");
        released = true;
        speech.Stop();
    }

    public void Drop()
    {
        speech?.Dispose();
        speech = null;
        talking = false;
    }

    void TellOnce(string notice)
    {
        if (notice == null || PlayerPrefs.GetString(ToldKey, "") == notice) return;
        chat.Say(notice);
        PlayerPrefs.SetString(ToldKey, notice);
        PlayerPrefs.Save();
    }

    void Cancel()
    {
        VoiceTrace.Line($"DROPPED: the box closed, with {VoiceTrace.Words(Joined(said, guess))}");
        dropped = true;
        speech.Stop();
    }

    void Heard(SpeechHeard heard, string text)
    {
        switch (heard)
        {
            case SpeechHeard.Partial:
                guess = text;
                break;
            case SpeechHeard.Final:
                said = Joined(said, text);
                guess = "";
                break;
            case SpeechHeard.Done:
                Finish();
                return;
        }
        if (dropped)
        {
            VoiceTrace.Line($"DROPPED: {heard} {VoiceTrace.Words(text)}, the box is closed");
            return;
        }
        chat.Type(Joined(said, guess));
    }

    void Finish()
    {
        talking = false;
        string message = Joined(said, guess);
        VoiceTrace.Line($"done, state {speech.State}, {VoiceTrace.Words(message)}{(dropped ? " DROPPED" : "")}");
        if (dropped) return;
        chat.SetOpen(false);
        if (speech.State == SpeechState.Unavailable) chat.Say(speech.Why);
        else if (message.Length == 0) chat.Say(NothingLine());
        chat.Send(message, GameChat.FromVoice);
    }

    string NothingLine()
    {
        string line = speech.Source == null ? NothingHeard + "." : $"{NothingHeard} on {speech.Source}.";
        return speech.ChangeSource == null ? line : $"{line} To change it: {speech.ChangeSource}.";
    }

    static string Joined(string first, string second) =>
        first.Length == 0 ? second : second.Length == 0 ? first : first + " " + second;
}
