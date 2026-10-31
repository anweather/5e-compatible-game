using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// The controller's motors. What wants a rumble plays a short pulse (a thump that
// dies away); the strongest pulse of each motor wins, and they never build into
// a drone. Each pulse ends by itself, and all of it stops when the Esc menu opens,
// the game loses focus or pauses, on a cancel, on quit, and before a domain reload:
// a motor left running is a nasty bug, so the stops are many and the pulses short.
// Only the pad in use rumbles, not one lying idle by the keyboard. Starts itself.
public class PadRumble : MonoBehaviour
{
    struct Pulse
    {
        public float low, high, seconds, left;
    }

    static PadRumble instance;

    /// <summary>The probe's way past "the pad in use" and the focus check: the Editor it runs in isn't in front.</summary>
    public static bool Testing { get; set; }

    readonly List<Pulse> pulses = new();
    Gamepad driven;
    float low, high;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        Testing = false;
        if (instance != null) return;
        var go = new GameObject("Pad rumble");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PadRumble>();
    }

    /// <summary>A pulse, at this share of its strength; nothing if the setting is off or no pad is in use.</summary>
    public static void Play(in CastFeel.Thump thump, float share)
    {
        float scale = share * GameFeel.Shake;
        if (instance == null || scale <= 0f || thump.seconds <= 0f || !Allowed) return;
        instance.pulses.Add(new Pulse { low = thump.low * scale, high = thump.high * scale, seconds = thump.seconds, left = thump.seconds });
    }

    /// <summary>Stops the motors now, and every pulse playing.</summary>
    public static void Stop()
    {
        if (instance != null) instance.Halt();
    }

    static bool Allowed
    {
        get
        {
            if (PauseMenu.Instance != null && PauseMenu.Instance.Open) return false;
            if (Testing) return true;
            var controls = EventLog.TrackedCaster != null ? EventLog.TrackedCaster.controls : null;
            return Application.isFocused && controls != null && controls.UsingGamepad;
        }
    }

    void Update()
    {
        if (!Allowed || GameFeel.Shake <= 0f)
        {
            if (pulses.Count > 0 || low > 0f || high > 0f) Halt();
            return;
        }
        float wantLow = 0f, wantHigh = 0f;
        for (int i = pulses.Count - 1; i >= 0; i--)
        {
            var pulse = pulses[i];
            pulse.left -= Time.unscaledDeltaTime;
            if (pulse.left <= 0f)
            {
                pulses.RemoveAt(i);
                continue;
            }
            pulses[i] = pulse;
            float share = pulse.left / pulse.seconds;
            wantLow = Mathf.Max(wantLow, pulse.low * share);
            wantHigh = Mathf.Max(wantHigh, pulse.high * share);
        }
        Send(Mathf.Clamp01(wantLow), Mathf.Clamp01(wantHigh));
    }

    void Send(float wantLow, float wantHigh)
    {
        var pad = Gamepad.current;
        if (driven != null && driven != pad) driven.SetMotorSpeeds(0f, 0f);
        driven = pad;
        if (pad == null || (Mathf.Approximately(wantLow, low) && Mathf.Approximately(wantHigh, high))) return;
        low = wantLow;
        high = wantHigh;
        pad.SetMotorSpeeds(low, high);
    }

    void Halt()
    {
        pulses.Clear();
        low = high = 0f;
        if (driven != null) driven.SetMotorSpeeds(0f, 0f);
        Gamepad.current?.SetMotorSpeeds(0f, 0f);
        driven = null;
    }

    void OnApplicationFocus(bool focused)
    {
        if (!focused && !Testing) Halt();
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) Halt();
    }

    void OnApplicationQuit() => Halt();

#if UNITY_EDITOR
    void OnEnable()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Halt;
        UnityEditor.EditorApplication.pauseStateChanged += EditorPaused;
    }

    void EditorPaused(UnityEditor.PauseState state) => Halt();

    void OnDisable()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Halt;
        UnityEditor.EditorApplication.pauseStateChanged -= EditorPaused;
        Halt();
    }
#else
    void OnDisable() => Halt();
#endif

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
