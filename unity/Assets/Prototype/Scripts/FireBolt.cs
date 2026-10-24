using UnityEngine;
using UnityEngine.InputSystem;

// Fire Bolt (SRD cantrip, 120 ft): click a spot to hurl a bolt at it.
// It ignites anything burnable it hits; water just hisses.
[RequireComponent(typeof(LineRenderer))]
public class FireBolt : MonoBehaviour
{
    public float range = 36f; // 120 ft
    public float cooldown = 0.3f;
    public float tracerTime = 0.12f;

    LineRenderer tracer;
    float nextCast;
    float tracerOff;

    void Awake()
    {
        tracer = GetComponent<LineRenderer>();
        tracer.enabled = false;
    }

    void Update()
    {
        if (tracer.enabled && Time.time >= tracerOff) tracer.enabled = false;

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || Time.time < nextCast) return;

        var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(ray, out var hit, 200f)) return;

        var from = transform.position + Vector3.up * 0.5f;
        if (Vector3.Distance(from, hit.point) > range)
        {
            EventLog.Add("Out of range (120 ft)");
            return;
        }

        nextCast = Time.time + cooldown;
        tracer.SetPosition(0, from);
        tracer.SetPosition(1, hit.point);
        tracer.enabled = true;
        tracerOff = Time.time + tracerTime;

        if (hit.collider.GetComponentInParent<Water>() != null)
        {
            EventLog.Add("The bolt hisses into the water");
            return;
        }
        var target = hit.collider.GetComponentInParent<Burnable>();
        if (target == null) EventLog.Add("The bolt scorches the ground");
        else if (target.IsBurnt) EventLog.Add($"The {target.displayName} is already burnt");
        else if (target.Ignite()) EventLog.Add($"The {target.displayName} catches fire");
    }
}
