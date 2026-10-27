using System.Reflection;
using Punchy.Fauna;
using UnityEngine;

// Shapeshift's body, on the caster's machine: the animal's speeds for the
// Starter Assets controller (through SurfaceEffects, which scales them for
// wading, swimming and slows), its size for the CharacterController (so a
// rabbit slips through a gap a wizard can't) and the camera's height, and a
// jump scaled to it. Swimming works as the wizard's does, at the animal's
// depth. All of it goes back as it was when the wizard changes back.
public partial class Shapeshift
{
    // Sane limits for the controller: a frog is no smaller than this, a bear no bigger.
    const float MinHeight = 0.4f, MaxHeight = 2.2f, MinRadius = 0.12f, MaxRadius = 0.6f;
    static readonly Collider[] Overlaps = new Collider[16];

    CharacterController body;
    SurfaceEffects surface;
    Component mover;
    FieldInfo jumpHeight;
    Transform cameraRoot;
    float height, radius, stepOffset, jump, swimDepth, floatDepth;
    Vector3 center, cameraRootAt;

    /// <summary>The controller's walk and sprint speeds in an animal's shape: a trot (at least half its run) and its run.</summary>
    public static (float move, float sprint) SpeedsOf(CreatureBody b)
    {
        float scale = (b.Size.x + b.Size.y) * 0.5f;
        return (Mathf.Max(b.WalkSpeed, b.RunSpeed * 0.5f) * scale, b.RunSpeed * scale);
    }

    /// <summary>How tall the wizard's capsule is in an animal's shape: to the top of its back and head, within limits.</summary>
    public static float HeightOf(CreatureBody b)
    {
        float scale = (b.Size.x + b.Size.y) * 0.5f;
        return Mathf.Clamp(b.StandHeight * scale * 1.6f, MinHeight, MaxHeight);
    }

    void AwakeBody()
    {
        TryGetComponent(out body);
        TryGetComponent(out surface);
        mover = GetComponent(WizardRig.ControllerComponent);
        jumpHeight = WizardRig.Field(mover, "JumpHeight", isPublic: true);
        cameraRoot = transform.Find(WizardRig.CameraRoot);
    }

    // Takes on the animal's speeds, size and jump, keeping the wizard's own to put back.
    void ShapeBody(CreatureBody b)
    {
        if (body != null)
        {
            (height, radius, center, stepOffset) = (body.height, body.radius, body.center, body.stepOffset);
            float h = HeightOf(b);
            float scale = (b.Size.x + b.Size.y) * 0.5f;
            float r = Mathf.Clamp(Mathf.Min(h * 0.5f, b.BodyLength * scale * 0.4f), MinRadius, MaxRadius);
            float feet = center.y - height * 0.5f;
            body.stepOffset = Mathf.Min(stepOffset, h + 2f * r - 0.05f);
            body.height = h;
            body.radius = r;
            body.center = new Vector3(center.x, feet + h * 0.5f, center.z);
            SetCamera(h / height);
            ScaleSwimming(h / height);
            ScaleJump(Mathf.Clamp(h / height, 0.4f, 1.3f));
        }
        var (move, sprint) = SpeedsOf(b);
        if (surface != null) surface.SetBaseSpeeds(move, sprint);
    }

    void RestoreBody()
    {
        if (body != null)
        {
            body.height = height;
            body.radius = radius;
            body.center = center;
            body.stepOffset = stepOffset;
            if (cameraRoot != null) cameraRoot.localPosition = cameraRootAt;
            if (surface != null) (surface.swimDepth, surface.floatDepth) = (swimDepth, floatDepth);
            if (jumpHeight != null) jumpHeight.SetValue(mover, jump);
        }
        if (surface != null) surface.RestoreBaseSpeeds();
    }

    // The follow camera looks from the animal's height, not the wizard's.
    void SetCamera(float k)
    {
        if (cameraRoot == null) return;
        cameraRootAt = cameraRoot.localPosition;
        cameraRoot.localPosition = new Vector3(cameraRootAt.x, Mathf.Max(0.35f, cameraRootAt.y * k), cameraRootAt.z);
    }

    // Afloat in water as deep, for its size, as the wizard is.
    void ScaleSwimming(float k)
    {
        if (surface == null) return;
        (swimDepth, floatDepth) = (surface.swimDepth, surface.floatDepth);
        surface.swimDepth = swimDepth * k;
        surface.floatDepth = floatDepth * k;
    }

    void ScaleJump(float k)
    {
        if (jumpHeight == null) return;
        jump = (float)jumpHeight.GetValue(mover);
        jumpHeight.SetValue(mover, jump * k);
    }

    // Room for the wizard's own capsule where the animal stands: nothing but itself in it.
    bool RoomToStand()
    {
        if (body == null) return true;
        float feet = transform.position.y + body.center.y - body.height * 0.5f;
        var bottom = new Vector3(transform.position.x, feet + radius + 0.05f, transform.position.z);
        var top = new Vector3(bottom.x, feet + Mathf.Max(radius + 0.06f, height - radius), bottom.z);
        int n = Physics.OverlapCapsuleNonAlloc(bottom, top, radius * 0.9f, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!Overlaps[i].transform.IsChildOf(transform)) return false;
        return true;
    }
}
