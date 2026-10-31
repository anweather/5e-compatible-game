// The element combos' data (rules/combos.json, docs/SPELL_COMBOS.md): what each
// loaded element, or ordered pair of them, casts, and the d-pad's list of the
// spells that have no element. The rule document holds it, so mods can patch it
// and the fingerprint covers it. World.Combos.cs runs it. Port of the combo
// types and checks in sandbox/src/rules.ts.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>What one element, or an ordered pair, casts.</summary>
    public sealed class ComboDef
    {
        /// <summary>"fire", or "fire+air" for fire then air.</summary>
        public string Id;
        public string Name;
        /// <summary>The spell it casts, which may stand in for one not made yet.</summary>
        public string Spell;
        public string Shape;
        /// <summary>The spell it casts when held to its full charge instead, or null.</summary>
        public string Held;
        /// <summary>Ticks the cast meter takes to drain.</summary>
        public int CastTicks;
        /// <summary>What's still to make, when <see cref="Spell"/> is a stand-in; null otherwise.</summary>
        public string Todo;

        /// <summary>The spell a cast of this charge goes off as: <see cref="Held"/> at <paramref name="maxCharge"/>, if it has one.</summary>
        public string SpellAt(int charge, int maxCharge) => Held != null && maxCharge > 1 && charge >= maxCharge ? Held : Spell;
    }

    /// <summary>A slot on the d-pad's list. With no <see cref="Spell"/> it shows but can't be cast.</summary>
    public sealed class DpadSpellDef
    {
        public string Name;
        public string Spell;
        public int CastTicks;
        public string Todo;
    }

    /// <summary>combos.json: the elements, the window for a second one, and what they cast.</summary>
    public sealed class CombosDef
    {
        readonly Dictionary<string, ComboDef> byId = new Dictionary<string, ComboDef>(StringComparer.Ordinal);

        public IReadOnlyList<string> Elements { get; internal set; } = Array.Empty<string>();
        /// <summary>Ticks after the first element in which a second is taken.</summary>
        public int Window { get; internal set; }
        /// <summary>Ticks each level of charge past the first adds to a cast.</summary>
        public int ChargeTicks { get; internal set; } = 1;
        /// <summary>What the view multiplies walking speed by while the cast meter drains.</summary>
        public double MoveFactor { get; internal set; } = 1;
        /// <summary>The same once a held cast is charging (PlayerCast.Charging): the wizard is braced.</summary>
        public double ChargeMoveFactor { get; internal set; } = 1;
        /// <summary>The d-pad's list, in cycling order.</summary>
        public IReadOnlyList<DpadSpellDef> Dpad { get; internal set; } = Array.Empty<DpadSpellDef>();
        /// <summary>Every combo: the singles, then the pairs, in the order of <see cref="Elements"/>.</summary>
        public IReadOnlyCollection<ComboDef> All => byId.Values;

        /// <summary>The id of an element alone (second null) or of an ordered pair.</summary>
        public static string IdOf(string first, string second) => second == null ? first : first + "+" + second;

        /// <summary>The combo with this id, or null.</summary>
        public ComboDef Combo(string id) => id != null && byId.TryGetValue(id, out var c) ? c : null;

        internal void Add(ComboDef c) => byId[c.Id] = c;
    }

    public enum ComboCommandKind
    {
        /// <summary>Loads <see cref="ComboCommand.Element"/>: the first, or the second inside the window.</summary>
        Load,
        /// <summary>Drops whatever is loaded; nothing casts.</summary>
        Cancel,
        /// <summary>Moves the d-pad selection by <see cref="ComboCommand.Step"/>, wrapping.</summary>
        Cycle,
        /// <summary>Starts casting the d-pad's selected spell.</summary>
        CastSelected,
        /// <summary>Lets go of a held element: the cast goes off at the charge its meter has reached.</summary>
        Release,
    }

    /// <summary>A player's cast input: what goes into the core for the button, stamped like a cast.</summary>
    public readonly struct ComboCommand
    {
        public ComboCommand(string player, ComboCommandKind kind, string element = null, int step = 0, bool hold = false)
        {
            Player = player;
            Kind = kind;
            Element = element;
            Step = step;
            Hold = hold;
        }

        public string Player { get; }
        public ComboCommandKind Kind { get; }
        public string Element { get; }
        public int Step { get; }
        /// <summary>For a <see cref="ComboCommandKind.Load"/>: the button is down, so the cast waits for a Release before it goes off.</summary>
        public bool Hold { get; }

        public static ComboCommand Load(string player, string element, bool hold = false) => new ComboCommand(player, ComboCommandKind.Load, element, hold: hold);
        public static ComboCommand Cancel(string player) => new ComboCommand(player, ComboCommandKind.Cancel);
        public static ComboCommand Cycle(string player, int step) => new ComboCommand(player, ComboCommandKind.Cycle, null, step);
        public static ComboCommand CastSelected(string player) => new ComboCommand(player, ComboCommandKind.CastSelected);
        public static ComboCommand Release(string player) => new ComboCommand(player, ComboCommandKind.Release);
    }

    /// <summary>
    /// A meter that reached zero this tick. The view aims it and sends the usual
    /// <see cref="CastCommand"/>, since where a spell lands is only known as it goes off.
    /// </summary>
    public readonly struct ComboFired
    {
        public ComboFired(string player, string combo, string name, string spell, string shape, bool discovered, int charge = 1)
        {
            Player = player;
            Combo = combo;
            Name = name;
            Spell = spell;
            Shape = shape;
            Discovered = discovered;
            Charge = charge;
        }

        public string Player { get; }
        /// <summary>The combo's id; null for a d-pad spell.</summary>
        public string Combo { get; }
        public string Name { get; }
        public string Spell { get; }
        public string Shape { get; }
        /// <summary>This was the player's first cast of the combo: it just joined the grimoire.</summary>
        public bool Discovered { get; }
        /// <summary>The strength it goes off at (CastCommand.Charge).</summary>
        public int Charge { get; }
    }

    /// <summary>One player's cast state and grimoire. Integers and ids only.</summary>
    public sealed class PlayerCast
    {
        public string Player;
        /// <summary>The loaded elements, in order; null for none.</summary>
        public string First, Second;
        /// <summary>Ticks until the cast goes off; 0 when not casting.</summary>
        public int Meter;
        /// <summary>The meter's full length, for the view's bar.</summary>
        public int Total;
        /// <summary>Ticks left to load a second element; 0 once it's shut.</summary>
        public int Window;
        /// <summary>The d-pad's selected slot.</summary>
        public int Slot;
        /// <summary>The d-pad slot being cast, or -1.</summary>
        public int DpadCasting = -1;
        /// <summary>The last element's button is down: the meter runs on through the charge, then stops at its last tick until a Release.</summary>
        public bool Holding;
        /// <summary>The ticks a full charge adds to <see cref="Meter"/> while held; 0 otherwise.</summary>
        public int Extra;
        /// <summary>The charge the Release gave; 1 until then.</summary>
        public int Charge = 1;
        /// <summary>The ids of the combos this player has cast: the grimoire.</summary>
        public SortedSet<string> Known { get; } = new SortedSet<string>(StringComparer.Ordinal);

        public bool Casting => Meter > 0;
        /// <summary>Held past a tap's length: the meter is counting charge now (World.HeldCharge), or waiting at its last tick.</summary>
        public bool Charging => Holding && Casting && Meter <= Extra + 1;
        /// <summary>A Load would be taken now: the first element, or the second inside the window.</summary>
        public bool TakesElement => !Casting || (First != null && Second == null && Window > 0);
    }

    public sealed partial class Rules
    {
        /// <summary>The combo table, empty when there is no combos.json.</summary>
        public CombosDef Combos { get; private set; } = new CombosDef();

        static void ParseCombos(Rules r, JObject t)
        {
            var c = new CombosDef
            {
                Elements = Strings(t["elements"]),
                Window = Int(t["window"]) ?? 0,
                ChargeTicks = Math.Max(1, Int(t["chargeTicks"]) ?? 1),
                MoveFactor = Num(t["moveFactor"]) ?? 1,
                ChargeMoveFactor = Num(t["chargeMoveFactor"]) ?? 1,
                Dpad = (t["dpad"] as JArray ?? new JArray()).OfType<JObject>().Select(d => new DpadSpellDef
                {
                    Name = Str(d["name"]),
                    Spell = Str(d["spell"]),
                    CastTicks = Int(d["castTicks"]) ?? 0,
                    Todo = Str(d["todo"]),
                }).ToList(),
            };
            foreach (var e in c.Elements)
                if (Entry(t["singles"], e) is JObject o) c.Add(ParseCombo(CombosDef.IdOf(e, null), o));
            foreach (var first in c.Elements)
                foreach (var second in c.Elements)
                    if (Entry(Entry(t["pairs"], first), second) is JObject o) c.Add(ParseCombo(CombosDef.IdOf(first, second), o));
            r.Combos = c;
        }

        // A table's entry, or null when the table or the entry isn't there.
        static JToken Entry(JToken table, string key) => (table as JObject)?[key];

        static ComboDef ParseCombo(string id, JObject o) => new ComboDef
        {
            Id = id,
            Name = Str(o["name"]),
            Spell = Str(o["spell"]),
            Shape = Str(o["shape"]),
            Held = Str(o["held"]),
            CastTicks = Int(o["castTicks"]) ?? 0,
            Todo = Str(o["todo"]),
        };

        sealed partial class Checker
        {
            /// <summary>Same messages as comboErrors in sandbox/src/rules.ts.</summary>
            public void Combos()
            {
                if (!(r["combos"] is JObject c) || c.Count == 0) return;
                var known = Strings(r["elements"]);
                var names = Strings(c["elements"]);
                foreach (var e in names.Where(e => !known.Contains(e))) Errors.Add($"combos: unknown element {e}");
                if (!WholeFromOne(c["window"])) Errors.Add("combos: window must be a whole number from 1");
                if (!WholeFromOne(c["chargeTicks"])) Errors.Add("combos: chargeTicks must be a whole number from 1");
                if (!(IsNumber(c["moveFactor"]) && (double)c["moveFactor"] > 0 && (double)c["moveFactor"] <= 1)) Errors.Add("combos: moveFactor must be above 0 and at most 1");
                if (!(IsNumber(c["chargeMoveFactor"]) && (double)c["chargeMoveFactor"] > 0 && (double)c["chargeMoveFactor"] <= 1)) Errors.Add("combos: chargeMoveFactor must be above 0 and at most 1");
                ComboSingles(c, names);
                foreach (var first in names)
                    foreach (var second in names)
                        ComboEntry(CombosDef.IdOf(first, second), Entry(Entry(c["pairs"], first), second));
                DpadEntries(c["dpad"] as JArray ?? new JArray());
            }

            void ComboSingles(JObject c, IReadOnlyList<string> names)
            {
                foreach (var e in names)
                {
                    var single = Entry(c["singles"], e);
                    ComboEntry(e, single);
                    var ticks = Entry(single, "castTicks");
                    if (ticks != null && c["window"] != null && IsWholeNumber(ticks) && IsWholeNumber(c["window"]) && (double)ticks < (double)c["window"])
                        Errors.Add($"combo {e}: castTicks must be at least the window");
                }
            }

            void DpadEntries(JArray dpad)
            {
                for (int i = 0; i < dpad.Count; i++)
                {
                    var d = dpad[i] as JObject ?? new JObject();
                    if (!Truthy(d["name"])) Errors.Add($"dpad {i}: needs a name");
                    if (d["spell"] != null && !SpellKnown(d["spell"])) Errors.Add($"dpad {i}: unknown spell {Show(d["spell"])}");
                    if (!WholeFromOne(d["castTicks"])) Errors.Add($"dpad {i}: castTicks must be a whole number from 1");
                }
            }

            void ComboEntry(string id, JToken e)
            {
                if (!(e is JObject))
                {
                    Errors.Add($"combo {id}: missing");
                    return;
                }
                if (!Truthy(e["name"])) Errors.Add($"combo {id}: needs a name");
                if (!SpellKnown(e["spell"])) Errors.Add($"combo {id}: unknown spell {Show(e["spell"])}");
                if (!Shapes.Contains(Text(e["shape"]) ?? "")) Errors.Add($"combo {id}: unknown shape {Show(e["shape"])}");
                if (!WholeFromOne(e["castTicks"])) Errors.Add($"combo {id}: castTicks must be a whole number from 1");
                if (e["held"] != null && !SpellKnown(e["held"])) Errors.Add($"combo {id}: held spell {Show(e["held"])} unknown");
            }

            bool SpellKnown(JToken id) => r["spells"].Any(s => Text(s["id"]) == Text(id));

            static bool WholeFromOne(JToken v) => v != null && IsWholeNumber(v) && (double)v >= 1;
        }
    }
}
