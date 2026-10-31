using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's castfeel scenario (docs/SPELL_COMBOS.md, "Beats"): casts by
// elements through the wizard's own LoadElement and LetGo, single, pair,
// charged and cancelled. Each must go through its beats in order with the hands
// at their targets once a beat has settled; the slam takes about 0.1 s; a charge
// pulses at each level and slows the walk; the recoil moves only what's drawn.
// With --shots, the wizard from the side at each beat, first with the pose off
// (the "before": the old gestures, the same camera and moments). The impact is
// in ProbeCastFeel.Impact.cs.
public partial class CastFeelProbe : ProbeScenario
{
    const float HandTolerance = 0.06f, WaitLimit = 8f;

    CastBeats beats;
    readonly List<Beat> seen = new();
    readonly Dictionary<Beat, float> worstHand = new();
    int pulses;
    float slamSeconds;

    protected override IEnumerator Run()
    {
        beats = wizard.GetComponent<CastBeats>();
        Check("wizard_has_beats", beats != null && beats.feel != null && beats.feel.name == "CastFeel", beats == null || beats.feel == null ? "no CastBeats or CastFeel" : $"feel {beats.feel.name}");
        if (beats == null) yield break;
        beats.BeatChanged += Heard;
        beats.ChargeLevel += Pulsed;
        bool casterWasOn = caster.enabled;
        caster.enabled = true;
        try
        {
            Place(8, 12);
            var aim = Yard(14, 12);
            aim.y = grid.GroundAt(aim);
            caster.AimOverride = aim;
            wizard.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(aim - wizard.transform.position, Vector3.up));
            var t = wizard.transform;
            Cut(t.position - t.right * 4.2f + t.forward * 1.4f + Vector3.up * 2.3f, t.position + Vector3.up * 0.85f);
            yield return Wait(0.5f);

            if (!string.IsNullOrEmpty(ScreenshotDir))
            {
                beats.PoseOn = false;
                yield return Pictures("before");
                beats.PoseOn = true;
                yield return Wait(1f);
            }

            yield return Cast("single", new[] { ElementIds.Fire }, false, new[] { Beat.First, Beat.Slam, Beat.Release, Beat.Recovery, Beat.Neutral });
            yield return Cast("pair", new[] { ElementIds.Water, ElementIds.Earth }, false, new[] { Beat.First, Beat.Second, Beat.Slam, Beat.Release, Beat.Recovery, Beat.Neutral });
            yield return Charged();
            yield return Cancelled();
            yield return MidTick();
            if (!string.IsNullOrEmpty(ScreenshotDir)) yield return Pictures("after");
            yield return Impact();
        }
        finally
        {
            caster.AimOverride = null;
            caster.enabled = casterWasOn;
            beats.PoseOn = true;
            beats.BeatChanged -= Heard;
            beats.ChargeLevel -= Pulsed;
        }
    }

    // A peer catching up on ticks in slices can be mid-tick at the end of every frame: the beats
    // must still play, all of them. (World.StepSome here leaves a tick begun for the grid to finish.)
    IEnumerator MidTick()
    {
        bool slicing = true;
        IEnumerator Slices()
        {
            while (slicing)
            {
                if (!World.TickInProgress) World.StepSome(() => true);
                yield return null;
            }
        }
        StartCoroutine(Slices());
        yield return Cast("mid_tick", new[] { ElementIds.Fire }, false, new[] { Beat.First, Beat.Slam, Beat.Release, Beat.Recovery, Beat.Neutral });
        slicing = false;
        yield return null;
    }

    void Heard(Beat beat)
    {
        seen.Add(beat);
        if (beat == Beat.Slam) StartCoroutine(TimeSlam());
    }

    void Pulsed(int level) => pulses++;

    // From the slam's start until the hands meet.
    IEnumerator TimeSlam()
    {
        float at = Time.time;
        var pose = wizard.GetComponent<CastPose>();
        while (Time.time - at < 0.5f && Vector3.Distance(pose.HandPosition, pose.LeftHandPosition) > 0.14f) yield return null;
        slamSeconds = Time.time - at;
    }

    // Beginning a cast and following it until the beats are back at neutral.
    IEnumerator Cast(string name, string[] elements, bool held, Beat[] expected)
    {
        seen.Clear();
        worstHand.Clear();
        Check($"{name}_loads", caster.LoadElement(elements[0], held));
        for (int i = 1; i < elements.Length; i++)
        {
            yield return Wait(0.35f);
            Check($"{name}_second_loads", caster.LoadElement(elements[i], held));
        }
        yield return Follow(expected, name);
    }

    IEnumerator Follow(Beat[] expected, string name)
    {
        yield return Settle();
        Report(name, expected);
    }

    // Runs until neutral, keeping the worst hand error of each settled beat.
    IEnumerator Settle()
    {
        float waited = 0f;
        do
        {
            Measure();
            waited += Time.deltaTime;
            yield return null;
        }
        while (beats.Now != Beat.Neutral && waited < WaitLimit);
    }

    void Report(string name, Beat[] expected)
    {
        Check($"{name}_beats_in_order", seen.SequenceEqual(expected), string.Join(" ", seen));
        foreach (var (beat, error) in worstHand.Select(kv => (kv.Key, kv.Value)))
        {
            Metric($"{name}_{beat.ToString().ToLowerInvariant()}_hand_cm", error * 100f);
            Check($"{name}_{beat.ToString().ToLowerInvariant()}_hands_at_targets", error < HandTolerance, $"worst {error * 100f:0.0} cm");
        }
    }

    void Measure()
    {
        var beat = beats.Now;
        if (beat is Beat.Neutral or Beat.Recovery or Beat.Fizzle || beats.InBeat < beats.feel.Of(beat).seconds + 0.01f) return;
        var pose = wizard.GetComponent<CastPose>();
        var (right, left) = beats.HandTargets;
        var feel = beats.feel.Of(beat);
        float error = feel.rightWeight > 0.99f ? Vector3.Distance(right, pose.HandPosition) : 0f;
        if (feel.leftWeight > 0.99f) error = Mathf.Max(error, Vector3.Distance(left, pose.LeftHandPosition));
        worstHand[beat] = Mathf.Max(error, worstHand.GetValueOrDefault(beat));
    }

    // Held on: the slam, the pressed hands, a pulse at each level, the slower walk; let go, the thrust and the recoil.
    IEnumerator Charged()
    {
        seen.Clear();
        worstHand.Clear();
        pulses = 0;
        Check("charged_loads", caster.LoadElement(ElementIds.Earth, held: true));
        var start = wizard.transform.position;
        string me = caster.CastPlayer;
        double slowest = 1d;
        float waited = 0f;
        while (World.HeldCharge(World.CastOf(me)) < World.MaxCharge && waited < WaitLimit)
        {
            Measure();
            slowest = System.Math.Min(slowest, World.MoveFactorOf(me));
            waited += Time.deltaTime;
            yield return null;
        }
        yield return Wait(0.35f);
        Check("charge_pulses_at_each_level", pulses == World.MaxCharge - 1, $"{pulses} pulses to level {World.MaxCharge}");
        Check("charging_walks_slower", slowest < World.Rules.Combos.MoveFactor, $"factor {slowest}, {World.Rules.Combos.MoveFactor} while gathering");
        var hips = wizard.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips);
        float dipped = wizard.transform.InverseTransformPoint(hips.position).y;
        caster.LetGo();
        float furthest = 0f, leanedBack = 0f;
        waited = 0f;
        while (beats.Now != Beat.Neutral && waited < WaitLimit)
        {
            Measure();
            if (beats.Now is Beat.Release or Beat.Recovery)
            {
                var from = hips.position - wizard.transform.position;
                leanedBack = Mathf.Max(leanedBack, -Vector3.Dot(Vector3.ProjectOnPlane(from, Vector3.up), wizard.transform.forward));
                furthest = Mathf.Max(furthest, Vector3.Distance(wizard.transform.position, start));
            }
            waited += Time.deltaTime;
            yield return null;
        }
        Metric("charged_hips_low_m", dipped);
        Metric("recoil_drawn_m", leanedBack);
        Metric("slam_seconds", slamSeconds);
        Check("recoil_is_about_half_a_step", leanedBack > 0.2f, $"hips {leanedBack:0.00} m back of the wizard");
        Check("recoil_only_drawn", furthest < 0.01f, $"the wizard itself moved {furthest:0.000} m");
        Check("slam_is_quick", slamSeconds > 0f && slamSeconds < 0.17f, $"hands met in {slamSeconds:0.000} s");
        Report("charged", new[] { Beat.First, Beat.Slam, Beat.Charge, Beat.Release, Beat.Recovery, Beat.Neutral });
    }

    IEnumerator Cancelled()
    {
        seen.Clear();
        worstHand.Clear();
        Check("cancelled_loads", caster.LoadElement(ElementIds.Fire));
        yield return Wait(0.35f);
        caster.DropCast();
        yield return Follow(new[] { Beat.First, Beat.Fizzle, Beat.Neutral }, "cancelled");
    }

    // The same moments, with the pose on or off: a pair gathering and going, a charge held, a cancel.
    IEnumerator Pictures(string set)
    {
        caster.LoadElement(ElementIds.Fire);
        yield return Wait(0.33f);
        Picture(set, "1-first");
        yield return Wait(0.06f);
        caster.LoadElement(ElementIds.Air);
        yield return Wait(0.33f);
        Picture(set, "2-second");
        yield return AtBeat(Beat.Slam, 0.1f);
        Picture(set, "3-slam");
        yield return AtBeat(Beat.Release, 0.11f);
        Picture(set, "5-release");
        yield return AtBeat(Beat.Recovery, 0.05f);
        Picture(set, "6-recovery");
        yield return AtBeat(Beat.Neutral, 0.6f);
        caster.LoadElement(ElementIds.Water, held: true);
        yield return Wait(2.4f);
        Picture(set, "4-charge");
        yield return null;
        caster.LetGo();
        yield return AtBeat(Beat.Neutral, 0.6f);
        caster.LoadElement(ElementIds.Fire);
        yield return Wait(0.33f);
        caster.DropCast();
        yield return Wait(0.06f);
        Picture(set, "7-fizzle");
        yield return AtBeat(Beat.Neutral, 0.6f);
    }

    void Picture(string set, string moment)
    {
        Shot($"{set}-{moment}");
        Metric($"shot_{set}_{moment}", (int)beats.Now);
    }

    IEnumerator AtBeat(Beat beat, float after)
    {
        for (float waited = 0f; beats.Now != beat && waited < WaitLimit; waited += Time.deltaTime) yield return null;
        yield return Wait(after);
    }
}
