using Unity.Cinemachine;
using UnityEngine;

// What a cast does to the follow camera besides its impulses (CastImpact): it
// holds still through a hit-stop and eases back after it, the lens tightens a
// little while charging, and a fine shake builds with the charge and tires if the
// cast is held on. All from the local wizard's CastBeats and only drawn: nothing
// reads the camera's place for the game, and the Shake and rumble setting scales
// it all, so at 0 this does nothing. Runs after CameraClearance (added after it).
public class CastCamera : CinemachineExtension
{
    Transform seen;
    CastBeats beats;
    float tight, ease;
    bool holding;
    Vector3 lastAt, heldAt;
    Quaternion lastTurn = Quaternion.identity, heldTurn = Quaternion.identity;

    /// <summary>The lens's tightening now (degrees), for the probe.</summary>
    public float Tightening { get; private set; }
    /// <summary>The charge shake's size now (m), for the probe.</summary>
    public float ChargeShake { get; private set; }

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize) return;
        Find(vcam.Follow);
        Tightening = ChargeShake = 0f;
        if (beats == null || deltaTime < 0f)
        {
            holding = false;
            tight = ease = 0f;
            return;
        }
        Charging(ref state, deltaTime);
        Holding(ref state, deltaTime);
    }

    void Find(Transform target)
    {
        if (target == seen) return;
        seen = target;
        beats = target != null ? target.GetComponentInParent<CastBeats>() : null;
    }

    void Charging(ref CameraState state, float deltaTime)
    {
        var feel = beats.feel;
        float level = beats.Now == Beat.Charge ? beats.Charge / (float)Chem.World.MaxCharge : 0f;
        tight = Mathf.MoveTowards(tight, level, deltaTime / Mathf.Max(0.01f, feel.chargeEase));
        float amount = GameFeel.Shake * tight;
        if (amount <= 0f) return;
        Tightening = feel.chargeTighten * amount;
        state.Lens.FieldOfView -= Tightening;
        float tired = beats.Now == Beat.Charge ? Mathf.Clamp01(beats.InBeat - feel.chargeShakeTires) : 1f;
        ChargeShake = feel.chargeShake * amount * tight * Mathf.Lerp(1f, feel.chargeShakeHeld, tired);
        float at = Time.time * feel.chargeShakeHertz;
        var offset = new Vector3(Mathf.PerlinNoise(at, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, at) - 0.5f, 0f) * (2f * ChargeShake);
        state.PositionCorrection += state.GetFinalOrientation() * offset;
    }

    // Still through a hit-stop, whatever the wizard does, then back to the live view without a jump.
    void Holding(ref CameraState state, float deltaTime)
    {
        if (beats.Stopped)
        {
            // Where it was last drawn: this frame may already have moved it.
            if (!holding)
            {
                heldAt = lastAt;
                heldTurn = lastTurn;
            }
            holding = true;
            ease = 1f;
        }
        else
        {
            holding = false;
            ease = Mathf.MoveTowards(ease, 0f, deltaTime / Mathf.Max(0.01f, beats.feel.thawSeconds));
        }
        if (ease > 0f)
        {
            state.PositionCorrection += (heldAt - state.GetFinalPosition()) * ease;
            state.OrientationCorrection *= Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(state.GetFinalOrientation()) * heldTurn, ease);
        }
        lastAt = state.GetFinalPosition();
        lastTurn = state.GetFinalOrientation();
    }
}
