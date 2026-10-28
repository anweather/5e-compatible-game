using System.Reflection;
using UnityEngine;

// The wizard's vitals on the HUD (EventLog draws it), as Andrew asked after his
// playtest: a portrait at the top left (a placeholder: the wizard's hat, face
// and robe in its look's colours) with health and magic stacked under it,
// WoW-style; and stamina in the middle of the screen under the crosshair,
// shown only while it's in use (sprinting, swimming, rolling, or not yet full
// again) and faded out a moment after, BotW-style.
public sealed class VitalsHud
{
    const float Left = 16f, Top = 12f, PortraitSize = 64f, BarWidth = 200f, BarHeight = 20f, Gap = 4f;
    const float StaminaWidth = 140f, StaminaHeight = 10f, StaminaBelowCentre = 46f;
    // How fast stamina fades in and out (per second), and how long it stays after the effort stops (s).
    const float FadeRate = 4f, Linger = 0.8f;

    static readonly Color HealthColor = new(0.85f, 0.25f, 0.25f), StaminaColor = new(0.9f, 0.75f, 0.2f), MagicColor = new(0.35f, 0.55f, 1f);
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
        text ??= new GUIStyle(style) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        Portrait(new Rect(Left, Top, PortraitSize, PortraitSize));
        float y = Top + PortraitSize + Gap;
        Bar(new Rect(Left, y, BarWidth, BarHeight), $"<b>Health</b> {Mathf.CeilToInt(vitals.Health)}/{Mathf.CeilToInt(vitals.HealthMax)}", vitals.Health / vitals.HealthMax, HealthColor, 1f);
        y += BarHeight + Gap;
        Bar(new Rect(Left, y, BarWidth, BarHeight), $"<b>Magic</b> {Mathf.CeilToInt(vitals.Magic)}/{Mathf.CeilToInt(Vitals.MagicMax)}", vitals.Magic / Vitals.MagicMax, MagicColor, 1f);
        Stamina();
        return y + BarHeight - Top;
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
        var inner = new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f);
        Fill(inner, Color.Lerp(robe, Color.black, 0.4f));
        float w = inner.width, h = inner.height;
        Fill(new Rect(inner.x + w * 0.2f, inner.y + h * 0.62f, w * 0.6f, h * 0.38f), robe);
        Fill(new Rect(inner.x + w * 0.32f, inner.y + h * 0.36f, w * 0.36f, h * 0.28f), Skin);
        if (look != null && look.BeardLength > 0f) Fill(new Rect(inner.x + w * 0.36f, inner.y + h * 0.52f, w * 0.28f, h * 0.22f), Opaque(look.Beard));
        Fill(new Rect(inner.x + w * 0.18f, inner.y + h * 0.32f, w * 0.64f, h * 0.06f), hat);
        Fill(new Rect(inner.x + w * 0.36f, inner.y + h * 0.16f, w * 0.28f, h * 0.17f), hat);
        Fill(new Rect(inner.x + w * 0.44f, inner.y + h * 0.04f, w * 0.12f, h * 0.13f), hat);
    }

    // Stamina under the crosshair while it's in use, fading in and out.
    void Stamina()
    {
        if (InUse() || vitals.Stamina < Vitals.StaminaMax) inUseUntil = Time.unscaledTime + Linger;
        float target = Time.unscaledTime < inUseUntil ? 1f : 0f;
        staminaShown = Mathf.MoveTowards(staminaShown, target, FadeRate * Time.unscaledDeltaTime);
        if (staminaShown <= 0f) return;
        var r = new Rect((Screen.width - StaminaWidth) / 2f, Screen.height / 2f + StaminaBelowCentre, StaminaWidth, StaminaHeight);
        Bar(r, null, vitals.Stamina / Vitals.StaminaMax, StaminaColor, staminaShown);
    }

    // Sprinting (the sprint held while moving), swimming or rolling.
    bool InUse()
    {
        if (surface != null && surface.Swimming) return true;
        if (roll != null && roll.Rolling) return true;
        if (sprint == null || move == null) return false;
        return (bool)sprint.GetValue(inputs) && ((Vector2)move.GetValue(inputs)).sqrMagnitude > 0.01f;
    }

    void Bar(Rect r, string label, float share, Color colour, float alpha)
    {
        Fill(r, new Color(0f, 0f, 0f, 0.55f * alpha));
        colour.a = alpha;
        Fill(new Rect(r.x + 2f, r.y + 2f, (r.width - 4f) * Mathf.Clamp01(share), r.height - 4f), colour);
        if (label != null) GUI.Label(r, label, text);
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
