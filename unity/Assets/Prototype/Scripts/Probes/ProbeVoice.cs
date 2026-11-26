using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

// The probe's voice scenario (J-061): a hold to talk from press to send, with a stand-in recogniser,
// then again from recorded samples, and a stand-in for claude. No microphone, no Windows speech and
// no real Claude run here, so what it proves is the seam, the box and the queue; real dictation
// needs a real voice.
public class VoiceProbe : ProbeScenario
{
    const string First = "make all fire", Guess = "bl", Second = "blue please";
    const string SampleGreen = "fire-near-me-green", SampleDay = "day-time", SampleNoisy = "undo-noisy";
    const float MostWait = 8f, MostQueueWait = 20f, FilmStep = 1f;
    const int SlowClaudeSeconds = 6;

    string adapterWas, logged, folder;

    protected override IEnumerator Run()
    {
        var chat = GameChat.Instance;
        var voice = GameVoice.Instance;
        Check("voice_here", chat != null && voice != null);
        if (voice == null) yield break;
        folder = Path.Combine(Application.temporaryCachePath, "voice-probe");
        Directory.CreateDirectory(folder);
        logged = Path.Combine(folder, "asked.txt");
        File.Delete(logged);
        adapterWas = SpeechChoice.Adapter;
        var controls = caster.controls;
        bool controlsWere = controls != null && controls.enabled;
        if (controls != null) controls.enabled = true;
        try
        {
            ClaudeChat.StandIn = StandInClaude("claude", 0);
            SpeechChoice.Adapter = SpeechChoice.StandIn;
            voice.Drop();
            PlayerPrefs.DeleteKey("speech.told");
            yield return Hold(chat, voice, controls);
            yield return Again(chat, voice);
            yield return Cannot(chat, voice, controls);
            yield return Closed(chat, voice);
            SpeechChoice.Adapter = SpeechChoice.Recorded;
            voice.Drop();
            yield return Sample(chat, voice);
            yield return Early(chat, voice);
            yield return Queue(chat, voice);
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

    // A script standing in for claude: it notes what came on stdin, waits, then answers.
    string StandInClaude(string name, int seconds)
    {
        string script = Path.Combine(folder, name + ".cmd");
        string wait = seconds > 0 ? $"ping -n {seconds + 1} 127.0.0.1 > nul\r\n" : "";
        File.WriteAllText(script, $"@echo off\r\nmore >> \"{logged}\"\r\n{wait}echo {{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"ok\"}}\r\n");
        return script;
    }

    string Asked() => File.Exists(logged) ? File.ReadAllText(logged) : "";

    static IEnumerator Until(System.Func<bool> done, float most = MostWait)
    {
        for (float t = 0f; t < most && !done(); t += Time.unscaledDeltaTime) yield return null;
    }

    static RecordedSpeech Play(GameVoice voice, string sample)
    {
        RecordedSpeech.Next = sample;
        voice.Press();
        return voice.Speech as RecordedSpeech;
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
        speech.Level = 0.5f;
        yield return null;
        yield return null;
        Check("the_level_reaches_the_box", Mathf.Approximately(chat.Level, 0.5f));
        Check("the_box_says_its_listening", chat.TalkHint.Contains("Listening"), chat.TalkHint);
        Check("the_box_names_what_listens", chat.TalkSource == speech.Source, chat.TalkSource);
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
        Check("the_box_says_its_waiting", chat.TalkHint.Contains("last words"), chat.TalkHint);
        speech.Final(Second);
        speech.Finish();
        if (film != null) yield return Wait(FilmStep);
        yield return Until(() => !voice.Talking);
        Check("the_box_closes_and_gives_the_controls_back", !chat.Open && (controls == null || controls.enabled));
        string message = First + " " + Second;
        var said = chat.Lines.ToList();
        Check("your_words_show", said.Contains("You: " + message), string.Join(" | ", said));
        yield return Until(() => !chat.Busy);
        Check("the_words_reach_claude", Asked().Contains(message), Asked());
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
        Check("nothing_heard_says_so_and_names_what_listened", chat.Lines.Last().Contains("Nothing heard") && chat.Lines.Last().Contains(speech.Source) && chat.Lines.Last().Contains(speech.ChangeSource), chat.Lines.Last());
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
        speech.SlowStart = true;
        Check("it_tries_again_after", voice.Press());
        yield return null;
        yield return null;
        Check("the_box_says_when_its_still_starting", speech.State == SpeechState.Starting && chat.TalkHint.Contains("ready"), chat.TalkHint);
        speech.SlowStart = false;
        speech.Ready();
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

    // A whole hold from a sample: the words come one by one on its timing, then it goes.
    IEnumerator Sample(GameChat chat, GameVoice voice)
    {
        var speech = Play(voice, SampleGreen);
        Check("the_sample_plays", speech != null && speech.State == SpeechState.Listening, speech?.Why);
        if (speech == null || speech.State != SpeechState.Listening)
        {
            voice.Release();
            yield return Until(() => !voice.Talking);
            yield break;
        }
        yield return WaitReal(speech.LastWordAt / 2f);
        string halfway = chat.Typed;
        Check("words_come_as_spoken", halfway.Length > 0 && speech.Text.StartsWith(halfway, System.StringComparison.Ordinal) && halfway != speech.Text, halfway);
        yield return WaitReal(speech.Seconds + 0.3f - speech.LastWordAt / 2f);
        Check("the_whole_ask_is_in_the_box", chat.Typed == speech.Text, chat.Typed);
        voice.Release();
        yield return Until(() => !voice.Talking);
        Check("the_sample_is_sent", chat.Lines.Contains("You: " + speech.Text), string.Join(" | ", chat.Lines));
        yield return Until(() => !chat.Busy);
        Check("the_sample_reaches_claude", Asked().Contains(speech.Text), Asked());
    }

    // Letting go mid-sentence sends what was heard, and only that.
    IEnumerator Early(GameChat chat, GameVoice voice)
    {
        var speech = Play(voice, SampleDay);
        if (speech == null) yield break;
        yield return WaitReal(speech.LastWordAt / 2f);
        string heard = chat.Typed;
        voice.Release();
        yield return Until(() => !voice.Talking);
        Check("letting_go_early_sends_whats_heard", heard.Length > 0 && heard != speech.Text && chat.Lines.Any(l => l.StartsWith("You: ", System.StringComparison.Ordinal) && speech.Text.StartsWith(l.Substring(5), System.StringComparison.Ordinal) && l.Length > 5), heard + " | " + string.Join(" | ", chat.Lines));
        yield return Until(() => !chat.Busy);
    }

    // With Claude busy, spoken and typed words wait in order, on one line, up to the cap.
    IEnumerator Queue(GameChat chat, GameVoice voice)
    {
        const string Typing = "first typed", Second = "second typed", Third = "third typed", TooMany = "one too many";
        File.Delete(logged);
        ClaudeChat.StandIn = StandInClaude("slow-claude", SlowClaudeSeconds);
        chat.Send(Typing);
        ClaudeChat.StandIn = StandInClaude("claude", 0);
        var speech = Play(voice, SampleNoisy);
        if (speech == null) yield break;
        yield return WaitReal(speech.Seconds + 0.3f);
        voice.Release();
        yield return Until(() => !voice.Talking);
        int linesWere = chat.Lines.Count();
        Check("spoken_words_wait_their_turn", chat.Queued != null && chat.Queued.Contains(speech.Text) && !chat.Lines.Contains("You: " + speech.Text), chat.Queued);
        chat.Send(Second);
        chat.Send(Third);
        chat.Send(TooMany);
        Check("typed_words_join_the_line_up_to_the_cap", chat.Queued != null && chat.Queued.Contains("(3)"), chat.Queued);
        Check("too_many_says_so_on_the_one_line", chat.Queued != null && chat.Queued.Contains("too many"), chat.Queued);
        Check("no_pile_up_of_lines", chat.Lines.Count() == linesWere, string.Join(" | ", chat.Lines));
        yield return Until(() => !chat.Busy, MostQueueWait);
        string asked = Asked();
        int[] order = { asked.IndexOf(Typing, System.StringComparison.Ordinal), asked.IndexOf(speech.Text, System.StringComparison.Ordinal), asked.IndexOf(Second, System.StringComparison.Ordinal), asked.IndexOf(Third, System.StringComparison.Ordinal) };
        Check("they_go_in_order", order[0] >= 0 && order[0] < order[1] && order[1] < order[2] && order[2] < order[3], asked);
        Check("one_too_many_isnt_sent", !asked.Contains(TooMany), asked);
        Check("the_line_empties", chat.Queued == null, chat.Queued);
    }

    static IEnumerator WaitReal(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
    }
}
