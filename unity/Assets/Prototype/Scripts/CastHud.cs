using Chem;
using UnityEngine;

// The cast on the HUD (EventLog draws it, docs/SPELL_COMBOS.md): about two thirds
// of the way down the screen, under the stamina ring, the loaded element orbs and
// the cast meter draining under them; and, bottom left and sized like the
// portrait's block, the d-pad cross: the selected spell on top, arrows either
// side, the grimoire's book below. Reads the core (World.CastOf); sends nothing.
public sealed class CastHud
{
    const float OrbRadius = 44f, OrbGap = 14f, MeterWidth = 260f, MeterHeight = 16f;
    const float DownTheScreen = 2f / 3f;
    // The cross's three by three cells: a block about the size of VitalsHud's.
    const float Cell = 56f;

    static readonly Color Shade = new(0f, 0f, 0f, 0.55f), Meter = new(1f, 0.82f, 0.3f), Glyph = new(1f, 1f, 1f, 0.9f);

    GUIStyle label, small;

    /// <summary>Draws both parts for <paramref name="caster"/>.</summary>
    public void Draw(SpellCaster caster, GUIStyle style)
    {
        label ??= new GUIStyle(style) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
        small ??= new GUIStyle(style) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        var world = caster.grid.World;
        var cast = world.CastOf(caster.CastPlayer);
        if (cast != null && cast.Casting) Loaded(world, cast, caster.controls);
        Cross(world, caster);
    }

    void Loaded(World world, PlayerCast cast, PlayerControls controls)
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
        Fill(new Rect(meter.x + 2f, meter.y + 2f, (meter.width - 4f) * cast.Meter / Mathf.Max(1, cast.Total), meter.height - 4f), Meter);
        bool pad = controls != null && controls.UsingGamepad;
        GUI.Label(new Rect(cx - 150f, meter.yMax + 4f, 300f, 28f), pad ? PadGlyphs.Text("{East} cancel") : "B cancel", small);
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
