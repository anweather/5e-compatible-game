using System.Reflection;
using UnityEngine;

// The wizard's vitals on the HUD (EventLog draws it), in Andrew's layout
// (docs/SPELL_COMBOS.md): a portrait in the top left corner (a placeholder: the
// wizard's hat, face and robe in its look's colours) with health and magic
// stacked to its right; and stamina as a green ring round the crosshair that
// starts full and drains, shown only while it's in use (sprinting, swimming,
// rolling, or not yet full again) and faded out a moment after, BotW-style.
public sealed class VitalsHud
{
    /// <summary>The margin from the screen's edge, and the portrait's size: DpadHud's block matches them.</summary>
    public const float Margin = 24f, BlockHeight = 104f;

    const float BarWidth = 300f, BarHeight = 46f, Gap = 12f;
    const float RingInner = 40f, RingOuter = 54f;
    // How fast stamina fades in and out (per second), and how long it stays after the effort stops (s).
    const float FadeRate = 4f, Linger = 0.8f;

    static readonly Color HealthColor = new(0.85f, 0.25f, 0.25f), StaminaColor = new(0.3f, 0.9f, 0.35f), MagicColor = new(0.35f, 0.55f, 1f);
    static readonly Color Skin = new(0.86f, 0.72f, 0.6f), Frame = new(0f, 0f, 0f, 0.55f), Unknown = new(0.2f, 0.25f, 0.5f);

    GameObject wizard;
    Vitals vitals;
    WizardOutfit outfit;
    SurfaceEffects surface;
    DodgeRoll roll;
    Component inputs;
    FieldInfo sprint, move;
    GUIStyle text;
    float staminaShown, inUseUntil;

    /// <summary>Draws the vitals for <paramref name="who"/>; the height they take at the top left.</summary>
    public float Draw(GameObject who, GUIStyle style)
    {
        if (who != wizard) Find(who);
        if (vitals == null) return 0f;
        text ??= new GUIStyle(style) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        Portrait(new Rect(Margin, Margin, BlockHeight, BlockHeight));
        float x = Margin + BlockHeight + Gap;
        Bar(new Rect(x, Margin, BarWidth, BarHeight), $"<b>Health</b> {Mathf.CeilToInt(vitals.Health)}/{Mathf.CeilToInt(vitals.HealthMax)}", vitals.Health / vitals.HealthMax, HealthColor);
        Bar(new Rect(x, Margin + BarHeight + Gap, BarWidth, BarHeight), $"<b>Magic</b> {Mathf.CeilToInt(vitals.Magic)}/{Mathf.CeilToInt(Vitals.MagicMax)}", vitals.Magic / Vitals.MagicMax, MagicColor);
        Stamina();
        return BlockHeight;
    }

    void Find(GameObject who)
    {
        wizard = who;
        who.TryGetComponent(out vitals);
        who.TryGetComponent(out outfit);
        who.TryGetComponent(out surface);
        who.TryGetComponent(out roll);
        inputs = who.GetComponent(WizardRig.InputsComponent);
        sprint = inputs != null ? WizardRig.Field(inputs, "sprint", isPublic: true) : null;
        move = inputs != null ? WizardRig.Field(inputs, "move", isPublic: true) : null;
    }

    // The placeholder portrait: a framed square with the wizard's hat over a face over its robe.
    void Portrait(Rect r)
    {
        var look = outfit != null ? outfit.Look : null;
        var robe = look != null ? Opaque(look.Robe) : Unknown;
        var hat = look != null ? Opaque(look.Hat) : Unknown;
        Fill(r, Frame);
        var inner = new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f);
        Fill(inner, Color.Lerp(robe, Color.black, 0.4f));
        float w = inner.width, h = inner.height;
        Fill(new Rect(inner.x + w * 0.2f, inner.y + h * 0.62f, w * 0.6f, h * 0.38f), robe);
        Fill(new Rect(inner.x + w * 0.32f, inner.y + h * 0.36f, w * 0.36f, h * 0.28f), Skin);
        if (look != null && look.BeardLength > 0f) Fill(new Rect(inner.x + w * 0.36f, inner.y + h * 0.52f, w * 0.28f, h * 0.22f), Opaque(look.Beard));
        Fill(new Rect(inner.x + w * 0.18f, inner.y + h * 0.32f, w * 0.64f, h * 0.06f), hat);
        Fill(new Rect(inner.x + w * 0.36f, inner.y + h * 0.16f, w * 0.28f, h * 0.17f), hat);
        Fill(new Rect(inner.x + w * 0.44f, inner.y + h * 0.04f, w * 0.12f, h * 0.13f), hat);
    }

    // Stamina round the crosshair while it's in use, fading in and out.
    void Stamina()
    {
        if (InUse() || vitals.Stamina < Vitals.StaminaMax) inUseUntil = Time.unscaledTime + Linger;
        float target = Time.unscaledTime < inUseUntil ? 1f : 0f;
        staminaShown = Mathf.MoveTowards(staminaShown, target, FadeRate * Time.unscaledDeltaTime);
        if (staminaShown <= 0f) return;
        var centre = new Vector2(HudScale.Width / 2f, HudScale.Height / 2f);
        HudDraw.Ring(centre, RingInner - 3f, RingOuter + 3f, 1f, new Color(0f, 0f, 0f, 0.45f * staminaShown));
        var colour = StaminaColor;
        colour.a = staminaShown;
        HudDraw.Ring(centre, RingInner, RingOuter, vitals.Stamina / Vitals.StaminaMax, colour);
    }

    // Sprinting (the sprint held while moving), swimming or rolling.
    bool InUse()
    {
        if (surface != null && surface.Swimming) return true;
        if (roll != null && roll.Rolling) return true;
        if (sprint == null || move == null) return false;
        return (bool)sprint.GetValue(inputs) && ((Vector2)move.GetValue(inputs)).sqrMagnitude > 0.01f;
    }

    void Bar(Rect r, string label, float share, Color colour)
    {
        Fill(r, new Color(0f, 0f, 0f, 0.55f));
        Fill(new Rect(r.x + 4f, r.y + 4f, (r.width - 8f) * Mathf.Clamp01(share), r.height - 8f), colour);
        GUI.Label(r, label, text);
    }

    static void Fill(Rect r, Color colour)
    {
        GUI.color = colour;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // The look's colours are matte (alpha 0 is VillageLit's no-glow); drawn on the HUD, opaque.
    static Color Opaque(Color c) => new(c.r, c.g, c.b, 1f);
}
