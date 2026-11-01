using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chem;
using UnityEngine;

// The casting sounds in the castfeel probe (docs/SPELL_COMBOS.md, "Sound"): what
// plays, in which order, for a single, a pair, a charged and a cancelled cast;
// the thump at contact, ahead of the hit-stop; the hum and tone rising by level;
// nothing left playing after a cancel, a pause, a disabled wizard, a Shapeshift, a
// fall, a world reset or a quit; fast casts not piling up; volume and distance.
// A remote wizard is checked in the online probe, on a real other peer.
public partial class CastFeelProbe
{
    const int GarbageSounds = 200;
    readonly List<string> heard = new();
    readonly List<float> humStarts = new();
    int thumpFrame = -1, struckFrame = -1;
    bool stoppedAtThump;
    float thumpInBeat;

    void Sounded(string sound, Vector3 at, float volume, float pitch)
    {
        heard.Add(sound);
        if (sound.StartsWith("charge-", StringComparison.Ordinal)) humStarts.Add(pitch);
        if (sound != CastSoundTable.Slam) return;
        thumpFrame = Time.frameCount;
        stoppedAtThump = beats.Stopped;
        thumpInBeat = beats.InBeat;
    }

    void HandsMet(Beat beat)
    {
        if (beat == Beat.Slam) struckFrame = Time.frameCount;
    }

    IEnumerator Sounds()
    {
        SoundVoices.Started += Sounded;
        beats.Struck += HandsMet;
        try
        {
            GameVolume.Load();
            yield return Wait(1f);
            yield return Heard("single", new[] { ElementIds.Fire },
                new[] { CastSoundTable.GatherFire, CastSoundTable.Slam, CastSoundTable.HumOf(ElementIds.Fire, 1), CastSoundTable.ReleaseBolt });
            Check("thump_at_contact", thumpFrame == struckFrame && thumpFrame > 0 && !stoppedAtThump && Mathf.Abs(thumpInBeat - beats.feel.tapSlam.seconds) < 0.06f,
                $"frame {thumpFrame}, hands met {struckFrame}, in a stop {stoppedAtThump}, {thumpInBeat:0.000} s into the slam");
            yield return Heard("pair", new[] { ElementIds.Water, ElementIds.Fire },
                new[] { CastSoundTable.GatherWater, CastSoundTable.GatherFire, CastSoundTable.Slam, CastSoundTable.HumOf(ElementIds.Water, 1), CastSoundTable.HumOf(ElementIds.Fire, 1), CastSoundTable.ReleaseBurst });
            yield return ChargedSound();
            yield return CancelledSound();
            yield return NothingLeft();
            yield return Fast();
            yield return Levels();
            yield return UntilGone();
        }
        finally
        {
            SoundVoices.Started -= Sounded;
            beats.Struck -= HandsMet;
        }
    }

    IEnumerator UntilGone()
    {
        caster.DropCast();
        yield return AtBeat(Beat.Neutral, 0.4f);
    }

    IEnumerator Heard(string name, string[] elements, string[] expected)
    {
        heard.Clear();
        caster.LoadElement(elements[0]);
        for (int i = 1; i < elements.Length; i++)
        {
            yield return Wait(0.35f);
            caster.LoadElement(elements[i]);
        }
        yield return Wait(0.3f);
        for (float t = 0f; beats.Now != Beat.Neutral && t < WaitLimit; t += Time.deltaTime) yield return null;
        yield return Wait(0.3f);
        Check($"{name}_sounds_in_order", heard.SequenceEqual(expected), string.Join(" ", heard));
    }

    IEnumerator HoldToCharge(string element, int level)
    {
        caster.LoadElement(element, held: true);
        for (float t = 0f; (beats.Now != Beat.Charge || World.HeldCharge(World.CastOf(caster.CastPlayer)) < level) && t < WaitLimit; t += Time.deltaTime)
            yield return null;
    }

    static string Listed(IEnumerable<float> pitches) => string.Join(" ", pitches.Select(p => p.ToString("0.00", CultureInfo.InvariantCulture)));

    IEnumerator ChargedSound()
    {
        heard.Clear();
        humStarts.Clear();
        var volumes = new List<float>();
        yield return ClimbToTheTop(volumes);
        volumes.Add(SoundVoices.Now(beats.Sounds.Hum).volume);
        float top = SoundVoices.Now(beats.Sounds.Hum).pitch;
        int loops = SoundVoices.Sounding.loops;
        Check("charge_hum_plays", loops == 1 && SoundVoices.Playing(beats.Sounds.Hum), $"{loops} loops");
        Metric("hum_pitch_top", top);
        var levels = Enumerable.Range(1, World.MaxCharge).Select(level => CastSoundTable.HumOf(ElementIds.Earth, level));
        Check("hum_rises_by_level", heard.Where(s => s.StartsWith("charge-", StringComparison.Ordinal)).SequenceEqual(levels) && humStarts.All(p => p < 1f) && Mathf.Abs(top - 1f) < 0.01f,
            $"[{string.Join(" ", heard)}] from [{Listed(humStarts)}], settled at {top:0.00}");
        Check("hum_louder_by_level", volumes.Count == World.MaxCharge && volumes.Zip(volumes.Skip(1), (a, b) => a < b).All(up => up), Listed(volumes));
        caster.LetGo();
        float letGo = Time.time;
        for (float t = 0f; beats.Now != Beat.Release && t < WaitLimit; t += Time.deltaTime) yield return null;
        Metric("hum_after_letting_go_s", Time.time - letGo);
        yield return Wait(0.2f);
        Check("hum_ends_at_release", SoundVoices.Sounding.loops == 0, $"{SoundVoices.Sounding.loops} loops");
        for (float t = 0f; beats.Now != Beat.Neutral && t < WaitLimit; t += Time.deltaTime) yield return null;
        yield return Wait(0.2f);
        CheckChargedOrder();
    }

    // Holds an element to the top level and a little past it, noting the hum's volume just before each step up.
    IEnumerator ClimbToTheTop(List<float> volumes)
    {
        caster.LoadElement(ElementIds.Earth, held: true);
        int lastLevel = 0;
        float atLevelStart = 0f, volume = 0f;
        for (float t = 0f; t < WaitLimit; t += Time.deltaTime)
        {
            var cast = World.CastOf(caster.CastPlayer);
            int level = cast != null ? World.HeldCharge(cast) : 0;
            if (level != lastLevel)
            {
                lastLevel = level;
                atLevelStart = t;
                if (level > 1) volumes.Add(volume);
            }
            if (level == World.MaxCharge && t - atLevelStart > 0.45f) yield break;
            volume = SoundVoices.Now(beats.Sounds.Hum).volume;
            yield return null;
        }
    }

    void CheckChargedOrder()
    {
        var order = heard.Where(s => !s.StartsWith("gather", StringComparison.Ordinal) || s == CastSoundTable.GatherEarth).ToList();
        int slam = order.IndexOf(CastSoundTable.Slam), hum = order.IndexOf(CastSoundTable.HumOf(ElementIds.Earth, 1)), top = order.IndexOf(CastSoundTable.HumOf(ElementIds.Earth, World.MaxCharge)),
            release = order.FindIndex(s => s.StartsWith("release-", StringComparison.Ordinal));
        Check("charged_sounds_in_order", order[0] == CastSoundTable.GatherEarth && slam > 0 && hum > slam && top > hum && release > top && heard.Count(s => s == CastSoundTable.Slam) == 1 && heard.Count(s => s.StartsWith("charge-", StringComparison.Ordinal)) == World.MaxCharge,
            string.Join(" ", heard));
    }

    IEnumerator CancelledSound()
    {
        yield return HoldToCharge(ElementIds.Fire, 2);
        yield return Wait(0.2f); // the last level's loop fades as this one takes over
        Check("cancel_starts_humming", SoundVoices.Sounding.loops == 1, $"{SoundVoices.Sounding.loops} loops");
        heard.Clear();
        caster.DropCast();
        yield return Wait(0.3f);
        Check("cancel_fizzles", heard.SequenceEqual(new[] { CastSoundTable.Fizzle }), string.Join(" ", heard));
        Check("cancel_leaves_nothing", SoundVoices.Sounding.loops == 0, $"{SoundVoices.Sounding.loops} loops");
        for (float t = 0f; beats.Now != Beat.Neutral && t < WaitLimit; t += Time.deltaTime) yield return null;
        yield return Wait(0.6f);
        Check("all_voices_free_after_a_cancel", SoundVoices.Sounding.all == 0, $"{SoundVoices.Sounding.all} voices");
    }

    // The ways a hum could be left behind.
    IEnumerator NothingLeft()
    {
        // A pause: quiet while the menu is up, back if the cast is still held after it.
        yield return HoldToCharge(ElementIds.Fire, 1);
        PauseMenu.Instance.SetOpen(true);
        yield return Wait(0.2f);
        Check("pause_quiets_the_hum", SoundVoices.Sounding.loops == 0, $"{SoundVoices.Sounding.loops} loops");
        PauseMenu.Instance.SetOpen(false);
        yield return Wait(0.2f);
        // Two for a moment when a charge step lands here: the old loop fading as the new one takes over.
        Check("hum_back_after_a_pause", SoundVoices.Sounding.loops is 1 or 2 || beats.Now != Beat.Charge, $"{SoundVoices.Sounding.loops} loops, beat {beats.Now}");
        yield return UntilGone();

        // CastBeats off, and a loop with nobody holding it.
        yield return HoldToCharge(ElementIds.Fire, 1);
        beats.enabled = false;
        yield return Wait(0.2f);
        Check("disabled_wizard_stops_the_hum", SoundVoices.Sounding.loops == 0, $"{SoundVoices.Sounding.loops} loops");
        beats.enabled = true;
        yield return UntilGone();
        var loose = SoundVoices.Loop(new Cue("probe", GameSounds.Clip(CastSoundTable.HumOf(ElementIds.Fire, 1)), 0.2f, 1f, 40f), Vector3.zero, wizard.transform, 0f);
        Check("a_loop_starts", SoundVoices.Playing(loose));
        yield return Wait(0.6f);
        Check("a_loop_not_held_lets_go", SoundVoices.Sounding.loops == 0, $"{SoundVoices.Sounding.loops} loops");

        // Shapeshift.
        yield return HoldToCharge(ElementIds.Fire, 1);
        if (wizard.TryGetComponent<Shapeshift>(out var shape))
        {
            shape.Show(Shapeshift.Pack(0, 0));
            yield return Wait(0.2f);
            Check("shapeshift_stops_the_hum", SoundVoices.Sounding.loops == 0 || !shape.Shifted, $"{SoundVoices.Sounding.loops} loops, shifted {shape.Shifted}");
            shape.Show(Shapeshift.None);
        }
        yield return UntilGone();

        // A fall, the real way.
        yield return HoldToCharge(ElementIds.Fire, 1);
        if (wizard.TryGetComponent<Vitals>(out var vitals))
        {
            vitals.TakeDamage(vitals.HealthMax * 2f);
            yield return Wait(0.3f);
            Check("downed_stops_the_hum", vitals.IsDown && SoundVoices.Sounding.loops == 0, $"down {vitals.IsDown}, {SoundVoices.Sounding.loops} loops");
            for (float t = 0f; vitals.IsDown && t < 30f; t += Time.deltaTime) yield return null;
            yield return Wait(0.5f);
        }
        yield return UntilGone();

        // Quit: the pool's last act.
        yield return HoldToCharge(ElementIds.Fire, 1);
        SoundVoices.StopAll();
        Check("quit_stops_everything", SoundVoices.Sounding.all == 0, $"{SoundVoices.Sounding.all} voices");
        yield return UntilGone();

        // A world reset last of these: the cast goes with the world.
        yield return HoldToCharge(ElementIds.Fire, 1);
        grid.ResetWorld();
        yield return Wait(0.4f);
        Check("world_reset_stops_the_hum", SoundVoices.Sounding.loops == 0, $"{SoundVoices.Sounding.loops} loops, beat {beats.Now}");
        yield return UntilGone();
    }

    // Casts as fast as the buttons go: one voice per job, so they can't pile up.
    IEnumerator Fast()
    {
        int most = 0;
        yield return Wait(0.5f);
        for (int i = 0; i < 12; i++)
        {
            caster.LoadElement(i % 2 == 0 ? ElementIds.Fire : ElementIds.Air);
            for (int f = 0; f < 4; f++)
            {
                most = Mathf.Max(most, SoundVoices.Sounding.all);
                yield return null;
            }
            caster.DropCast();
            for (int f = 0; f < 2; f++)
            {
                most = Mathf.Max(most, SoundVoices.Sounding.all);
                yield return null;
            }
        }
        Metric("fast_cast_voices_most", most);
        Check("fast_casts_do_not_pile_up", most <= 6, $"{most} voices at most");
        yield return UntilGone();
    }

    // Volume, distance, and no garbage.
    IEnumerator Levels()
    {
        yield return Wait(0.6f);
        float setting = GameVolume.Effects;
        var clip = GameSounds.Clip(CastSoundTable.HumOf(ElementIds.Fire, 1));
        var voice = SoundVoices.Loop(new Cue("probe", clip, 0.5f, 1f, CastSoundTable.MediumReach), Vector3.up, wizard.transform, 0f);
        yield return null;
        SoundVoices.Hold(voice, 0.5f, 1f, 0.1f);
        float before = SoundVoices.Now(voice).volume;
        GameVolume.Effects = setting * 0.5f;
        yield return null;
        SoundVoices.Hold(voice, 0.5f, 1f, 0.1f);
        yield return null;
        float halved = SoundVoices.Now(voice).volume;
        GameVolume.Effects = setting;
        SoundVoices.Stop(voice);
        Check("effects_setting_scales_a_voice", Mathf.Abs(halved - before * 0.5f) < 0.03f, $"{before:0.00} -> {halved:0.00}");
        Check("sound_falls_with_distance", SoundVoices.GainAt(2f, 40f) > 0.99f && SoundVoices.GainAt(20f, 40f) < 0.4f && SoundVoices.GainAt(39.9f, 40f) < 0.02f,
            $"{SoundVoices.GainAt(2f, 40f):0.00}, {SoundVoices.GainAt(20f, 40f):0.00}, {SoundVoices.GainAt(39.9f, 40f):0.00}");
        var beyond = SoundVoices.Play(new Cue("probe", clip, 1f, 1f, 40f), Camera.main.transform.position + Vector3.right * 60f);
        Check("out_of_reach_takes_no_voice", beyond.Serial == 0);

        // Through the same path as a cast; the probe's own listener is off for this.
        SoundVoices.Started -= Sounded;
        float made = -1f;
        yield return GarbagePerCall(() =>
        {
            var hit = SoundVoices.Play(new Cue(CastSoundTable.Slam, GameSounds.Clip(CastSoundTable.Slam), 0.01f, 1f, 40f), Vector3.up, wizard.transform);
            SoundVoices.Stop(hit, 0.01f);
        }, GarbageSounds, m => made = m);
        SoundVoices.Started += Sounded;
        Metric("bytes_a_sound", made);
        Check("no_garbage_per_sound", made >= 0f && made < MostGarbage, $"{made:0.00} bytes a sound, over {GarbageSounds} a frame");
        yield return Wait(0.3f);
    }
}
