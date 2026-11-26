// A wizard's hands, in the world and the Hold (D16, docs/design/unified-casting.md): rules/hold.json "hands". A hand is a slot,
// and the button is the gesture: what comes out is the table's row for what the hand holds. An empty hand is a row like any other, and nothing
// here knows what a slot can hold, so a later weapon is rows too. Nothing in code names a spell. World.Hands.cs runs it.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class HandRowDef
    {
        public string Spell, Act;
        public int CastTicks, Cooldown;
    }

    public sealed class HandsDef
    {
        public const int Left = 0, Right = 1, Count = 2;
        public const int Thrust = 0, Throw = 1, Sweep = 2, NoGesture = -1;
        public const string Empty = "empty";

        static readonly string[] GestureNames = { "thrust", "throw", "sweep" };

        readonly Dictionary<string, HandRowDef[]> rows = new Dictionary<string, HandRowDef[]>(StringComparer.Ordinal);
        readonly int[] range = new int[GestureNames.Length];
        readonly Dictionary<string, (string slot, int gesture)> bySpell = new Dictionary<string, (string, int)>(StringComparer.Ordinal);

        public IReadOnlyList<string> Start { get; private set; } = new string[Count];
        public int Window { get; private set; }
        public int HandCooldown { get; private set; }
        public int BothCooldown { get; private set; }
        public int TapTicks { get; private set; }
        public int TapCooldown { get; private set; }
        public int AttuneTicks { get; private set; }
        public bool Any => rows.Count > 0;
        public IReadOnlyList<DpadSpellDef> Kit { get; private set; } = Array.Empty<DpadSpellDef>();
        public IEnumerable<HandRowDef> All => rows.Values.SelectMany(r => r).Where(r => r != null);

        static string Slot(string holds) => holds ?? Empty;

        public HandRowDef One(string holds, int gesture) => rows.TryGetValue(Slot(holds), out var row) ? row[gesture] : null;

        public HandRowDef Both(string left, string right, int gesture)
        {
            string a = Slot(left), b = Slot(right);
            return rows.TryGetValue(CombosDef.IdOf(a, b), out var row) || rows.TryGetValue(CombosDef.IdOf(b, a), out row) ? row[gesture] : null;
        }

        public static int GestureOf(string name) => name == null ? NoGesture : Array.IndexOf(GestureNames, name);

        public static string GestureName(int gesture) => gesture >= 0 && gesture < GestureNames.Length ? GestureNames[gesture] : null;

        public (string slot, int gesture) Find(string spell) => spell != null && bySpell.TryGetValue(spell, out var at) ? at : (null, NoGesture);

        public int RangeOf(int gesture) => range[gesture];

        public static (int x, int y) Within(int fromX, int fromY, int toX, int toY, int range)
        {
            long dx = toX - fromX, dy = toY - fromY;
            if (range <= 0 || dx * dx + dy * dy <= (long)range * range) return (toX, toY);
            double scale = range / Math.Sqrt(dx * dx + dy * dy);
            return (fromX + (int)(dx * scale), fromY + (int)(dy * scale));
        }

        internal static HandsDef Parse(JToken t)
        {
            var h = new HandsDef();
            if (!(t is JObject o)) return h;
            h.Start = new[] { Holds(o["start"]?["left"]), Holds(o["start"]?["right"]) };
            h.Window = (int?)o["window"] ?? 0;
            h.HandCooldown = (int?)o["handCooldown"] ?? 0;
            h.BothCooldown = (int?)o["bothCooldown"] ?? 0;
            h.TapTicks = (int?)o["tapTicks"] ?? 0;
            h.TapCooldown = (int?)o["tapCooldown"] ?? 0;
            h.AttuneTicks = (int?)o["attuneTicks"] ?? 0;
            h.Kit = (o["kit"] as JArray ?? new JArray()).OfType<JObject>().Select(k => new DpadSpellDef { Name = (string)k["name"], Spell = (string)k["spell"], CastTicks = (int?)k["castTicks"] ?? 0 }).ToList();
            for (int g = 0; g < GestureNames.Length; g++)
                h.range[g] = (int?)o["range"]?[(string)o["rangeOf"]?[GestureNames[g]] ?? ""] ?? 0;
            foreach (var row in (o["spells"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                h.rows[row.Name] = GestureNames.Select(g => Row(row.Value[g])).ToArray();
                for (int g = 0; g < GestureNames.Length; g++)
                    if (h.rows[row.Name][g]?.Spell != null) h.bySpell[h.rows[row.Name][g].Spell] = (row.Name, g);
            }
            return h;
        }

        static string Holds(JToken t) => (string)t == Empty ? null : (string)t;

        static HandRowDef Row(JToken t) => t == null ? null : new HandRowDef { Spell = (string)t["spell"], Act = (string)t["act"], CastTicks = (int?)t["castTicks"] ?? 0, Cooldown = (int?)t["cooldown"] ?? 0 };

        internal static IReadOnlyList<string> Gestures => GestureNames;
    }

    public sealed class PlayerHands
    {
        public string Player;
        public readonly string[] Holds = new string[HandsDef.Count];
        public readonly int[] Ready = new int[HandsDef.Count];
        public readonly int[] Gesture = { HandsDef.NoGesture, HandsDef.NoGesture };
        public readonly int[] Meter = new int[HandsDef.Count];
        public readonly int[] Total = new int[HandsDef.Count];
        public readonly int[] Extra = new int[HandsDef.Count];
        public readonly bool[] Down = new bool[HandsDef.Count];
        public readonly int[] Charge = { 1, 1 };
        public readonly bool[] Tap = new bool[HandsDef.Count];
        public readonly int[] Since = new int[HandsDef.Count];
        public int AttuneHand;
        public string AttuneTo;
        public int AttuneLeft;

        public bool Casting => Gesture[HandsDef.Left] != HandsDef.NoGesture || Gesture[HandsDef.Right] != HandsDef.NoGesture;
        public bool Busy => Casting || AttuneLeft > 0;
    }

    public sealed partial class Rules
    {
        sealed partial class Checker
        {
            public void Hands()
            {
                if (!(r["hold"]?["hands"] is JObject h)) return;
                var spells = h["spells"] as JObject ?? new JObject();
                var slots = spells.Properties().Select(p => p.Name).Where(n => !n.Contains('+')).ToList();
                foreach (var side in new[] { "left", "right" })
                    if (!slots.Contains((string)h["start"]?[side] ?? "")) Errors.Add($"hands: start {side} must be in the table");
                foreach (var key in new[] { "window", "handCooldown", "bothCooldown", "tapTicks", "tapCooldown", "attuneTicks" })
                    if (!WholeFromOne(h[key])) Errors.Add($"hands: {key} must be a whole number from 1");
                foreach (var g in HandsDef.Gestures)
                    if (!WholeFromOne(h["range"]?[(string)h["rangeOf"]?[g] ?? ""])) Errors.Add($"hands: {g} has no range");
                foreach (var slot in slots)
                    foreach (var g in HandsDef.Gestures)
                        if (Entry(spells[slot], g) == null) Errors.Add($"hands: {slot} {g}: missing");
                foreach (var row in spells.Properties())
                    foreach (var g in HandsDef.Gestures)
                        if (Entry(row.Value, g) is JObject e) HandRow($"{row.Name} {g}", e, row.Name.Contains('+') ? 0 : (int?)h["window"] ?? 0);
                foreach (var k in (h["kit"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    if (!SpellKnown(k["spell"])) Errors.Add($"hands: kit: unknown spell {Show(k["spell"])}");
                    if (!WholeFromOne(k["castTicks"])) Errors.Add($"hands: kit {Show(k["spell"])}: castTicks must be a whole number from 1");
                }
            }

            void HandRow(string id, JObject row, int leastTicks)
            {
                if (row["act"] == null && !SpellKnown(row["spell"])) Errors.Add($"hands: {id}: unknown spell {Show(row["spell"])}");
                if (!WholeFromOne(row["castTicks"])) Errors.Add($"hands: {id}: castTicks must be a whole number from 1");
                else if ((int)row["castTicks"] < leastTicks) Errors.Add($"hands: {id}: castTicks must be at least the window");
                if (row["cooldown"] != null && !WholeFromOne(row["cooldown"])) Errors.Add($"hands: {id}: cooldown must be a whole number from 1");
            }
        }
    }
}
