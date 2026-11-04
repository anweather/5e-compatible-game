using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Vivox;
using Unity.Services.Vivox.AudioTaps;
using UnityEngine;
using Debug = UnityEngine.Debug;

// The spike for proximity voice (D8, docs/MULTIPLAYER.md section 4), run in small players, not the
// Editor. On a Mac with its screen locked (2026-11-03) Vivox's audio thread stalled opening the capture
// device, in a player too; requests behind it got no answer (positions, injection, leaving), and in a
// hidden Editor exiting Play then held the main thread in vx_uninitialize for 12 minutes. Why it stalls
// is not known. ForceCaptureSilence still opens the device; -vivox-no-capture (input "No Device") doesn't,
// and everything answers, but injected audio was not heard that way.
// Inert without one of its arguments:
//   -vivox-speaker <wav>   stands at the origin and injects the file; no microphone needed
//   -vivox-listener <out>  walks away from the origin, writing what a channel tap hears
//   -vivox-hold <out>      stands 2 m away for the whole run, writing the level each second
//   -vivox-mic <out>       the real microphone in the channel, with Whisper's capture opened beside it
//   -vivox-group           a plain group channel in place of the positional one
//   -vivox-no-capture      input "No Device", so the capture device is never opened
// Voice is a view-side service: nothing here reaches the core or lockstep.
public static class VivoxSpike
{
    const string Channel = "vivox-spike";
    const int Audible = 32, Conversational = 4;
    const float Fade = 1f, PositionEvery = 1f / 3f, Settle = 2f, Window = 8f;
    const int SpeakerSeconds = 200, HoldSeconds = 60, Phase = 6, QuietVolume = -50; // Vivox's lowest: heard by the tap, not the room
    static readonly int[] Distances = { 2, 4, 8, 16, 24, 31, 40 };

    static StreamWriter log;
    static int sent;

    sealed class Meter : MonoBehaviour
    {
        readonly object gate = new();
        double sum;
        long count;

        /// <summary>Samples behind the last Take: none means the tap never ran, as opposed to silence.</summary>
        public long LastCount { get; private set; }

        public double Take()
        {
            lock (gate)
            {
                double rms = count == 0 ? 0 : Math.Sqrt(sum / count);
                LastCount = count;
                sum = 0;
                count = 0;
                return rms;
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            lock (gate)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    sum += data[i] * data[i];
                    data[i] = 0f; // measured, not played
                }
                count += data.Length;
            }
        }
    }

    // Positional channels with different properties are different channels: every end uses this.
    static Channel3DProperties Properties => new(Audible, Conversational, Fade, AudioFadeModel.InverseByDistance);

    static void Place(Vector3 at)
    {
        if (!Environment.GetCommandLineArgs().Contains("-vivox-group")) VivoxService.Instance.Set3DPosition(at, at, Vector3.forward, Vector3.up, Channel, false);
    }

    // Sends the position three times a second, by the clock rather than by counting loops.
    // Vivox lists only people within audible distance (seen: 2 people at 31 m, 1 at 40 m).
    static async Task Keep(Vector3 at, float seconds, Func<bool> until = null)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds && !(until?.Invoke() ?? false))
        {
            Place(at);
            sent++;
            await Task.Delay(TimeSpan.FromSeconds(PositionEvery));
        }
    }

    /// <summary>Vivox's inverse model: full inside the conversational distance, silent past the audible one.</summary>
    static double ExpectedGain(int distance) =>
        distance > Audible ? 0
        : distance <= Conversational ? 1
        : (double)Conversational / (Conversational + Fade * (distance - Conversational));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Start()
    {
        Application.runInBackground = true;
        var args = Environment.GetCommandLineArgs().ToList();
        string Arg(string name)
        {
            int at = args.IndexOf(name);
            return at >= 0 && at + 1 < args.Count ? args[at + 1] : null;
        }
        if (Arg("-vivox-speaker") is { } wav) _ = Guarded(null, () => Speak(wav));
        else if (Arg("-vivox-listener") is { } walk) _ = Guarded(walk, Walk);
        else if (Arg("-vivox-hold") is { } hold) _ = Guarded(hold, Hold);
        else if (Arg("-vivox-mic") is { } mic) _ = Guarded(mic, Mic);
    }

    static void Say(string line)
    {
        line = $"{DateTime.Now:HH:mm:ss.f} {line}";
        Debug.Log("VIVOX: " + line);
        log?.WriteLine(line);
        log?.Flush();
    }

    static async Task Guarded(string path, Func<Task> work)
    {
        if (path != null) log = new StreamWriter(path);
        try
        {
            await work();
            await Timed("leave", VivoxService.Instance.LeaveAllChannelsAsync());
            await Timed("logout", VivoxService.Instance.LogoutAsync());
        }
        catch (Exception e)
        {
            Say("FAILED: " + e);
        }
        var clock = Stopwatch.StartNew();
        VivoxService.Instance.Uninitialize();
        Say($"uninitialize {clock.ElapsedMilliseconds} ms");
        log?.Dispose();
        Application.Quit();
    }

    static async Task Timed(string what, Task task)
    {
        var clock = Stopwatch.StartNew();
        bool done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30))) == task;
        Say($"{what} {(done ? (task.IsFaulted ? "FAULTED " + task.Exception?.GetBaseException().Message : "ok") : "TIMED OUT")} {clock.ElapsedMilliseconds} ms");
    }

    static async Task Join(string name, bool muted)
    {
        await Timed("sign_in", NetSession.SignIn());
        await Timed("init", VivoxService.Instance.InitializeAsync(new VivoxConfigurationOptions { LogLevel = VivoxLogLevel.Debug }));
        await Timed("login", VivoxService.Instance.LoginAsync(new LoginOptions { DisplayName = name }));
        if (muted) VivoxService.Instance.MuteInputDevice();
        if (Environment.GetCommandLineArgs().Contains("-vivox-no-capture"))
        {
            var none = VivoxService.Instance.AvailableInputDevices.First(d => d.DeviceName == "No Device");
            await Timed("no_input_device", VivoxService.Instance.SetActiveInputDeviceAsync(none));
        }
        VivoxService.Instance.ParticipantAddedToChannel += p => Say($"added {p.DisplayName} self={p.IsSelf} in {p.ChannelName}");
        bool group = Environment.GetCommandLineArgs().Contains("-vivox-group");
        await Timed("join", group
            ? VivoxService.Instance.JoinGroupChannelAsync(Channel, ChatCapability.AudioOnly)
            : VivoxService.Instance.JoinPositionalChannelAsync(Channel, ChatCapability.AudioOnly, Properties));
        var clock = Stopwatch.StartNew();
        while (Self() == null && clock.Elapsed.TotalSeconds < 30) await Task.Delay(50);
        Say($"in_audio {Self() != null} {clock.ElapsedMilliseconds} ms; input devices {VivoxService.Instance.AvailableInputDevices.Count}; uri {Self()?.ChannelURI}; player {VivoxService.Instance.SignedInPlayerId}; transmitting {string.Join(",", VivoxService.Instance.TransmittingChannels)}");
    }

    static IEnumerable<VivoxParticipant> People() =>
        VivoxService.Instance.ActiveChannels.TryGetValue(Channel, out var people) ? people : Enumerable.Empty<VivoxParticipant>();

    static VivoxParticipant Self() => People().FirstOrDefault(p => p.IsSelf && p.IsInAudio);

    static async Task Speak(string wav)
    {
        await Join("speaker", false);
        VivoxService.Instance.SetOutputDeviceVolume(QuietVolume);
        VivoxService.Instance.StartAudioInjection(wav);
        await Keep(Vector3.zero, 3f); // the flag is set when the request's answer arrives
        Say($"injecting {VivoxService.Instance.IsInjectingAudio}");
        await Keep(Vector3.zero, SpeakerSeconds);
        VivoxService.Instance.StopAudioInjection();
    }

    static async Task<Meter> Tap(Vector3 at)
    {
        await Keep(at, 60f, () => People().Any(p => !p.IsSelf));
        Say($"other_side_present {People().Any(p => !p.IsSelf)}");
        var holder = new GameObject("VivoxSpikeTap");
        UnityEngine.Object.DontDestroyOnLoad(holder);
        holder.SetActive(false);
        holder.AddComponent<AudioListener>();
        holder.AddComponent<AudioSource>();
        holder.AddComponent<VivoxChannelAudioTap>().ChannelName = Channel;
        var meter = holder.AddComponent<Meter>();
        holder.SetActive(true);
        return meter;
    }

    static async Task Walk()
    {
        await Join("listener", true);
        VivoxService.Instance.SetOutputDeviceVolume(QuietVolume);
        var meter = await Tap(new Vector3(0f, 0f, -Distances[0]));
        double near = 0;
        foreach (int distance in Distances)
        {
            var at = new Vector3(0f, 0f, -distance);
            await Keep(at, Settle);
            meter.Take();
            sent = 0;
            await Keep(at, Window);
            double rms = meter.Take();
            if (near == 0) near = rms;
            Say($"distance {distance} rms {rms:0.000000} gain {(near > 0 ? rms / near : 0):0.000} expected {ExpectedGain(distance):0.000} positions/s {sent / Window:0.0} people {People().Count()} samples {meter.LastCount} energy {People().FirstOrDefault(p => !p.IsSelf)?.AudioEnergy:0.000} speaking {People().FirstOrDefault(p => !p.IsSelf)?.SpeechDetected}");
        }
    }

    static async Task Hold()
    {
        await Join("holder", true);
        VivoxService.Instance.SetOutputDeviceVolume(QuietVolume);
        var at = new Vector3(0f, 0f, -2f);
        var meter = await Tap(at);
        for (int second = 0; second < HoldSeconds; second++)
        {
            await Keep(at, 1f);
            Say($"second {second} {DateTime.Now:HH:mm:ss} rms {meter.Take():0.000000}");
        }
    }

    // What the microphone gives each side in turn: Vivox alone, Whisper's capture opened beside it
    // with Vivox still sending, then the hand-over a hold would make (Vivox muted while capturing).
    static async Task Mic()
    {
        await Join("mic", false);
        await Keep(Vector3.zero, 3f);
        await Phased("vivox_alone", false);
        await Phased("both_open", true);
        await Phased("vivox_after_capture", false);
        VivoxService.Instance.MuteInputDevice();
        await Phased("handed_over_muted", true);
        VivoxService.Instance.UnmuteInputDevice();
        await Phased("handed_back", false);
    }

    static async Task Phased(string phase, bool capture)
    {
        var audio = capture ? new MicrophoneAudio() : null;
        var clock = Stopwatch.StartNew();
        string why = audio?.Open();
        long opened = clock.ElapsedMilliseconds;
        Say($"phase {phase} start {DateTime.Now:HH:mm:ss} muted {VivoxService.Instance.IsInputDeviceMuted} open {opened} ms {why}");
        double energy = 0;
        float level = 0f;
        bool speech = false;
        for (float t = 0f; t < Phase; t += 0.1f)
        {
            Place(Vector3.zero);
            await Task.Delay(100);
            audio?.Pump();
            level = Mathf.Max(level, audio?.Level ?? 0f);
            var self = Self();
            if (self == null) continue;
            energy = Math.Max(energy, self.AudioEnergy);
            speech |= self.SpeechDetected;
        }
        clock.Restart();
        int samples = audio?.Close().Length ?? 0;
        Say($"phase {phase} end {DateTime.Now:HH:mm:ss} energy {energy:0.000} speech {speech} unity_peak {level:0.000} samples {samples} close {clock.ElapsedMilliseconds} ms");
    }
}
