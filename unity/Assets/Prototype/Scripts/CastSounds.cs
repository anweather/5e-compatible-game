using UnityEngine;

// A wizard's casting sounds (docs/SPELL_COMBOS.md, "Sound"; J-040, J-044): a low hum
// for each element in the hands, the second layered over the first, rising as they
// combine at the slam and at each charge step; the slam's thump; a short whoosh at the
// release that cuts the hums off; a cancel's fizzle. CastBeats makes one for every
// copy of every wizard and everything is played from its hooks, so other players' casts
// sound the same, from where they stand. Which clip, how loud and how high is
// CastSoundTable's; the voices are SoundVoices' pool. A wizard has one voice per job,
// and a new sound in a job cuts off the last, so fast casts don't pile up into mush.
// The thump goes at Struck, the frame the hands meet, ahead of the hit-stop that starts
// there. A hum is a loop per stage, held each frame while the hands are in a cast, the
// wizard is itself and no menu is open, so whatever ends that ends it; a new stage's
// loop takes over and glides up from the last one's pitch.
public sealed class CastSounds
{
    static readonly Vector3 AtChest = Vector3.up * 1.15f;

    readonly CastBeats beats;
    readonly SpellCaster caster;
    readonly Transform body;
    readonly Shapeshift shape;
    Voice thump, release;
    Held right, left;
    bool met;

    // A hum and the stage of the cast its loop is.
    struct Held
    {
        public Voice voice;
        public int stage;
    }

    /// <summary>The first hand's hum, for the probe.</summary>
    public Voice Hum => right.voice;

    public CastSounds(CastBeats beats, SpellCaster caster)
    {
        this.beats = beats;
        this.caster = caster;
        body = beats.transform;
        beats.TryGetComponent(out shape);
        beats.BeatChanged += Began;
        beats.Struck += Struck;
    }

    void Began(Beat beat)
    {
        if (!beats.SoundsOn) return;
        if (beat == Beat.Release)
        {
            Quiet(CastSoundTable.HumCutOff);
            met = false;
            var kind = CastSoundTable.KindOf(CastImpact.SpellOf(beats), caster != null && caster.FallsFromSky(beats.Spell));
            Say(ref release, CastSoundTable.ReleaseOf(kind), CastSoundTable.ReleaseLoudnessAt(beats.Charge), CastSoundTable.ReleasePitchAt(beats.Charge));
        }
        else if (beat == Beat.Fizzle)
        {
            Hush();
            Say(ref thump, CastSoundTable.Fizzle);
        }
    }

    void Struck(Beat beat)
    {
        if (!beats.SoundsOn || beat != Beat.Slam) return;
        met = true;
        Say(ref thump, CastSoundTable.Slam);
    }

    /// <summary>Each frame: the hums while the hands are in a cast. If this stops being called, they let go by themselves.</summary>
    public void Tick()
    {
        bool humming = beats.SoundsOn && beats.Now is Beat.First or Beat.Second or Beat.WindUp or Beat.Slam or Beat.Charge
            && !(shape != null && shape.Shifted) && !(PauseMenu.Instance != null && PauseMenu.Instance.Open);
        if (!humming)
        {
            Quiet(CastSoundTable.HumOut);
            return;
        }
        var (first, second) = beats.Elements;
        // Never back down: the charge reads 1 for a moment between letting go and the core firing.
        int stage = met ? Mathf.Max(beats.Charge, right.stage) : 0;
        Keep(ref right, first, stage, 0f);
        if (second != null) Keep(ref left, second, stage, CastSoundTable.SecondHumAt);
    }

    void Keep(ref Held hum, string element, int stage, float startAt)
    {
        var row = CastSoundTable.Of(CastSoundTable.HumOf(element, stage));
        float volume = row.Volume * CastSoundTable.HumLoudnessAt(stage);
        bool playing = SoundVoices.Playing(hum.voice);
        if (hum.stage == stage && playing && SoundVoices.Hold(hum.voice, volume, 1f, CastSoundTable.HumGlide)) return;
        float from = playing ? CastSoundTable.HumPitchAt(hum.stage) / CastSoundTable.HumPitchAt(stage) : 1f;
        SoundVoices.Stop(hum.voice, CastSoundTable.CutSeconds);
        hum.stage = stage;
        hum.voice = SoundVoices.Loop(new Cue(row.Sound, GameSounds.Clip(row.Sound), volume, from, row.Reach), AtChest, body,
            playing ? CastSoundTable.CutSeconds : CastSoundTable.HumIn, startAt);
    }

    /// <summary>Stops what's in the hands.</summary>
    public void Hush()
    {
        Quiet(CastSoundTable.HumOut);
        met = false;
    }

    void Quiet(float seconds)
    {
        SoundVoices.Stop(right.voice, seconds);
        SoundVoices.Stop(left.voice, seconds);
        right = left = default;
    }

    void Say(ref Voice voice, string sound, float loudness = 1f, float pitch = 1f)
    {
        SoundVoices.Stop(voice, CastSoundTable.CutSeconds);
        var row = CastSoundTable.Of(sound);
        float varied = pitch * (1f + Random.Range(-row.PitchJitter, row.PitchJitter));
        voice = SoundVoices.Play(new Cue(sound, GameSounds.Clip(sound), row.Volume * loudness, varied, row.Reach), AtChest, body);
    }
}
