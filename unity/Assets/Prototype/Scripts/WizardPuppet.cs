using System.Collections;
using System.Reflection;
using UnityEngine;

// Drives a Starter Assets wizard from a script, for demo recordings: the
// move stick (so walk and run animations play as for a player), which way
// the wizard faces, and where its follow camera looks. Starter Assets
// components are reached by name (see WizardRig) so this compiles before the
// Asset Store packs are restored.
public sealed class WizardPuppet
{
    readonly Transform wizard;
    readonly Component input;
    readonly Component controller;
    readonly FieldInfo cameraYaw;
    readonly FieldInfo cameraPitch;

    public WizardPuppet(GameObject go)
    {
        wizard = go.transform;
        input = go.GetComponent(WizardRig.InputsComponent);
        WizardRig.Field(input, "cursorInputForLook", isPublic: true).SetValue(input, false);
        controller = go.GetComponent(WizardRig.ControllerComponent);
        if (controller == null) return;
        cameraYaw = WizardRig.Field(controller, "_cinemachineTargetYaw", isPublic: false);
        cameraPitch = WizardRig.Field(controller, "_cinemachineTargetPitch", isPublic: false);
    }

    public Vector3 Position => wizard.position;

    /// <summary>Pushes the move stick toward a point, relative to the camera, until the wizard arrives.</summary>
    public IEnumerator WalkTo(Vector3 target)
    {
        var cam = Camera.main.transform;
        while (true)
        {
            var to = target - wizard.position;
            to.y = 0f;
            if (to.magnitude < 0.3f) break;
            var forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            var dir = to.normalized;
            Move(new Vector2(Vector3.Dot(dir, right), Vector3.Dot(dir, forward)));
            yield return null;
        }
        Move(Vector2.zero);
    }

    public void Move(Vector2 stick) => input.SendMessage("MoveInput", stick);

    public void Face(Vector3 point)
    {
        var to = point - wizard.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f) wizard.rotation = Quaternion.LookRotation(to);
    }

    /// <summary>Turns the follow camera toward a point, easing by <paramref name="sharpness"/> per second.</summary>
    public void AimCamera(Vector3 point, float pitch, float sharpness)
    {
        if (cameraYaw == null) return;
        var to = point - wizard.position;
        float targetYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float k = 1f - Mathf.Exp(-sharpness * Time.deltaTime);
        float yaw = (float)cameraYaw.GetValue(controller);
        cameraYaw.SetValue(controller, Mathf.LerpAngle(yaw, targetYaw, k));
        cameraPitch.SetValue(controller, Mathf.Lerp((float)cameraPitch.GetValue(controller), pitch, k));
    }
}
