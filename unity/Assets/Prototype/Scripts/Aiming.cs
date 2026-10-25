using UnityEngine;
using UnityEngine.InputSystem;

// Where the player is aiming, shared by spells (SpellCaster) and held items
// (throwing, the lantern's beam, the Dancing Lights mote).
public static class Aiming
{
    static readonly RaycastHit[] Hits = new RaycastHit[16];

    /// <summary>
    /// With mouse-look or a gamepad, through the crosshair at the screen centre; with a free
    /// cursor (e.g. after Esc in the Editor), where it points.
    /// </summary>
    public static Ray Ray(PlayerControls controls)
    {
        bool centre = Cursor.lockState == CursorLockMode.Locked || (controls != null && controls.UsingGamepad) || Mouse.current == null;
        var aimAt = centre ? new Vector2(Screen.width / 2f, Screen.height / 2f) : Mouse.current.position.ReadValue();
        return Camera.main.ScreenPointToRay(aimAt);
    }

    /// <summary>
    /// The nearest hit that isn't <paramref name="self"/> or its children (what it holds): the
    /// over-the-shoulder crosshair often passes through the wizard's own collider first.
    /// </summary>
    public static bool PastSelf(Ray ray, Transform self, out RaycastHit nearest, float maxDistance = 200f)
    {
        nearest = default;
        int count = Physics.RaycastNonAlloc(ray, Hits, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (Hits[i].collider.transform.IsChildOf(self) || Hits[i].distance >= best) continue;
            best = Hits[i].distance;
            nearest = Hits[i];
        }
        return best < float.MaxValue;
    }
}
