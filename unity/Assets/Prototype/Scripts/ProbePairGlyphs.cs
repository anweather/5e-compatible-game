using System.Collections;
using System.Collections.Generic;
using Chem;
using UnityEngine;

// The probe's pairglyphs scenario: every merged pair's icon (D16, unified-casting.md, "The merge") on one sheet, drawn by
// CastHud.PairIcon as the HUD draws it, with the spell's name under it, for a before and after with --shots. Each pair
// from the hands' table, both gestures: what both hands bring out together.
public class PairGlyphsProbe : ProbeScenario
{
    const int Columns = 6;
    const float Radius = 64f, CellWidth = 300f, CellHeight = 250f;

    /// <summary>Every merged spell in the hands' table: (left, right, spell), each pair of elements once, thrust then throw.</summary>
    public static List<(string left, string right, string spell)> Pairs(World world)
    {
        var hands = world.Rules.Sim.Hold.Hands;
        var elements = world.Rules.Combos.Elements;
        var pairs = new List<(string, string, string)>();
        for (int a = 0; a < elements.Count; a++)
            for (int b = a; b < elements.Count; b++)
                foreach (int gesture in new[] { HandsDef.Thrust, HandsDef.Throw, HandsDef.Sweep })
                    if (hands.Both(elements[a], elements[b], gesture)?.Spell is { } spell) pairs.Add((elements[a], elements[b], spell));
        return pairs;
    }

    sealed class Sheet : MonoBehaviour
    {
        public List<(string left, string right, string spell)> Pairs;
        public World World;
        GUIStyle name, inside;

        void OnGUI()
        {
            HudScale.Apply();
            name ??= new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.UpperCenter, richText = true };
            inside ??= new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, richText = true };
            int rows = (Pairs.Count + Columns - 1) / Columns;
            float left = (HudScale.Width - Columns * CellWidth) / 2f, top = (HudScale.Height - rows * CellHeight) / 2f;
            GUI.color = new Color(0.08f, 0.09f, 0.11f, 1f);
            GUI.DrawTexture(new Rect(0f, 0f, HudScale.Width, HudScale.Height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            for (int i = 0; i < Pairs.Count; i++)
            {
                var (l, r, spell) = Pairs[i];
                var at = new Vector2(left + (i % Columns + 0.5f) * CellWidth, top + (i / Columns) * CellHeight + Radius + 16f);
                string title = World.Rules.Spell(spell)?.Name ?? spell;
                CastHud.PairIcon(l, r, spell, title, at, Radius, inside);
                GUI.Label(new Rect(at.x - CellWidth / 2f, at.y + Radius + 10f, CellWidth, 30f), $"<b>{title}</b>", name);
                GUI.Label(new Rect(at.x - CellWidth / 2f, at.y + Radius + 40f, CellWidth, 30f), $"{l} + {r}", name);
            }
        }
    }

    protected override IEnumerator Run()
    {
        var pairs = Pairs(World);
        Metric("pairs", pairs.Count);
        Check("every_pair_found", pairs.Count >= 10, $"{pairs.Count} merged spells");
        var sheet = gameObject.AddComponent<Sheet>();
        sheet.Pairs = pairs;
        sheet.World = World;
        yield return Wait(0.5f);
        Shot("sheet");
        yield return Wait(0.5f);
        Destroy(sheet);
    }
}
