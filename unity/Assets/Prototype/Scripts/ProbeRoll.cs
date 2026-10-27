using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's roll scenario (docs/FAST_LOOP.md): a virtual controller walks
// the wizard north and pulls the left trigger (DodgeRoll), then pulls it again
// just after the roll ends, inside the wait before the next. It follows the
// ground covered and the body's turn (hips to head about the wizard's side).
public class RollProbe : ProbeScenario
{
    const float Watch = 0.75f, SecondPress = 0.55f;

    Gamepad pad;

    protected override IEnumerator Run()
    {
        pad = InputSystem.AddDevice<Gamepad>("ProbePad");
        wizard.TryGetComponent<DodgeRoll>(out var roll);
        wizard.TryGetComponent<Animator>(out var animator);
        var hips = animator != null ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        bool ready = roll != null && wizard.TryGetComponent<RollPose>(out _) && hips != null && head != null;
        Check("roll_parts", ready, "DodgeRoll, RollPose and the hips and head bones");
        if (!ready) yield break;

        var up = new Vector2(0f, 1f);
        Press(up, false);
        yield return Wait(1f);

        int before = roll.Count;
        yield return WatchRoll(up, head, hips);
        Press(up, false);
        yield return Wait(0.3f);

        Metric("reached_3m_s", past3m);
        Metric("turned_deg", turned);
        float tilt = Vector3.Angle(head.position - hips.position, Vector3.up);
        Metric("end_tilt_deg", tilt);
        Check("rolls", roll.Count >= before + 1, $"{roll.Count - before} rolls from the trigger");
        Check("quick", past3m > 0f && past3m < 0.6f, past3m > 0f ? $"3 m in {past3m:0.00} s" : "never 3 m from the start");
        // The pose's own turn (RollPose.Angle); the bones' turn as read here is a metric, as when the
        // probe's frame comes before the pose is laid on it reads nothing.
        Metric("pose_turn_deg", posed);
        Check("tumbles_once", posed > 300f && posed < 420f, $"the pose turned {posed:0} degrees (the bones as read: {turned:0})");
        Check("ends_upright", tilt < 25f, $"hips to head {tilt:0} degrees off upright");
        Check("cooldown_holds", secondPressed && roll.Count == before + 1, $"{roll.Count - before} rolls with the second pull {SecondPress:0.00} s in");
        Shot("after");
    }

    float turned, past3m, posed;
    bool secondPressed;

    // Pulls the trigger and follows the roll for Watch seconds: how far the body turns, when it's 3 m
    // out, and a second pull just after the roll, inside its wait.
    IEnumerator WatchRoll(Vector2 up, Transform head, Transform hips)
    {
        var from = wizard.transform.position;
        float start = Time.time;
        turned = 0f;
        past3m = -1f;
        posed = 0f;
        wizard.TryGetComponent<RollPose>(out var pose);
        bool shot = false;
        var was = head.position - hips.position;
        Press(up, true);
        var endOfFrame = new WaitForEndOfFrame();
        while (Time.time - start < Watch)
        {
            // The tumble is laid on the bones late in the frame (RollPose): read them after it.
            yield return endOfFrame;
            float t = Time.time - start;
            bool second = t >= SecondPress && !secondPressed;
            secondPressed |= second;
            Press(up, t < 0.04f || second);
            var now = head.position - hips.position;
            turned += Vector3.SignedAngle(was, now, wizard.transform.right);
            if (pose != null) posed = Mathf.Max(posed, Mathf.Abs(pose.Angle));
            was = now;
            var d = wizard.transform.position - from;
            d.y = 0f;
            if (past3m < 0f && d.magnitude > 3f) past3m = t;
            if (shot || t <= 0.2f) continue;
            shot = true;
            Shot("mid-roll");
        }
    }

    protected override void OnDestroy()
    {
        if (pad != null) InputSystem.RemoveDevice(pad);
        base.OnDestroy();
    }

    void Press(Vector2 stick, bool trigger) =>
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = stick, leftTrigger = trigger ? 1f : 0f });
}
