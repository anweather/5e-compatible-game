using System;
using UnityEngine;

// A spell that travels: flies from the caster to its target cell in an
// arc, then lands (the chemistry happens on arrival) and lets its
// particles fade out.
public class SpellProjectile : MonoBehaviour
{
    public float speed = 22f;
    public float arc = 0.12f;

    Vector3 from, to;
    float t, duration;
    Action onArrive;

    public static void Launch(GameObject prefab, Vector3 from, Vector3 to, Action onArrive)
    {
        var go = Instantiate(prefab, from, Quaternion.LookRotation(to - from));
        var p = go.AddComponent<SpellProjectile>();
        p.from = from;
        p.to = to;
        p.duration = Mathf.Max(0.05f, Vector3.Distance(from, to) / p.speed);
        p.onArrive = onArrive;
    }

    void Update()
    {
        t += Time.deltaTime / duration;
        float k = Mathf.Clamp01(t);
        var pos = Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * arc * Vector3.Distance(from, to));
        if (pos != transform.position) transform.rotation = Quaternion.LookRotation(pos - transform.position);
        transform.position = pos;
        if (k < 1f) return;

        onArrive?.Invoke();
        onArrive = null;
        foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(gameObject, 1.5f);
        enabled = false;
    }
}
