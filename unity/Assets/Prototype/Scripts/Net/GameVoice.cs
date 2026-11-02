using UnityEngine;
using UnityEngine.InputSystem;

// Talking to Claude in the game (J-061): hold H, or both sticks in on a pad; the chat box opens,
// fills with the words as they come, and letting go sends them. All it knows of what hears the
// player is ISpeech, and it makes one only at the first hold, so nothing listens before that.
// RulesMcpServer adds it beside GameChat.
[RequireComponent(typeof(GameChat))]
public class GameVoice : MonoBehaviour
{
    const string ToldKey = "speech.told";

    GameChat chat;
    ISpeech speech;
    string said = "", guess = "";
    bool talking, held, dropped;

    /// <summary>The voice, or null where the game doesn't serve Claude.</summary>
    public static GameVoice Instance { get; private set; }

    /// <summary>The recogniser in use, once there has been a hold; for the probe.</summary>
    public ISpeech Speech => speech;

    /// <summary>Whether a hold is going, from the press until the recogniser has finished.</summary>
    public bool Talking => talking;

    void Awake()
    {
        Instance = this;
        chat = GetComponent<GameChat>();
        QuickExit.BeforeExit += Drop; // some players have no OnApplicationQuit
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

    /// <summary>Starts a hold, as the key does. False if one can't start now.</summary>
    public bool Press()
    {
        if (talking || !chat.CanOpen) return false;
        speech ??= SpeechChoice.Make();
        TellOnce(speech.Notice);
        said = guess = "";
        dropped = false;
        talking = true;
        chat.SetOpen(true);
        chat.Talking = true;
        speech.Start();
        return true;
    }

    /// <summary>Ends a hold, as letting go does: what was said goes once the recogniser has finished.</summary>
    public void Release()
    {
        if (talking) speech.Stop();
    }

    /// <summary>Forgets the recogniser, so the next hold makes the chosen one.</summary>
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
        if (!dropped) chat.Type(Joined(said, guess));
    }

    // Done: a guess still in the box was the best it had, so it goes too.
    void Finish()
    {
        talking = false;
        if (dropped) return;
        chat.SetOpen(false);
        if (speech.State == SpeechState.Unavailable) chat.Say(speech.Why);
        chat.Send(Joined(said, guess));
    }

    static string Joined(string first, string second) =>
        first.Length == 0 ? second : second.Length == 0 ? first : first + " " + second;
}
