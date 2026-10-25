using Chem;
using UnityEngine;

// The light an item sheds (items.json "light"), as a real Unity light: a point
// light for a glowing ball, a spot light down the beam for the bullseye
// lantern, which points where its holder aims. Use covers and uncovers it.
//
// The SRD's bright and dim light become the light's reach: it ends where the
// dim light does (RangeMetres), and its intensity is set so the bright edge
// still gets BrightLux. Unity's lights fade smoothly to nothing at their range
// rather than stepping from bright to dim, so "dim" is the fading outer half.
public class ItemLight : ItemUse
{
    /// <summary>Light falling on a surface at the bright edge (URP's units, the sun being 1.6 here).</summary>
    public const float BrightLux = 0.8f;
    /// <summary>For a light that is only dim (Dancing Lights): what falls halfway out.</summary>
    public const float DimLux = 0.5f;

    Renderer look;
    Color glow;

    /// <summary>The light itself, on a child, so DancingLights can send it out on its own.</summary>
    public Light Lamp { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        TryGetComponent(out look);
    }

    void Start()
    {
        var def = Item.Def.Light;
        ColorUtility.TryParseHtmlString(def.Color, out glow);
        Lamp = new GameObject("Light").AddComponent<Light>();
        Lamp.transform.SetParent(transform, false);
        // A beam starts just in front, so the lantern's own body doesn't shadow it.
        if (def.Cone > 0 && look != null) Lamp.transform.position = transform.position + transform.forward * (look.bounds.extents.magnitude + 0.03f);
        Configure(Lamp, def, glow);
        SetGlow(true);
        Item.DefChanged += Reconfigure;
    }

    void OnDestroy()
    {
        if (Item != null) Item.DefChanged -= Reconfigure;
    }

    // A live mod changed the item's light (e.g. "bright-lantern"): same lamp, new reach and colour.
    void Reconfigure()
    {
        var def = Item.Def.Light;
        if (def == null || Lamp == null) return;
        ColorUtility.TryParseHtmlString(def.Color, out glow);
        bool on = Lamp.enabled;
        Configure(Lamp, def, glow);
        SetGlow(on);
    }

    /// <summary>Sets up <paramref name="lamp"/> for an items.json light: a spot for a cone, otherwise a point light.</summary>
    public static void Configure(Light lamp, ItemLightDef def, Color color)
    {
        bool cone = def.Cone > 0;
        lamp.type = cone ? LightType.Spot : LightType.Point;
        lamp.color = color;
        lamp.range = (float)def.RangeMetres;
        lamp.intensity = Intensity(def);
        lamp.shadows = cone ? LightShadows.Soft : LightShadows.None;
        if (!cone) return;
        lamp.spotAngle = (float)def.Cone;
        lamp.innerSpotAngle = (float)def.Cone * 0.75f;
    }

    /// <summary>
    /// URP's falloff is 1/d² times a window that reaches 0 at the range, (1 - (d/r)^4)²; this picks
    /// the intensity that leaves BrightLux at the bright edge (or DimLux halfway out for a dim light).
    /// </summary>
    public static float Intensity(ItemLightDef def)
    {
        double range = def.RangeMetres;
        if (range <= 0) return 0f;
        bool bright = def.Bright > 0;
        double d = bright ? def.BrightMetres : range / 2;
        return (float)((bright ? BrightLux : DimLux) * d * d / Window(d, range));
    }

    /// <summary>The light URP's falloff leaves at <paramref name="distance"/> from a lamp (before the spot's cone).</summary>
    public static double Falloff(double intensity, double distance, double range)
    {
        if (distance <= 0 || distance >= range) return distance <= 0 ? intensity : 0;
        return intensity / (distance * distance) * Window(distance, range);
    }

    static double Window(double distance, double range)
    {
        double f = distance * distance / (range * range);
        double w = System.Math.Max(0, 1 - f * f);
        return w * w;
    }

    public override void Use(ItemHolder holder)
    {
        bool on = !Lamp.enabled;
        Lamp.enabled = on;
        SetGlow(on);
        EventLog.Add(on ? $"You uncover the {Item.Name.ToLowerInvariant()}" : $"You cover the {Item.Name.ToLowerInvariant()}");
    }

    // The item's own surface glows in its light's colour while lit (bloom picks it up).
    void SetGlow(bool on)
    {
        if (look == null) return;
        var block = new MaterialPropertyBlock();
        look.GetPropertyBlock(block);
        // A lantern's brass only warms a little; a ball with the Light cantrip on it shines.
        float strength = Item.Def.Light.Cone > 0 ? 0.6f : 1.5f;
        block.SetColor("_EmissionColor", on ? glow * strength : Color.black);
        look.SetPropertyBlock(block);
    }
}
