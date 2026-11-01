using System.Collections.Generic;
using UnityEngine;

// A look tint (docs/GLOSSARY.md): how far an effect is turned round the colour
// wheel, from the colour it's authored in to the colour of a spell's look. A
// blue fireball is the orange one turned a little over half way round: the white
// heart of the blast stays white, its grey smoke stays grey and the fire goes
// blue. A look of nearly the same hue, or a pale one (white asks for no colour),
// turns nothing, and an effect that isn't turned is drawn exactly as authored:
// the same materials, the same numbers. Only ever a view of the rules in force.
public readonly struct LookTint
{
    /// <summary>A colour this pale says nothing about its hue (saturation, 0 to 1).</summary>
    public const float Pale = 0.15f;
    // Hues this close count as the same (a share of the wheel): Fire Bolt and Meteor differ by a little from their family's orange.
    const float SameHue = 12f / 360f;
    const float NoTurn = 1e-4f;
    const string UnlitParticles = "Universal Render Pipeline/Particles/Unlit";

    static readonly Dictionary<(Material from, float turn), Material> turned = new();
    static readonly int Turn_ = Shader.PropertyToID("_Turn");

    /// <summary>How far round the wheel, in radians, red toward green.</summary>
    public readonly float Turn;

    LookTint(float turn) => Turn = turn;

    /// <summary>Whether this changes anything.</summary>
    public bool Any => Mathf.Abs(Turn) > NoTurn;

    /// <summary>What turns <paramref name="own"/> to the hue of <paramref name="look"/>; none if either is pale or they're nearly the same.</summary>
    public static LookTint Between(Color own, Color look)
    {
        Color.RGBToHSV(own, out float from, out float ownSaturation, out _);
        Color.RGBToHSV(look, out float to, out float lookSaturation, out _);
        if (ownSaturation < Pale || lookSaturation < Pale) return default;
        float apart = Mathf.Repeat(to - from + 0.5f, 1f) - 0.5f;
        return Mathf.Abs(apart) <= SameHue ? default : new LookTint(apart * 2f * Mathf.PI);
    }

    /// <summary>A colour turned (HueTurn.hlsl does the same on the GPU); exactly itself if this changes nothing.</summary>
    public Color Of(Color c)
    {
        if (!Any) return c;
        const float Grey = 0.57735f;
        float s = Mathf.Sin(Turn), k = Mathf.Cos(Turn), along = Grey * (c.r + c.g + c.b) * (1f - k) * Grey;
        return new Color(
            Mathf.Max(0f, c.r * k + Grey * (c.b - c.g) * s + along),
            Mathf.Max(0f, c.g * k + Grey * (c.r - c.b) * s + along),
            Mathf.Max(0f, c.b * k + Grey * (c.g - c.r) * s + along),
            c.a);
    }

    /// <summary>
    /// Turns a copy of a pack effect: its particles' materials and colours, and its lights. Once per cast, never per frame.
    /// </summary>
    public void Dress(GameObject show, Shader shader)
    {
        if (!Any) return;
        if (shader == null) shader = Shader.Find("Prototype/LookParticle");
        foreach (var r in show.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            var materials = r.sharedMaterials;
            bool turnedInShader = false;
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = Turned(materials[i], shader);
                turnedInShader |= materials[i] != null && materials[i].shader == shader;
            }
            r.sharedMaterials = materials;
            if (!turnedInShader) Recolor(r.GetComponent<ParticleSystem>());
            if (r.trailMaterial != null) r.trailMaterial = Turned(r.trailMaterial, shader);
        }
        foreach (var light in show.GetComponentsInChildren<Light>(true)) light.color = Of(light.color);
    }

    // The copy of a pack material drawn turned, made once for each material and turn.
    Material Turned(Material from, Shader shader)
    {
        if (from == null || shader == null) return from;
        if (turned.TryGetValue((from, Turn), out var made) && made != null) return made;
        bool sheet = from.shader.name == UnlitParticles && !from.IsKeywordEnabled("_DISTORTION_ON");
        return turned[(from, Turn)] = sheet ? TurnedParticle(from, shader) : TurnedColours(from);
    }

    // What only the pack's own shader can draw (a lit puff, a blast's shock dome bending what's behind it)
    // stays on it: its sheets are grey, so its colours are all there is to turn.
    Material TurnedColours(Material from)
    {
        var made = new Material(from) { hideFlags = HideFlags.DontSave };
        foreach (string colour in new[] { "_BaseColor", "_EmissionColor" })
            if (from.HasProperty(colour)) made.SetColor(colour, Of(from.GetColor(colour)));
        return made;
    }

    Material TurnedParticle(Material from, Shader shader)
    {
        var made = new Material(shader) { name = from.name + " in a look", renderQueue = from.renderQueue, hideFlags = HideFlags.DontSave };
        made.SetTexture("_BaseMap", from.GetTexture("_BaseMap"));
        made.SetColor("_BaseColor", from.GetColor("_BaseColor"));
        made.SetFloat("_SrcBlend", from.GetFloat("_SrcBlend"));
        made.SetFloat("_DstBlend", from.GetFloat("_DstBlend"));
        made.SetVector("_SoftParticleFadeParams", from.GetVector("_SoftParticleFadeParams"));
        made.SetFloat(Turn_, Turn);
        foreach (string keyword in new[] { "_FLIPBOOKBLENDING_ON", "_SOFTPARTICLES_ON" })
            if (from.IsKeywordEnabled(keyword)) made.EnableKeyword(keyword);
        if (from.IsKeywordEnabled("_EMISSION") && from.HasProperty("_EmissionMap") && from.GetTexture("_EmissionMap") != null)
        {
            made.EnableKeyword("_EMISSION");
            made.SetTexture("_EmissionMap", from.GetTexture("_EmissionMap"));
            made.SetColor("_EmissionColor", from.GetColor("_EmissionColor"));
        }
        return made;
    }

    // A particle system whose material stays: what colour it has is its own.
    void Recolor(ParticleSystem ps)
    {
        if (ps == null) return;
        var main = ps.main;
        main.startColor = Of(main.startColor);
        var fade = ps.colorOverLifetime;
        if (fade.enabled) fade.color = Of(fade.color);
    }

    ParticleSystem.MinMaxGradient Of(ParticleSystem.MinMaxGradient g) => g.mode switch
    {
        ParticleSystemGradientMode.Color => new ParticleSystem.MinMaxGradient(Of(g.color)),
        ParticleSystemGradientMode.TwoColors => new ParticleSystem.MinMaxGradient(Of(g.colorMin), Of(g.colorMax)),
        ParticleSystemGradientMode.Gradient => new ParticleSystem.MinMaxGradient(Of(g.gradient)),
        ParticleSystemGradientMode.TwoGradients => new ParticleSystem.MinMaxGradient(Of(g.gradientMin), Of(g.gradientMax)),
        _ => g,
    };

    Gradient Of(Gradient g)
    {
        var keys = g.colorKeys;
        for (int i = 0; i < keys.Length; i++) keys[i].color = Of(keys[i].color);
        return new Gradient { colorKeys = keys, alphaKeys = g.alphaKeys, mode = g.mode };
    }
}
