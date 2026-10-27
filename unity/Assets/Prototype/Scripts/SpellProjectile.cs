using System;
using UnityEngine;

// A spell that travels: flies from the caster to its target cell in an
// arc, then lands (the chemistry happens on arrival) and lets its
// particles fade out. If its caster is destroyed on the way (the solo
// wizard is, when the player goes online), it fizzles instead of landing:
// its landing belongs to a caster that's gone, and a solo cast landing
// after the game went online would change the world outside lockstep.
public class SpellProjectile : MonoBehaviour
{
    public const float DefaultSpeed = 22f;

    public float speed = DefaultSpeed;
    public float arc = 0.12f;

    Vector3 from, to;
    float t, duration;
    Action onArrive;
    UnityEngine.Object caster;

    public static void Launch(GameObject prefab, Vector3 from, Vector3 to, Action onArrive, UnityEngine.Object caster)
    {
        var go = SpellVisuals.Spawn(prefab, from, Quaternion.LookRotation(to - from));
        var p = go.AddComponent<SpellProjectile>();
        p.from = from;
        p.to = to;
        p.duration = Mathf.Max(0.05f, Vector3.Distance(from, to) / p.speed);
        p.onArrive = onArrive;
        p.caster = caster;
    }

    void Update()
    {
        if (caster == null) // destroyed in flight (Unity's null check)
        {
            onArrive = null;
            FadeOut();
            return;
        }
        t += Time.deltaTime / duration;
        float k = Mathf.Clamp01(t);
        var pos = Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * arc * Vector3.Distance(from, to));
        if (pos != transform.position) transform.rotation = Quaternion.LookRotation(pos - transform.position);
        transform.position = pos;
        if (k < 1f) return;

        onArrive?.Invoke();
        onArrive = null;
        FadeOut();
    }

    // Stops emitting and lets the particles already out fade, then goes.
    void FadeOut()
    {
        foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(gameObject, 1.5f);
        enabled = false;
    }
}
