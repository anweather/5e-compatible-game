using System.Reflection;
using UnityEngine;

// The wizard's vitals on the HUD (EventLog draws it), in Andrew's layout
// (docs/SPELL_COMBOS.md): a portrait in the top left corner (a placeholder: the
// wizard's headwear, black face and red eyes, and robe in its look's colours) with health and magic
// stacked to its right; and stamina as a green ring round the crosshair that
// starts full and drains, shown only while it's in use (sprinting, swimming,
// rolling, or not yet full again) and faded out a moment after, BotW-style.
// Breath is a bar to the right of health, there only while it runs down under
// water or fills again (Vitals.Breath), faded in and out the same way.
public sealed class VitalsHud
{
    /// <summary>The margin from the screen's edge, and the portrait's size: DpadHud's block matches them.</summary>
    public const float Margin = 24f, BlockHeight = 104f;

    const float BarWidth = 300f, BarHeight = 46f, Gap = 12f;
    const float RingInner = 40f, RingOuter = 54f;
    // How fast stamina fades in and out (per second), and how long it stays after the effort stops (s).
    const float FadeRate = 4f, Linger = 0.8f;

    static readonly Color HealthColor = new(0.85f, 0.25f, 0.25f), StaminaColor = new(0.3f, 0.9f, 0.35f), MagicColor = new(0.35f, 0.55f, 1f);
    static readonly Color BreathColor = new(0.75f, 0.92f, 1f), DrowningColor = new(0.3f, 0.45f, 0.9f);
    static readonly Color FaceColor = new(0.02f, 0.02f, 0.025f), EyeColor = new(1f, 0.1f, 0.06f), Frame = new(0f, 0f, 0f, 0.55f), Unknown = new(0.2f, 0.25f, 0.5f);

    GameObject wizard;
    Vitals vitals;
    WizardOutfit outfit;
    SurfaceEffects surface;
    DodgeRoll roll;
    Component inputs;
    FieldInfo sprint, move;
    GUIStyle text;
    float staminaShown, inUseUntil, breathShown, breathUntil;

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
        Breath(new Rect(x + BarWidth + Gap, Margin, BarWidth, BarHeight));
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

    // The placeholder portrait: a framed square with the wizard's headwear over its face over its robe.
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
        var face = new Vector2(inner.x + w * 0.5f, inner.y + h * 0.48f);
        bool cowl = look != null && look.Headwear == WizardLook.HeadwearKind.Cowl;
        if (cowl) HudDraw.Disc(face + new Vector2(0f, -h * 0.03f), w * 0.31f, hat);
        HudDraw.Disc(face, w * 0.2f, FaceColor);
        Fill(new Rect(face.x - w * 0.09f, face.y - h * 0.07f, w * 0.03f, h * 0.15f), EyeColor);
        Fill(new Rect(face.x + w * 0.06f, face.y - h * 0.07f, w * 0.03f, h * 0.15f), EyeColor);
        if (cowl) return;
        Fill(new Rect(inner.x + w * 0.18f, inner.y + h * 0.3f, w * 0.64f, h * 0.06f), hat);
        Fill(new Rect(inner.x + w * 0.36f, inner.y + h * 0.14f, w * 0.28f, h * 0.17f), hat);
        Fill(new Rect(inner.x + w * 0.44f, inner.y + h * 0.02f, w * 0.12f, h * 0.13f), hat);
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

    // Breath while it runs down or fills again, fading in and out like stamina; drowning, the bar's empty and darker.
    void Breath(Rect r)
    {
        var breath = vitals.Breath;
        if (breath == null) return;
        if (breath.Under || !breath.Full) breathUntil = Time.unscaledTime + Linger;
        float target = Time.unscaledTime < breathUntil ? 1f : 0f;
        breathShown = Mathf.MoveTowards(breathShown, target, FadeRate * Time.unscaledDeltaTime);
        if (breathShown <= 0f) return;
        var colour = breath.Drowning ? DrowningColor : BreathColor;
        string label = breath.Drowning ? "<b>Drowning</b>" : $"<b>Breath</b> {Mathf.CeilToInt((float)breath.Left)} s";
        Bar(r, label, (float)breath.Share, colour, breathShown);
    }

    // Sprinting (the sprint held while moving), swimming or rolling.
    bool InUse()
    {
        if (surface != null && surface.Swimming) return true;
        if (roll != null && roll.Rolling) return true;
        if (sprint == null || move == null) return false;
        return (bool)sprint.GetValue(inputs) && ((Vector2)move.GetValue(inputs)).sqrMagnitude > 0.01f;
    }

    void Bar(Rect r, string label, float share, Color colour, float alpha = 1f)
    {
        Fill(r, new Color(0f, 0f, 0f, 0.55f * alpha));
        colour.a *= alpha;
        Fill(new Rect(r.x + 4f, r.y + 4f, (r.width - 8f) * Mathf.Clamp01(share), r.height - 8f), colour);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(r, label, text);
        GUI.color = Color.white;
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
