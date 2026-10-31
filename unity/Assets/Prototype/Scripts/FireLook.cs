using UnityEngine;

// How a fire or its smoke looks by what burns (materials.json "fireLook",
// Andrew, 2026-10-28). A flare (thatch, straw) is quick, tall and bright, with
// more tongues; wood burns steady and orange under darker smoke; oil and grease
// burn low and hot under black smoke. Flames take their numbers from here
// (FlameBank). Smoke is a pooled effect, retuned: it keeps each particle
// system's own settings from when the effect was made, so one taken for
// another look starts from those, not from the last look's.
public class FireLook : MonoBehaviour
{
    public const string Flare = "flare", Wood = "wood", Oil = "oil";

    ParticleSystem[] systems;
    ParticleSystem.MinMaxGradient[] colors;
    string current;

    /// <summary>Each look's flames: how tall and wide, their colour (multiplied), how fast they play, and how many more tongues.</summary>
    public static (float tall, float wide, Color tint, float speed, float more) FlamesOf(string look) => look switch
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

    /// <summary>Smoke over a fire of this look (null: plain smoke).</summary>
    public static void SmokeOf(GameObject fx, string look) => On(fx).Set(look ?? "", Smoke(look));

    static FireLook On(GameObject fx) => fx.TryGetComponent<FireLook>(out var l) ? l : fx.AddComponent<FireLook>();

    // Retunes the effect's systems from their own settings; nothing to do when the look is unchanged.
    void Set(string look, Color tint)
    {
        if (look == current) return;
        current = look;
        if (systems == null) Remember();
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.startColor = Tinted(colors[i], tint);
        }
    }

    void Remember()
    {
        systems = GetComponentsInChildren<ParticleSystem>(true);
        colors = new ParticleSystem.MinMaxGradient[systems.Length];
        for (int i = 0; i < systems.Length; i++) colors[i] = systems[i].main.startColor;
    }

    static ParticleSystem.MinMaxGradient Tinted(ParticleSystem.MinMaxGradient g, Color tint) => g.mode switch
    {
        ParticleSystemGradientMode.Color => new ParticleSystem.MinMaxGradient(g.color * tint),
        ParticleSystemGradientMode.TwoColors => new ParticleSystem.MinMaxGradient(g.colorMin * tint, g.colorMax * tint),
        _ => g,
    };
}
