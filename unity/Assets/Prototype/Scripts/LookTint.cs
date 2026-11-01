using System.Collections.Generic;
using Chem;
using UnityEngine;

// A look tint (docs/GLOSSARY.md): how far an effect is turned round the colour
// wheel, from the colour it's authored in to the colour of a spell's look. A
// blue fireball is the orange one turned a little over half way round: the white
// heart of the blast stays white, its grey smoke stays grey and the fire goes
// blue. A look of nearly the same hue, or a pale one (white asks for no colour),
// turns nothing, and an effect that isn't turned is drawn exactly as authored:
// the same materials, the same numbers. Only ever a view of the rules in force.
// Pale art has no hue to turn (a splash, a spark, a dust cloud), so a family says
// how far its pale parts are washed in the look (the fourth number of its "own"
// in rules/looks.json; none in the fire families, whose grey smoke stays grey).
public readonly struct LookTint
{
    /// <summary>A colour this pale says nothing about its hue (saturation, 0 to 1).</summary>
    public const float Pale = LookFamilies.PaleSaturation;
    // Hues this close count as the same: Fire Bolt and Meteor differ by a little from their family's orange.
    const float SameHue = LookFamilies.SameHue;
    const float NoTurn = 1e-4f;
    const string UnlitParticles = "Universal Render Pipeline/Particles/Unlit";

    static readonly Dictionary<(Material from, LookTint tint), Material> turned = new();
    static readonly int Turn_ = Shader.PropertyToID("_Turn");

    /// <summary>How far round the wheel, in radians, red toward green.</summary>
    public readonly float Turn;
    // The look's colour at full strength, and in its alpha how far pale colours take it, 0 to 1.
    readonly Color wash;

    LookTint(float turn, Color wash)
    {
        Turn = turn;
        this.wash = wash;
    }

    /// <summary>Whether this changes anything.</summary>
    public bool Any => Mathf.Abs(Turn) > NoTurn || Washes;

    /// <summary>Whether pale colours take the look too (not the fire families, whose smoke stays grey).</summary>
    public bool Washes => wash.a > 0f;

    /// <summary>
    /// What takes <paramref name="own"/> to the hue of <paramref name="look"/>, with pale colours washed in it by
    /// <paramref name="paleWash"/> (0 to 1); none if the look is pale or nearly the same hue. Pale art is only washed.
    /// </summary>
    public static LookTint Between(Color own, Color look, float paleWash = 0f)
    {
        Color.RGBToHSV(own, out float from, out float ownSaturation, out _);
        Color.RGBToHSV(look, out float to, out float lookSaturation, out _);
        if (lookSaturation < Pale || (ownSaturation < Pale && paleWash <= 0f)) return default;
        float apart = ownSaturation < Pale ? 0f : Mathf.Repeat(to - from + 0.5f, 1f) - 0.5f;
        if (ownSaturation >= Pale && Mathf.Abs(apart) <= SameHue) return default;
        float strongest = Mathf.Max(look.r, Mathf.Max(look.g, look.b));
        return new LookTint(apart * 2f * Mathf.PI, new Color(look.r / strongest, look.g / strongest, look.b / strongest, paleWash));
    }

    /// <summary>A colour in this look (HueTurn.hlsl turns the same on the GPU); exactly itself if this changes nothing.</summary>
    public Color Of(Color c)
    {
        if (!Any) return c;
        var spun = Spun(c, Turn);
        if (!Washes) return spun;
        float top = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), low = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        float pale = wash.a * (top > 0f ? low / top : 1f);
        return new Color(spun.r * Mathf.Lerp(1f, wash.r, pale), spun.g * Mathf.Lerp(1f, wash.g, pale), spun.b * Mathf.Lerp(1f, wash.b, pale), c.a);
    }

    static Color Spun(Color c, float turn)
    {
        const float Grey = 0.57735f;
        float s = Mathf.Sin(turn), k = Mathf.Cos(turn), along = Grey * (c.r + c.g + c.b) * (1f - k) * Grey;
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
            // Real lights take their colour from the particles, so those particles turn themselves.
            var system = r.GetComponent<ParticleSystem>();
            if (system != null && system.lights.enabled)
            {
                Recolor(system);
                continue;
            }
            var materials = r.sharedMaterials;
            bool turnedInShader = false;
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = Turned(materials[i], shader);
                turnedInShader |= materials[i] != null && materials[i].shader == shader;
            }
            r.sharedMaterials = materials;
            if (!turnedInShader) Recolor(system);
            if (r.trailMaterial != null) r.trailMaterial = Turned(r.trailMaterial, shader);
        }
        foreach (var light in show.GetComponentsInChildren<Light>(true)) light.color = Of(light.color);
    }

    // The copy of a pack material drawn turned, made once for each material and turn.
    Material Turned(Material from, Shader shader)
    {
        if (from == null || shader == null) return from;
        if (turned.TryGetValue((from, this), out var made) && made != null) return made;
        bool sheet = from.shader.name == UnlitParticles && !from.IsKeywordEnabled("_DISTORTION_ON");
        return turned[(from, this)] = sheet ? TurnedParticle(from, shader) : TurnedColours(from);
    }

    // What only the pack's own shader can draw (a lit puff, a blast's shock dome bending what's behind it)
    // stays on it: its sheets are grey, so its colours are all there is to turn.
    Material TurnedColours(Material from)
    {
        var made = new Material(from) { hideFlags = HideFlags.DontSave };
        foreach (string colour in new[] { "_BaseColor", "_Color" })
            if (from.HasProperty(colour)) made.SetColor(colour, Of(from.GetColor(colour)));
        if (from.HasProperty("_EmissionColor")) made.SetColor("_EmissionColor", Shining(from.GetColor("_EmissionColor")));
        // The old particle shaders double this: white takes the look at full strength over a dull sheet.
        if (from.HasProperty("_TintColor"))
        {
            var tint = from.GetColor("_TintColor");
            var washed = Of(Color.white);
            made.SetColor("_TintColor", new Color(washed.r, washed.g, washed.b, tint.a));
        }
        return made;
    }

    // A glow past white comes out white whatever its hue, so a washed one is all the look's colour.
    Color Shining(Color glow)
    {
        if (!Washes) return Of(glow);
        float top = Mathf.Max(glow.r, Mathf.Max(glow.g, glow.b));
        return new Color(wash.r * top, wash.g * top, wash.b * top, glow.a);
    }

    Material TurnedParticle(Material from, Shader shader)
    {
        var made = new Material(shader) { name = from.name + " in a look", renderQueue = from.renderQueue, hideFlags = HideFlags.DontSave };
        made.SetTexture("_BaseMap", from.GetTexture("_BaseMap"));
        // The shader turns it after this, so a wash goes on turned back.
        var colour = from.GetColor("_BaseColor");
        made.SetColor("_BaseColor", Washes ? Spun(Of(colour), -Turn) : colour);
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
        var trails = ps.trails;
        if (!trails.enabled) return;
        trails.colorOverLifetime = Of(trails.colorOverLifetime);
        trails.colorOverTrail = Of(trails.colorOverTrail);
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
