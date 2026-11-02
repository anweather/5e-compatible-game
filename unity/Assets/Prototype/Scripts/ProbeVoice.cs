using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

// The probe's voice scenario (J-061): a hold to talk from press to send, with a stand-in recogniser
// and a stand-in for claude. No microphone, no Windows speech and no real Claude run here, so
// what it proves is the seam and the box; real dictation needs a real voice.
public class VoiceProbe : ProbeScenario
{
    const string First = "make all fire", Guess = "bl", Second = "blue please";
    const float MostWait = 8f, FilmStep = 1f;

    string adapterWas, logged;

    protected override IEnumerator Run()
    {
        var chat = GameChat.Instance;
        var voice = GameVoice.Instance;
        Check("voice_here", chat != null && voice != null);
        if (voice == null) yield break;
        string folder = Path.Combine(Application.temporaryCachePath, "voice-probe");
        Directory.CreateDirectory(folder);
        logged = Path.Combine(folder, "asked.txt");
        string script = Path.Combine(folder, "claude.cmd");
        File.WriteAllText(script, $"@echo off\r\nmore > \"{logged}\"\r\necho {{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"ok\"}}\r\n");
        adapterWas = SpeechChoice.Adapter;
        var controls = caster.controls;
        bool controlsWere = controls != null && controls.enabled;
        if (controls != null) controls.enabled = true;
        try
        {
            ClaudeChat.StandIn = script;
            SpeechChoice.Adapter = SpeechChoice.StandIn;
            voice.Drop();
            PlayerPrefs.DeleteKey("speech.told");
            yield return Hold(chat, voice, controls);
            yield return Again(chat, voice);
            yield return Cannot(chat, voice, controls);
            yield return Closed(chat, voice);
            Check("the_controls_page_says_whats_bound", ControlsMenu.Rows.Any(r => r.what.Contains("Talk to Claude") && r.keys.Contains('H') && r.pad.Contains("both sticks")));
        }
        finally
        {
            voice.Drop();
            ClaudeChat.StandIn = null;
            SpeechChoice.Adapter = adapterWas;
            if (controls != null) controls.enabled = controlsWere;
        }
    }

    static IEnumerator Until(System.Func<bool> done)
    {
        for (float t = 0f; t < MostWait && !done(); t += Time.unscaledDeltaTime) yield return null;
    }

    IEnumerator Hold(GameChat chat, GameVoice voice, PlayerControls controls)
    {
        ClipFilm film = null;
        if (!string.IsNullOrEmpty(ScreenshotDir))
        {
            film = new ClipFilm(this, ScreenshotDir, withHud: true);
            film.Roll();
            yield return Wait(FilmStep);
        }
        Check("a_hold_starts", voice.Press());
        var speech = voice.Speech as StandInSpeech;
        Check("the_setting_picked_the_adapter", speech != null);
        if (speech == null) yield break;
        Check("the_box_opens_and_takes_the_controls", chat.Open && chat.Talking && (controls == null || !controls.enabled));
        Check("it_listens", speech.State == SpeechState.Listening);
        Check("told_where_the_voice_goes", chat.Lines.Contains(speech.Notice), string.Join(" | ", chat.Lines));
        Check("a_second_press_does_nothing", !voice.Press());
        if (film != null) yield return Wait(FilmStep);
        speech.Partial(First);
        yield return null;
        yield return null;
        Check("the_guess_fills_the_box", chat.Typed == First, chat.Typed);
        if (film != null) yield return Wait(FilmStep);
        speech.Final(First);
        speech.Partial(Guess);
        yield return null;
        yield return null;
        Check("words_add_up", chat.Typed == First + " " + Guess, chat.Typed);
        speech.Slow = true;
        voice.Release();
        yield return null;
        yield return null;
        Check("letting_go_waits_for_the_recogniser", chat.Open && voice.Talking && !chat.Busy);
        speech.Final(Second);
        speech.Finish();
        if (film != null) yield return Wait(FilmStep);
        yield return Until(() => !voice.Talking);
        Check("the_box_closes_and_gives_the_controls_back", !chat.Open && (controls == null || controls.enabled));
        string message = First + " " + Second;
        var said = chat.Lines.ToList();
        Check("your_words_show", said.Contains("You: " + message), string.Join(" | ", said));
        yield return Until(() => !chat.Busy);
        string asked = File.Exists(logged) ? File.ReadAllText(logged) : "";
        Check("the_words_reach_claude", asked.Contains(message), asked);
        Check("sent_once", chat.Lines.Count(l => l == "You: " + message) == 1, string.Join(" | ", chat.Lines));
        if (film != null)
        {
            yield return Wait(FilmStep * 2f);
            yield return film.Stop();
        }
    }

    IEnumerator Again(GameChat chat, GameVoice voice)
    {
        int noticesWere = chat.Lines.Count(l => l == voice.Speech.Notice);
        int sentWere = chat.Lines.Count(l => l.StartsWith("You:", System.StringComparison.Ordinal));
        voice.Press();
        var speech = (StandInSpeech)voice.Speech;
        Check("told_once_only", chat.Lines.Count(l => l == speech.Notice) == noticesWere);
        speech.Slow = false;
        voice.Release();
        yield return Until(() => !voice.Talking);
        Check("saying_nothing_sends_nothing", !chat.Open && !chat.Busy && chat.Lines.Count(l => l.StartsWith("You:", System.StringComparison.Ordinal)) == sentWere, string.Join(" | ", chat.Lines));
    }

    IEnumerator Cannot(GameChat chat, GameVoice voice, PlayerControls controls)
    {
        const string Why = "Voice needs a setting turned on.";
        var speech = (StandInSpeech)voice.Speech;
        speech.FailWith = Why;
        voice.Press();
        yield return Until(() => !voice.Talking);
        Check("cant_says_why_in_a_line", chat.Lines.Last() == Why, chat.Lines.Last());
        Check("cant_gives_everything_back", !chat.Open && !chat.Busy && (controls == null || controls.enabled));
        speech.FailWith = null;
        Check("it_tries_again_after", voice.Press());
        Check("and_listens", speech.State == SpeechState.Listening);
        voice.Release();
        yield return Until(() => !voice.Talking);
    }

    IEnumerator Closed(GameChat chat, GameVoice voice)
    {
        const string Words = "words nobody meant to send";
        voice.Press();
        var speech = (StandInSpeech)voice.Speech;
        speech.Partial(Words);
        yield return null;
        yield return null;
        chat.SetOpen(false);
        yield return Until(() => !voice.Talking);
        Check("closing_the_box_drops_the_words", !chat.Busy && !chat.Lines.Any(l => l.Contains(Words)), string.Join(" | ", chat.Lines));
        Check("box_stays_closed", !chat.Open);
    }
}
