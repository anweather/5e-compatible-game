using Chem;
using UnityEngine;

// The light an item sheds (items.json "light"), as a real Unity light: a point
// light for a glowing ball, a spot light down the beam for the bullseye
// lantern, which points where its holder aims. Use covers and uncovers it
// (online, for everyone: NetItem keeps whether it is lit).
//
// The SRD's bright and dim light become the light's reach: it ends where the
// dim light does (RangeMetres), and its intensity is set so the bright edge
// still gets BrightLux. Unity's lights fade smoothly to nothing at their range
// rather than stepping from bright to dim, so "dim" is the fading outer half.
public class ItemLight : ItemUse
{
    public const float BrightLux = 0.5f;
    public const float LampLift = 2f;
    public const float DimLux = 0.5f;

    Renderer look;
    Color glow;

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
        if (def.Cone > 0 && look != null) Lamp.transform.position = transform.position + transform.forward * (look.bounds.extents.magnitude + 0.03f);
        Configure(Lamp, def, glow);
        SetOn(!Item.Online || Item.Net.Lit);
        Item.DefChanged += Reconfigure;
    }

    void OnDestroy()
    {
        if (Item != null) Item.DefChanged -= Reconfigure;
    }

    void LateUpdate()
    {
        if (Lamp == null || Lamp.type != LightType.Point || Lamp.transform.parent != transform) return;
        Lamp.transform.position = transform.position + Vector3.up * LampLift;
    }

    void Reconfigure()
    {
        var def = Item.Def.Light;
        if (def == null || Lamp == null) return;
        ColorUtility.TryParseHtmlString(def.Color, out glow);
        bool on = Lamp.enabled;
        Configure(Lamp, def, glow);
        SetGlow(on);
    }

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

    public static float Intensity(ItemLightDef def)
    {
        double range = def.RangeMetres;
        if (range <= 0) return 0f;
        bool bright = def.Bright > 0;
        double d = bright ? def.BrightMetres : range / 2;
        return (float)((bright ? BrightLux : DimLux) * d * d / Window(d, range));
    }

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
        if (Item.Online) Item.Net.RequestLit(on);
        else SetOn(on);
        EventLog.Add(on ? $"You uncover the {Item.Name.ToLowerInvariant()}" : $"You cover the {Item.Name.ToLowerInvariant()}");
    }

    public void SetOn(bool on)
    {
        if (Lamp == null) return;
        Lamp.enabled = on;
        SetGlow(on);
    }

    void SetGlow(bool on)
    {
        if (look == null) return;
        var block = new MaterialPropertyBlock();
        look.GetPropertyBlock(block);
        float strength = Item.Def.Light.Cone > 0 ? 0.6f : 0.9f;
        block.SetColor("_EmissionColor", on ? glow * strength : Color.black);
        look.SetPropertyBlock(block);
    }
}
