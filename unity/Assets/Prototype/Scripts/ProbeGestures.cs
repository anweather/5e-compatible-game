using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The probe's gestures scenario (docs/FAST_LOOP.md): the casting gestures
// (CastPose, CastGestures), each cast from the bar as a player casts it, with
// the camera beside the wizard. Follows the hands frame by frame in the
// wizard's own frame (height over its feet, reach toward the target) and
// checks the shape of each: Gust of Wind shoves both hands forward, Raise
// Earth sweeps them from low to overhead, Fireball draws the casting hand
// back then throws it forward, Meteor raises both overhead then pulls them
// down, the meteor starting down at the pull. Shots mid-gesture.
public class GesturesProbe : ProbeScenario
{
    readonly struct Sample
    {
        public readonly float t, rightY, leftY, rightZ, leftZ, headY, shoulderY;

        public Sample(float t, Vector3 right, Vector3 left, float headY, float shoulderY)
        {
            this.t = t;
            rightY = right.y;
            leftY = left.y;
            rightZ = right.z;
            leftZ = left.z;
            this.headY = headY;
            this.shoulderY = shoulderY;
        }
    }

    readonly List<Sample> samples = new();
    CastPose pose;
    Transform rightHand, leftHand, head, shoulder;
    float scale = 1f, meteorAt = -1f;

    protected override IEnumerator Run()
    {
        pose = wizard.GetComponent<CastPose>();
        var animator = wizard.GetComponent<Animator>();
        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        head = animator.GetBoneTransform(HumanBodyBones.Head);
        shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Check("cast_pose", pose != null && rightHand != null && leftHand != null && head != null && shoulder != null, "CastPose and the hand, head and shoulder bones");
        if (pose == null || rightHand == null || leftHand == null || head == null || shoulder == null) yield break;

        Place(4, 10);
        yield return Wait(0.5f);
        scale = Mathf.Max(0.3f, (shoulder.position.y - wizard.transform.position.y) / 1.45f);
        Metric("shoulder_height_m", shoulder.position.y - wizard.transform.position.y);

        yield return Perform("gust", SpellIds.GustOfWind, (5, 10, 1), CastGesture.Shove, 0.18f, 0.4f);
        CheckGust();
        yield return Wait(1f);
        yield return Perform("raise", SpellIds.MoldEarthHeap, (8, 10, 1), CastGesture.Raise, 0.32f, 0.7f);
        CheckRaise();
        yield return Wait(1f);
        yield return Perform("fireball", SpellIds.Fireball, (14, 10, 1), CastGesture.Throw, 0.3f, 0.46f);
        CheckFireball();
        yield return Wait(1.5f);
        yield return Perform("meteor", SpellIds.Meteor, (14, 3, 1), CastGesture.CallDown, 0.5f, 0.95f);
        CheckMeteor();
        yield return Wait(2f);
    }

    // Faces the target, casts from the bar, and follows the hands until the gesture is over; two shots on the way.
    IEnumerator Perform(string label, string spellId, (int x, int y, int dir) at, CastGesture expected, float shotA, float shotB)
    {
        var (x, y, dir) = at;
        samples.Clear();
        meteorAt = -1f;
        Check($"bar_has_{spellId}", caster.Select(spellId));
        var cell = grid.YardCell(x, y);
        puppet.Face(grid.CellCenter(cell.x, cell.y));
        SideView();
        yield return Wait(0.6f);

        caster.Cast(caster.SelectedId, cell.x, cell.y, dir);
        Check($"{label}_gesture", pose.Playing == expected, $"playing {pose.Playing}, expected {expected}");
        float duration = CastGestures.Of(expected).Duration, start = Time.time;
        bool tookA = false, tookB = false;
        while (Time.time - start < duration)
        {
            yield return null;
            float t = Time.time - start;
            samples.Add(Measure(t));
            if (meteorAt < 0f && GameObject.Find("Meteor") != null) meteorAt = t;
            if (!tookA && t >= shotA) { tookA = true; Shot($"{label}-a"); }
            if (!tookB && t >= shotB) { tookB = true; Shot($"{label}-b"); }
        }
    }

    Sample Measure(float t)
    {
        var body = wizard.transform;
        float feet = body.position.y;
        return new Sample(t, body.InverseTransformPoint(rightHand.position), body.InverseTransformPoint(leftHand.position),
            head.position.y - feet, shoulder.position.y - feet);
    }

    // From the wizard's right, a little ahead, at chest height: the whole body and the arms in profile.
    void SideView()
    {
        var body = wizard.transform;
        var chest = body.position + Vector3.up * (1.2f * scale);
        Cut(chest + body.right * 4.5f + body.forward * 1.5f + Vector3.up * 0.6f, chest);
    }

    // The first sample (after <paramref name="after"/> seconds) that passes, or null.
    Sample? First(Func<Sample, bool> test, float after = -1f)
    {
        foreach (var s in samples)
            if (s.t > after && test(s)) return s;
        return null;
    }

    static string When(Sample? s) => s.HasValue ? $" at {s.Value.t:0.00} s" : " (not seen)";

    void CheckGust()
    {
        float reach = samples.Count > 0 ? samples.Max(s => Mathf.Min(s.rightZ, s.leftZ)) : 0f;
        Metric("gust_hands_forward_m", reach);
        var pushed = First(s => Mathf.Min(s.rightZ, s.leftZ) > 0.4f * scale && Mathf.Min(s.rightY, s.leftY) > s.shoulderY - 0.35f * scale);
        Check("gust_both_hands_push", pushed.HasValue,
            pushed.HasValue ? $"both hands {reach:0.00} m forward, at {pushed.Value.t:0.00} s" : $"the hands reached {reach:0.00} m forward at most");
    }

    void CheckRaise()
    {
        float lowest = samples.Count > 0 ? samples.Min(s => Mathf.Max(s.rightY, s.leftY)) : 0f;
        float highest = samples.Count > 0 ? samples.Max(s => Mathf.Min(s.rightY, s.leftY)) : 0f;
        Metric("raise_hands_low_m", lowest);
        Metric("raise_hands_high_m", highest);
        var low = First(s => Mathf.Max(s.rightY, s.leftY) < 0.95f * scale);
        var high = low.HasValue ? First(s => Mathf.Min(s.rightY, s.leftY) > s.headY, low.Value.t) : null;
        Check("raise_hands_low_then_high", low.HasValue && high.HasValue,
            $"both hands down to {lowest:0.00} m{When(low)}, then up to {highest:0.00} m{When(high)}");
    }

    void CheckFireball()
    {
        float back = samples.Count > 0 ? samples.Min(s => s.rightZ) : 0f;
        float forward = samples.Count > 0 ? samples.Max(s => s.rightZ) : 0f;
        Metric("fireball_hand_back_m", back);
        Metric("fireball_hand_forward_m", forward);
        var drawn = First(s => s.rightZ < -0.08f * scale && s.rightY > s.shoulderY);
        var thrown = drawn.HasValue ? First(s => s.rightZ > 0.3f * scale, drawn.Value.t) : null;
        Check("fireball_hand_back_then_forward", drawn.HasValue && thrown.HasValue,
            $"the casting hand back to {back:0.00} m{When(drawn)}, then forward to {forward:0.00} m{When(thrown)}");
    }

    void CheckMeteor()
    {
        float top = samples.Count > 0 ? samples.Max(s => Mathf.Min(s.rightY, s.leftY) - s.headY) : 0f;
        Metric("meteor_hands_over_head_m", top);
        var overhead = First(s => Mathf.Min(s.rightY, s.leftY) > s.headY);
        var down = overhead.HasValue ? First(s => Mathf.Max(s.rightY, s.leftY) < s.shoulderY, overhead.Value.t) : null;
        Check("meteor_hands_overhead_then_down", overhead.HasValue && down.HasValue,
            $"both hands {top:0.00} m over the head at most{When(overhead)}, below the shoulders after it{When(down)}");
        float release = CastGestures.Of(CastGesture.CallDown).release;
        Metric("meteor_appears_s", meteorAt);
        Check("meteor_at_the_pull", meteorAt >= release - 0.05f && meteorAt <= release + 0.2f && (!down.HasValue || meteorAt <= down.Value.t + 0.2f),
            $"the meteor appeared {meteorAt:0.00} s after the cast; the pull releases at {release:0.00} s");
    }
}
