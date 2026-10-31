using UnityEngine;

// A wizard's casting sounds (docs/SPELL_COMBOS.md, "Sound"): each element gathering,
// the slam's thump, the charge's hum rising by level, the release by kind of cast
// and a cancel's fizzle. CastBeats makes one for every copy of every wizard and
// everything is played from its hooks, so other players' casts sound the same,
// from where they stand. Which clip, how loud and how high is CastSoundTable's; the
// voices are SoundVoices' pool. A wizard has one voice per job, and a new sound in
// a job cuts off the last, so fast casts don't pile up into mush. The thump goes at
// Struck, the frame the hands meet, ahead of the hit-stop that starts there. The
// hum is held each frame while the beat is Charge, the wizard is itself and no
// menu is open; whatever ends that ends the hum.
public sealed class CastSounds
{
    static readonly Vector3 AtChest = Vector3.up * 1.15f;

    readonly CastBeats beats;
    readonly SpellCaster caster;
    readonly Transform body;
    readonly Shapeshift shape;
    Voice right, left, thump, ping, hum, release;

    /// <summary>The hum, for the probe.</summary>
    public Voice Hum => hum;

    public CastSounds(CastBeats beats, SpellCaster caster)
    {
        this.beats = beats;
        this.caster = caster;
        body = beats.transform;
        beats.TryGetComponent(out shape);
        beats.BeatChanged += Began;
        beats.Struck += Struck;
        beats.ChargeLevel += Leveled;
    }

    void Began(Beat beat)
    {
        if (!beats.SoundsOn) return;
        switch (beat)
        {
            case Beat.First:
                Say(ref right, CastSoundTable.GatherOf(beats.Elements.first));
                break;
            case Beat.Second:
                Say(ref left, CastSoundTable.GatherOf(beats.Elements.second));
                break;
            case Beat.Fizzle:
                Hush();
                Say(ref thump, CastSoundTable.Fizzle);
                break;
        }
    }

    void Struck(Beat beat)
    {
        if (!beats.SoundsOn) return;
        if (beat == Beat.Slam)
        {
            Cut(ref right);
            Cut(ref left);
            Say(ref thump, CastSoundTable.Slam);
            return;
        }
        var kind = CastSoundTable.KindOf(CastImpact.SpellOf(beats), caster != null && caster.FallsFromSky(beats.Spell));
        Say(ref release, CastSoundTable.ReleaseOf(kind), CastSoundTable.ReleaseLoudnessAt(beats.Charge), CastSoundTable.ReleasePitchAt(beats.Charge));
    }

    void Leveled(int level)
    {
        if (beats.SoundsOn) Say(ref ping, CastSoundTable.ChargeLevel, 1f, CastSoundTable.LevelPitchAt(level));
    }

    /// <summary>Each frame: the hum while charging. If this stops being called, the hum lets go by itself.</summary>
    public void Tick()
    {
        bool humming = beats.SoundsOn && beats.Now == Beat.Charge && !(shape != null && shape.Shifted) && !(PauseMenu.Instance != null && PauseMenu.Instance.Open);
        if (!humming)
        {
            Cut(ref hum, CastSoundTable.HumOut);
            return;
        }
        var row = CastSoundTable.Of(CastSoundTable.ChargeLoop);
        float volume = row.Volume * CastSoundTable.HumLoudnessAt(beats.Charge), pitch = CastSoundTable.HumPitchAt(beats.Charge);
        if (!SoundVoices.Hold(hum, volume, pitch, CastSoundTable.HumGlide))
            hum = SoundVoices.Loop(new Cue(row.Sound, GameSounds.Clip(row.Sound), volume, pitch, row.Reach), AtChest, body, CastSoundTable.HumIn);
    }

    /// <summary>Stops what's in the hands: the hum and any gathering.</summary>
    public void Hush()
    {
        Cut(ref hum, CastSoundTable.HumOut);
        Cut(ref right);
        Cut(ref left);
    }

    void Say(ref Voice voice, string sound, float loudness = 1f, float pitch = 1f)
    {
        SoundVoices.Stop(voice, CastSoundTable.CutSeconds);
        var row = CastSoundTable.Of(sound);
        float varied = pitch * (1f + Random.Range(-row.PitchJitter, row.PitchJitter));
        voice = SoundVoices.Play(new Cue(sound, GameSounds.Clip(sound), row.Volume * loudness, varied, row.Reach), AtChest, body);
    }

    static void Cut(ref Voice voice, float seconds = CastSoundTable.CutSeconds)
    {
        SoundVoices.Stop(voice, seconds);
        voice = default;
    }
}
