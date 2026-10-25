using Unity.Cinemachine;
using UnityEngine;

// Zooms the over-the-shoulder camera in and out: the mouse wheel steps,
// the gamepad d-pad moves smoothly while held. Orbiting is the Starter
// Assets mouse/right-stick look.
[RequireComponent(typeof(CinemachineThirdPersonFollow))]
public class CameraZoom : MonoBehaviour
{
    public PlayerControls controls;
    public float minDistance = 1.5f;
    public float maxDistance = 12f;
    public float wheelStep = 0.8f;
    public float padSpeed = 6f;

    CinemachineThirdPersonFollow follow;

    void Awake() => follow = GetComponent<CinemachineThirdPersonFollow>();

    void Update()
    {
        if (controls == null) return;
        float input = controls.Zoom.ReadValue<float>();
        if (Mathf.Approximately(input, 0f)) return;

        // The d-pad reports -1/1 while held. Wheels and trackpads report deltas of very
        // different sizes per platform, so each scroll event moves at most one step.
        float change = controls.UsingGamepad ? input * padSpeed * Time.deltaTime : -Mathf.Clamp(input * 0.1f, -wheelStep, wheelStep);
        follow.CameraDistance = Mathf.Clamp(follow.CameraDistance + change, minDistance, maxDistance);
    }
}
