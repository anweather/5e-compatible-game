using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Chem;
using UnityEngine;

// The probe's castclip scenario: one take from the side, as the game is now, for
// Andrew's verdict: a single, a pair and a charged cast, none of fire, so nothing
// burns into the frame or the sound. Run it before and after a tuning to get the
// two takes. Needs --shots <folder>: the frames and sound.wav (ClipFilm), and
// timeline.txt, each beat and blow with its time in the take.
public class CastClipProbe : ProbeScenario
{
    CastBeats beats;
    ClipFilm film;
    readonly List<string> timeline = new();

    protected override IEnumerator Run()
    {
        beats = wizard.GetComponent<CastBeats>();
        if (string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "castclip films into --shots <folder>");
            yield break;
        }
        bool casterWasOn = caster.enabled;
        caster.enabled = true;
        Place(8, 12);
        var aim = Yard(14, 12);
        aim.y = grid.GroundAt(aim);
        caster.AimOverride = aim;
        wizard.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(aim - wizard.transform.position, Vector3.up));
        var t = wizard.transform;
        Cut(t.position - t.right * 5.6f + t.forward * 1.4f + Vector3.up * 2.1f, t.position + t.forward * 0.3f + Vector3.up * 1f);
        yield return Wait(0.5f);

        film = new ClipFilm(this, ScreenshotDir);
        beats.BeatChanged += Began;
        beats.Struck += Struck;
        beats.ChargeLevel += Leveled;
        Check("sound_renders_offline", film.Roll(), "AudioRenderer.Start");
        Metric("sound_rate", AudioSettings.outputSampleRate);
        yield return Wait(0.4f);
        yield return Casts();
        yield return film.Stop();
        beats.BeatChanged -= Began;
        beats.Struck -= Struck;
        beats.ChargeLevel -= Leveled;

        File.WriteAllLines(Path.Combine(ScreenshotDir, "timeline.txt"), timeline);
        Metric("seconds", film.Seconds);
        Metric("sound_peak", film.Peak);
        Metric("sound_seconds", film.SoundSeconds);
        Check("frames_saved", film.Saved > 100, $"{film.Saved} of {film.Frames} frames");
        Check("sound_heard", film.Peak > 0.01f, $"peak {film.Peak:0.000}");
        caster.AimOverride = null;
        caster.enabled = casterWasOn;
    }

    IEnumerator Casts()
    {
        // By the hands (D16): air alone, water and air merged, air held to its full charge.
        Note("load single");
        yield return caster.CastByHands(SpellIds.GustOfWind);
        yield return Neutral(1f);
        Note("load pair");
        yield return caster.CastByHands(SpellIds.Frost);
        yield return Neutral(1f);
        Note("load charged");
        yield return caster.CastByHands(SpellIds.GustOfWind, World.MaxCharge);
        Note("letgo");
        yield return Neutral(1f);
    }

    IEnumerator Neutral(float after)
    {
        yield return Wait(0.3f);
        for (float t = 0f; beats.Now != Beat.Neutral && t < 8f; t += Time.deltaTime) yield return null;
        yield return Wait(after);
    }

    void Note(string what) => timeline.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.000} {1}", film.Seconds, what));
    void Began(Beat beat) => Note(beat.ToString());
    void Struck(Beat beat) => Note("struck " + beat);
    void Leveled(int level) => Note("level " + level);
}
