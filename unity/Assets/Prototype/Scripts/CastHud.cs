using Chem;
using UnityEngine;

// The cast on the HUD (EventLog draws it, docs/SPELL_COMBOS.md): about two thirds
// of the way down the screen, the loaded element orbs and the cast bar filling
// smoothly under them (the core's meter counts ticks; the bar runs on between them),
// with a step lit for each level a held cast has charged and the held spell's name
// once it's reached; and, bottom left and sized like the
// portrait's block, the d-pad cross: the selected spell on top, arrows either
// side, the grimoire's book below. Reads the core (World.CastOf); sends nothing.
public sealed class CastHud
{
    const float OrbRadius = 44f, OrbGap = 14f, MeterWidth = 260f, MeterHeight = 14f, StepHeight = 7f, StepGap = 4f;
    // How fast a charge step lights up (per second), and the bar's bright leading edge.
    const float StepRate = 9f, EdgeWidth = 3f;
    const float DownTheScreen = 2f / 3f;
    // The cross's three by three cells: a block about the size of VitalsHud's.
    const float Cell = 56f;

    static readonly Color Shade = new(0f, 0f, 0f, 0.55f), Meter = new(1f, 0.82f, 0.3f), Edge = new(1f, 0.96f, 0.8f), Glyph = new(1f, 1f, 1f, 0.9f);

    /// <summary>How full the cast bar was drawn last, 0 to 1, for the probe.</summary>
    public float Share { get; private set; }

    GUIStyle label, small, hint;
    int meterSeen, totalSeen;
    float meterAt;
    readonly float[] stepsLit = new float[World.MaxCharge];

    /// <summary>Draws both parts for <paramref name="caster"/>.</summary>
    public void Draw(SpellCaster caster, GUIStyle style)
    {
        label ??= new GUIStyle(style) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
        small ??= new GUIStyle(style) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        hint ??= new GUIStyle(style) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
        var world = caster.grid.World;
        var cast = world.CastOf(caster.CastPlayer);
        if (cast != null && cast.Casting) Loaded(world, cast, caster);
        Cross(world, caster);
    }

    void Loaded(World world, PlayerCast cast, SpellCaster caster)
    {
        float cx = HudScale.Width / 2f, y = HudScale.Height * DownTheScreen;
        if (cast.DpadCasting >= 0 && cast.DpadCasting < world.Rules.Combos.Dpad.Count)
            GUI.Label(new Rect(cx - 200f, y - 22f, 400f, 44f), $"<b>{world.Rules.Combos.Dpad[cast.DpadCasting].Name}</b>", small);
        else if (cast.Second != null || cast.Window > 0)
        {
            float x = OrbRadius + OrbGap / 2f;
            HudDraw.ElementOrb(new Vector2(cx - x, y), OrbRadius, cast.First);
            if (cast.Second != null) HudDraw.ElementOrb(new Vector2(cx + x, y), OrbRadius, cast.Second);
            else HudDraw.Ring(new Vector2(cx + x, y), OrbRadius - 3f, OrbRadius, 1f, new Color(1f, 1f, 1f, 0.45f));
        }
        else HudDraw.ElementOrb(new Vector2(cx, y), OrbRadius, cast.First);

        var meter = new Rect(cx - MeterWidth / 2f, y + OrbRadius + 12f, MeterWidth, MeterHeight);
        Fill(meter, Shade);
        Share = Progress(world, cast);
        float fill = (meter.width - 4f) * Share;
        Fill(new Rect(meter.x + 2f, meter.y + 2f, fill, meter.height - 4f), Meter);
        Fill(new Rect(meter.x + 2f + Mathf.Max(0f, fill - EdgeWidth), meter.y + 2f, Mathf.Min(fill, EdgeWidth), meter.height - 4f), Edge);
        Steps(world, cast, caster, meter, y - OrbRadius - 36f);
        bool pad = caster.controls != null && caster.controls.UsingGamepad;
        GUI.Label(new Rect(cx - 150f, meter.yMax + StepHeight + 6f, 300f, 22f), pad ? PadGlyphs.Text("{East} cancel") : "B cancel", hint);
    }

    // How far through the cast it is, 0 to 1: the ticks gone by, and the time since the last one.
    // The core drains its meter a few times a second, and a held cast waits on the last tick, so full.
    float Progress(World world, PlayerCast cast)
    {
        if (cast.Meter != meterSeen || cast.Total != totalSeen)
        {
            (meterSeen, totalSeen) = (cast.Meter, cast.Total);
            meterAt = Time.time;
        }
        float tick = Mathf.Clamp01((Time.time - meterAt) * world.Rules.Sim.TicksPerSecond);
        return Mathf.Clamp01((cast.Total - cast.Meter + tick) / Mathf.Max(1, cast.Total));
    }

    // The levels under the meter, from the second (a tap never shows them), and the held spell's name over the orbs.
    void Steps(World world, PlayerCast cast, SpellCaster caster, Rect meter, float nameY)
    {
        int charge = Mathf.Max(caster.HoldCharge, cast.Charge);
        if (charge < 2)
        {
            System.Array.Clear(stepsLit, 0, stepsLit.Length);
            return;
        }
        var combo = world.ComboOf(cast);
        int max = world.MaxChargeOf(combo);
        float width = (meter.width - StepGap * (max - 1)) / max;
        for (int i = 0; i < max && i < stepsLit.Length; i++)
        {
            stepsLit[i] = Mathf.MoveTowards(stepsLit[i], i < charge ? 1f : 0f, StepRate * Time.unscaledDeltaTime);
            Fill(new Rect(meter.x + i * (width + StepGap), meter.yMax + 3f, width, StepHeight), Color.Lerp(Shade, Meter, stepsLit[i]));
        }
        string spell = combo?.SpellAt(charge, max);
        if (spell != null && spell != combo.Spell) HeldName(world.Rules.Spell(spell)?.Name ?? spell, meter.center.x, nameY);
    }

    // The spell a full hold turns the cast into, on a plate of its own so it reads over anything.
    void HeldName(string name, float cx, float y)
    {
        float width = small.CalcSize(new GUIContent(name)).x + 28f;
        Fill(new Rect(cx - width / 2f, y + 2f, width, 28f), Shade);
        GUI.Label(new Rect(cx - 200f, y, 400f, 32f), $"<color=#ffd24a><b>{name}</b></color>", small);
    }

    void Cross(World world, SpellCaster caster)
    {
        float x = VitalsHud.Margin, y = HudScale.Height - VitalsHud.Margin - Cell * 3f;
        var top = Middle(x, y, 1, 0);
        var book = Middle(x, y, 1, 2);
        var left = Middle(x, y, 0, 1);
        var right = Middle(x, y, 2, 1);
        foreach (var at in new[] { top, book, left, right, Middle(x, y, 1, 1) })
            Fill(new Rect(at.x - Cell / 2f, at.y - Cell / 2f, Cell, Cell), Shade);
        float r = Cell * 0.3f;
        HudDraw.Triangle(left + new Vector2(r * 0.7f, -r), left + new Vector2(r * 0.7f, r), left + new Vector2(-r, 0f), Glyph);
        HudDraw.Triangle(right + new Vector2(-r * 0.7f, -r), right + new Vector2(-r * 0.7f, r), right + new Vector2(r, 0f), Glyph);
        Book(book);

        var spell = world.SelectedDpad(caster.CastPlayer);
        if (spell == null) return;
        var colour = spell.Spell != null ? new Color(0.62f, 0.4f, 0.95f) : new Color(0.4f, 0.4f, 0.45f);
        HudDraw.Disc(top, Cell * 0.42f, colour);
        GUI.Label(new Rect(top.x - Cell / 2f, top.y - Cell / 2f, Cell, Cell), $"<b>{Initials(spell.Name)}</b>", small);
        GUI.Label(new Rect(top.x + Cell * 0.7f, top.y - 22f, 300f, 44f), $"<b>{spell.Name}</b>", label);
    }

    static Vector2 Middle(float x, float y, int column, int row) => new(x + Cell * (column + 0.5f), y + Cell * (row + 0.5f));

    // The grimoire: covers and a spine.
    static void Book(Vector2 at)
    {
        float w = Cell * 0.26f, h = Cell * 0.32f;
        Fill(new Rect(at.x - w, at.y - h, w * 2f, h * 2f), new Color(0.62f, 0.4f, 0.95f));
        Fill(new Rect(at.x - 1.5f, at.y - h, 3f, h * 2f), Color.white);
        Fill(new Rect(at.x - w * 0.7f, at.y - h * 0.5f, w * 0.45f, 3f), Color.white);
        Fill(new Rect(at.x + w * 0.25f, at.y - h * 0.5f, w * 0.45f, 3f), Color.white);
    }

    static string Initials(string name)
    {
        var words = name.Split(' ');
        return words.Length > 1 ? $"{words[0][0]}{words[1][0]}" : name.Substring(0, Mathf.Min(2, name.Length));
    }

    static void Fill(Rect r, Color colour)
    {
        GUI.color = colour;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
