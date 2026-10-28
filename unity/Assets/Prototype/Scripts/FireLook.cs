using UnityEngine;

// How a fire or its smoke looks by what burns (materials.json "fireLook",
// Andrew, 2026-10-28): the same pooled effect, retuned. A flare (thatch,
// straw) is quick, tall and bright, with more of its embers; wood burns
// steady and orange under darker smoke; oil and grease burn low and hot under
// black smoke. It keeps each particle system's own settings from when the
// effect was made, so a pooled effect taken for another look starts from
// those, not from the last look's.
public class FireLook : MonoBehaviour
{
    public const string Flare = "flare", Wood = "wood", Oil = "oil";

    ParticleSystem[] systems;
    ParticleSystem.MinMaxGradient[] colors;
    float[] speeds, rates;
    string current;

    // Each look's flames: how tall and wide (on the caller's scale), their colour (multiplied), how fast
    // they play, and how many more particles.
    static (float tall, float wide, Color tint, float speed, float more) Flames(string look) => look switch
    {
        Flare => (1.8f, 1.05f, new Color(1f, 0.95f, 0.75f), 1.7f, 1.6f),
        Wood => (1f, 1f, new Color(1f, 0.62f, 0.32f), 0.8f, 1f),
        Oil => (0.6f, 1.3f, new Color(1f, 0.45f, 0.18f), 1.25f, 1.2f),
        _ => (1f, 1f, Color.white, 1f, 1f),
    };

    // Its smoke's colour (multiplied).
    static Color Smoke(string look) => look switch
    {
        Wood => new Color(0.42f, 0.4f, 0.38f),
        Oil => new Color(0.12f, 0.11f, 0.11f),
        _ => Color.white,
    };

    /// <summary>Flames of this look (null: the plain fire) at this size.</summary>
    public static void Flames(GameObject fx, string look, Vector3 scale)
    {
        var (tall, wide, tint, speed, more) = Flames(look);
        fx.transform.localScale = Vector3.Scale(scale, new Vector3(wide, tall, wide));
        On(fx).Set(look ?? "", tint, speed, more);
    }

    /// <summary>Smoke over a fire of this look (null: plain smoke).</summary>
    public static void SmokeOf(GameObject fx, string look) => On(fx).Set(look ?? "", Smoke(look), 1f, 1f);

    static FireLook On(GameObject fx) => fx.TryGetComponent<FireLook>(out var l) ? l : fx.AddComponent<FireLook>();

    // Retunes the effect's systems from their own settings; nothing to do when the look is unchanged.
    void Set(string look, Color tint, float speed, float more)
    {
        if (look == current) return;
        current = look;
        if (systems == null) Remember();
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.startColor = Tinted(colors[i], tint);
            main.simulationSpeed = speeds[i] * speed;
            var emission = systems[i].emission;
            emission.rateOverTimeMultiplier = rates[i] * more;
        }
    }

    void Remember()
    {
        systems = GetComponentsInChildren<ParticleSystem>(true);
        colors = new ParticleSystem.MinMaxGradient[systems.Length];
        speeds = new float[systems.Length];
        rates = new float[systems.Length];
        for (int i = 0; i < systems.Length; i++)
        {
            colors[i] = systems[i].main.startColor;
            speeds[i] = systems[i].main.simulationSpeed;
            rates[i] = systems[i].emission.rateOverTimeMultiplier;
        }
    }

    static ParticleSystem.MinMaxGradient Tinted(ParticleSystem.MinMaxGradient g, Color tint) => g.mode switch
    {
        ParticleSystemGradientMode.Color => new ParticleSystem.MinMaxGradient(g.color * tint),
        ParticleSystemGradientMode.TwoColors => new ParticleSystem.MinMaxGradient(g.colorMin * tint, g.colorMax * tint),
        _ => g,
    };
}
