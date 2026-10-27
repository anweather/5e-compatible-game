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
        var from = wizard.transform.position;
        float start = Time.time, turned = 0f, past3m = -1f;
        bool shot = false, secondPressed = false;
        var was = head.position - hips.position;
        Press(up, true);
        while (Time.time - start < Watch)
        {
            yield return null;
            float t = Time.time - start;
            // Let go after a couple of frames; pull again just after the roll, inside its wait.
            bool second = t >= SecondPress && !secondPressed;
            secondPressed |= second;
            Press(up, t < 0.04f || second);
            var now = head.position - hips.position;
            turned += Vector3.SignedAngle(was, now, wizard.transform.right);
            was = now;
            var d = wizard.transform.position - from;
            d.y = 0f;
            if (past3m < 0f && d.magnitude > 3f) past3m = t;
            if (!shot && t > 0.2f)
            {
                shot = true;
                Shot("mid-roll");
            }
        }
        Press(up, false);
        yield return Wait(0.3f);

        Metric("reached_3m_s", past3m);
        Metric("turned_deg", turned);
        float tilt = Vector3.Angle(head.position - hips.position, Vector3.up);
        Metric("end_tilt_deg", tilt);
        Check("rolls", roll.Count >= before + 1, $"{roll.Count - before} rolls from the trigger");
        Check("quick", past3m > 0f && past3m < 0.6f, past3m > 0f ? $"3 m in {past3m:0.00} s" : "never 3 m from the start");
        Check("tumbles_once", Mathf.Abs(turned) > 300f && Mathf.Abs(turned) < 420f, $"the body turned {turned:0} degrees");
        Check("ends_upright", tilt < 25f, $"hips to head {tilt:0} degrees off upright");
        Check("cooldown_holds", secondPressed && roll.Count == before + 1, $"{roll.Count - before} rolls with the second pull {SecondPress:0.00} s in");
        Shot("after");
    }

    protected override void OnDestroy()
    {
        if (pad != null) InputSystem.RemoveDevice(pad);
        base.OnDestroy();
    }

    void Press(Vector2 stick, bool trigger) =>
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = stick, leftTrigger = trigger ? 1f : 0f });
}
