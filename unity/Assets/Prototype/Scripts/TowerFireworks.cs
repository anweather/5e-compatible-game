using System.Collections;
using UnityEngine;

// Fireworks over the climbing tower's flag when a wizard reaches it
// (TowerCourse): a few seconds of bursts high over the summit, each a sphere
// of bright sparks in one colour that droop and fade, with a flash of light in
// the same colour that washes over the tower. Only the show; it touches
// nothing.
public class TowerFireworks : MonoBehaviour
{
    const int Bursts = 7, Sparks = 140;
    const float Between = 0.4f, Spread = 7f;

    static Material sparks;

    static readonly Color[] Colours =
    {
        new(1f, 0.35f, 0.3f), new(1f, 0.85f, 0.3f), new(0.4f, 0.9f, 1f), new(0.6f, 1f, 0.45f), new(0.95f, 0.5f, 1f), new(1f, 1f, 1f),
    };

    /// <summary>Sets off a show of fireworks round a point.</summary>
    public static void Launch(Vector3 at)
    {
        var go = new GameObject("Tower fireworks");
        go.transform.position = at;
        go.AddComponent<TowerFireworks>();
    }

    IEnumerator Start()
    {
        for (int i = 0; i < Bursts; i++)
        {
            var at = transform.position + new Vector3(Random.Range(-Spread, Spread), Random.Range(0f, Spread), Random.Range(-Spread, Spread));
            Burst(at, Colours[Random.Range(0, Colours.Length)]);
            yield return new WaitForSeconds(Between * Random.Range(0.6f, 1.4f));
        }
        Destroy(gameObject, 4f);
    }

    void Burst(Vector3 at, Color colour)
    {
        var go = new GameObject("Firework burst");
        go.transform.SetParent(transform, false);
        go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.2f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 10f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startColor = new ParticleSystem.MinMaxGradient(colour * 2.5f, Color.Lerp(colour, Color.white, 0.5f) * 2.5f);
        main.gravityModifier = 0.35f;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Sparks) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;
        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.drag = 1.6f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) },
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.75f, 0.55f), 1f) },
        });
        var look = go.GetComponent<ParticleSystemRenderer>();
        look.sharedMaterial = Sparks();
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        StartCoroutine(Flash(go, colour));
    }

    static Material Sparks()
    {
        if (sparks == null) sparks = UrpMaterials.NewUnlitParticles("Firework sparks", Color.white, additive: true);
        return sparks;
    }

    // A light in the burst's colour, bright at once and dying away.
    static IEnumerator Flash(GameObject burst, Color colour)
    {
        var glow = burst.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = colour;
        glow.range = 45f;
        const float seconds = 1.2f;
        for (float t = 0f; t < seconds && glow != null; t += Time.deltaTime)
        {
            float f = 1f - t / seconds;
            glow.intensity = 12f * f * f;
            yield return null;
        }
        if (glow != null) glow.enabled = false;
    }
}
