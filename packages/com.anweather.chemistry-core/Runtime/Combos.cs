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
    public sealed class ComboDef
    {
        public string Id;
        public string Name;
        public string Spell;
        public string Shape;
        public int CastTicks;
        public string Todo;

        public string SpellAt(int charge, int maxCharge) => Spell;
    }

    public sealed class DpadSpellDef
    {
        public string Name;
        public string Spell;
        public int CastTicks;
        public string Todo;
    }

    public sealed class CombosDef
    {
        readonly Dictionary<string, ComboDef> byId = new Dictionary<string, ComboDef>(StringComparer.Ordinal);

        public IReadOnlyList<string> Elements { get; internal set; } = Array.Empty<string>();
        public int Window { get; internal set; }
        public int ChargeTicks { get; internal set; } = 1;
        public double MoveFactor { get; internal set; } = 1;
        public double ChargeMoveFactor { get; internal set; } = 1;
        public IReadOnlyList<DpadSpellDef> Dpad { get; internal set; } = Array.Empty<DpadSpellDef>();
        public IReadOnlyCollection<ComboDef> All => byId.Values;

        public static string IdOf(string first, string second) => second == null ? first : first + "+" + second;

        public ComboDef Combo(string id) => id != null && byId.TryGetValue(id, out var c) ? c : null;

        internal void Add(ComboDef c) => byId[c.Id] = c;
    }

    public enum ComboCommandKind
    {
        Load,
        Cancel,
        Cycle,
        CastSelected,
        Release,
        Hand,
        Moment,
        Pick,
        Bring,
        Attune,
        HandDown,
        HandUp,
    }

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
        public bool Hold { get; }

        public bool TakenBy(bool devBuild) => Kind != ComboCommandKind.Moment || devBuild;

        public static ComboCommand Load(string player, string element, bool hold = false) => new ComboCommand(player, ComboCommandKind.Load, element, hold: hold);
        public static ComboCommand Cancel(string player) => new ComboCommand(player, ComboCommandKind.Cancel);
        public static ComboCommand Cycle(string player, int step) => new ComboCommand(player, ComboCommandKind.Cycle, null, step);
        public static ComboCommand CastSelected(string player) => new ComboCommand(player, ComboCommandKind.CastSelected);
        public static ComboCommand Release(string player) => new ComboCommand(player, ComboCommandKind.Release);
        public static ComboCommand Hand(string player, int hand, bool bumper = false) => new ComboCommand(player, ComboCommandKind.Hand, null, hand, bumper);
        public static ComboCommand Moment(string player, string moment) => new ComboCommand(player, ComboCommandKind.Moment, moment);
        public static ComboCommand Pick(string player, string upgrade) => new ComboCommand(player, ComboCommandKind.Pick, upgrade);
        public static ComboCommand Bring(string player, int points) => new ComboCommand(player, ComboCommandKind.Bring, null, points);
        public static ComboCommand HandDown(string player, int hand, bool bumper = false) => new ComboCommand(player, ComboCommandKind.HandDown, null, hand, bumper);
        public static ComboCommand HandUp(string player, int hand) => new ComboCommand(player, ComboCommandKind.HandUp, null, hand);
        public static ComboCommand Attune(string player, int hand, string element) => new ComboCommand(player, ComboCommandKind.Attune, element, hand);
    }

    public struct ComboFired
    {
        public ComboFired(string player, string combo, string name, string spell, string shape, bool discovered, int charge = 1)
        {
            Act = null;
            Range = 0;
            Player = player;
            Combo = combo;
            Name = name;
            Spell = spell;
            Shape = shape;
            Discovered = discovered;
            Charge = charge;
        }

        public string Player { get; }
        public string Combo { get; }
        public string Name { get; }
        public string Spell { get; }
        public string Shape { get; }
        public bool Discovered { get; }
        public int Charge { get; }
        public string Act { get; private set; }
        public int Range { get; private set; }

        public ComboFired FromHand(string act, int range)
        {
            var fired = this;
            fired.Act = act;
            fired.Range = range;
            return fired;
        }
    }

    public sealed class FiredCastsHeard
    {
        readonly List<ComboFired> taken = new List<ComboFired>();
        World world;
        int heard;

        public void StartFrom(World from)
        {
            world = from;
            heard = from.TickInProgress ? from.Tick - 1 : from.Tick;
        }

        public IReadOnlyList<ComboFired> Take(World from)
        {
            taken.Clear();
            if (from != world || from.Tick < heard) StartFrom(from);
            if (from.TickInProgress || from.Tick == heard) return taken;
            from.CopyCombosFiredSince(heard, taken);
            heard = from.Tick;
            return taken;
        }
    }

    public sealed class PlayerCast
    {
        public string Player;
        public string First, Second;
        public int Meter;
        public int Total;
        public int Window;
        public int Slot;
        public int DpadCasting = -1;
        public bool Holding;
        public int Extra;
        public int Charge = 1;
        public string Merge;
        public SortedSet<string> Known { get; } = new SortedSet<string>(StringComparer.Ordinal);

        public bool Casting => Meter > 0;
        public bool Charging => Holding && Casting && Meter <= Extra + 1;
        public bool TakesElement => !Casting || (First != null && Second == null && Window > 0);
    }

    public sealed partial class Rules
    {
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

        static JToken Entry(JToken table, string key) => (table as JObject)?[key];

        static ComboDef ParseCombo(string id, JObject o) => new ComboDef
        {
            Id = id,
            Name = Str(o["name"]),
            Spell = Str(o["spell"]),
            Shape = Str(o["shape"]),
            CastTicks = Int(o["castTicks"]) ?? 0,
            Todo = Str(o["todo"]),
        };

        sealed partial class Checker
        {
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
                if (e["held"] != null) Errors.Add($"combo {id}: held is gone: charge only makes a spell stronger, so give the stronger form a row of its own");
            }

            bool SpellKnown(JToken id) => r["spells"].Any(s => Text(s["id"]) == Text(id));

            static bool WholeFromOne(JToken v) => v != null && IsWholeNumber(v) && (double)v >= 1;
        }
    }
}
