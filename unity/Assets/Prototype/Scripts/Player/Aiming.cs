using UnityEngine;
using UnityEngine.InputSystem;

// Where the player is aiming, shared by spells (SpellCaster) and held items
// (throwing, the lantern's beam, the Dancing Lights mote).
public static class Aiming
{
    static readonly RaycastHit[] Hits = new RaycastHit[16];

    public static Ray Ray(PlayerControls controls)
    {
        bool centre = Cursor.lockState == CursorLockMode.Locked || (controls != null && controls.UsingGamepad) || Mouse.current == null;
        var aimAt = centre ? new Vector2(Screen.width / 2f, Screen.height / 2f) : Mouse.current.position.ReadValue();
        return Camera.main.ScreenPointToRay(aimAt);
    }

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
