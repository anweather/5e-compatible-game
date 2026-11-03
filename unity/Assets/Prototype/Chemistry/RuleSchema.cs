// What a rules table's fields are (cleanup item 7): each field's name, type, range and whether
// a mod may set it, written once and read by Rules.Check (the numbers it holds a table to),
// ModValidator (through Rules.WithMods, which runs those checks) and the rules MCP
// (RuleFields, which refuses a field the engine wouldn't read). So far: items, entities and
// reaction rules, whose conditions and effects are verb lists with budget caps (rules/budgets.json).
// The rules that tie one field to another (use zoom needs a zoom above 1) stay in code.
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    /// <summary>What a field holds. Value: more than one shape (a number or an id), checked in code.</summary>
    public enum RuleFieldType { Text, Number, Whole, Flag, Dice, Value, Verb, Reference, References, Components, Section, Colour, Look }

    /// <summary>One field of a rules table.</summary>
    public sealed class RuleField
    {
        RuleField(string name, RuleFieldType type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; }
        public RuleFieldType Type { get; }
        /// <summary>A number's lowest value, or the value it stays above when <see cref="Above"/>.</summary>
        public double Min { get; private set; }
        public bool Above { get; private set; }
        public double Max { get; private set; } = double.MaxValue;
        /// <summary>True when a number has a range the checks hold it to.</summary>
        public bool Ranged { get; private set; }
        /// <summary>True when the entry (or its section) must give it.</summary>
        public bool Required { get; private set; }
        /// <summary>False for a field only the shipped rules set.</summary>
        public bool ModSets { get; private set; } = true;
        /// <summary>A verb's words: each needs code.</summary>
        public IReadOnlyCollection<string> Verbs { get; private set; } = System.Array.Empty<string>();
        /// <summary>The table a reference names an entry of.</summary>
        public string Table { get; private set; }
        /// <summary>The budgets.json number a mod's value may not go over, or null.</summary>
        public string Cap { get; private set; }
        /// <summary>A section's own fields.</summary>
        public RuleSchema Inside { get; private set; }

        public static RuleField Text(string name, bool required = false) => new RuleField(name, RuleFieldType.Text) { Required = required };
        public static RuleField Flag(string name) => new RuleField(name, RuleFieldType.Flag);
        public static RuleField Dice(string name) => new RuleField(name, RuleFieldType.Dice);
        public static RuleField Value(string name) => new RuleField(name, RuleFieldType.Value);
        public static RuleField References(string name, string table) => new RuleField(name, RuleFieldType.References) { Table = table };
        public static RuleField Colour(string name) => new RuleField(name, RuleFieldType.Colour);
        public static RuleField Look(string name) => new RuleField(name, RuleFieldType.Look);
        public static RuleField Components(string name) => new RuleField(name, RuleFieldType.Components);
        public static RuleField Verb(string name, IReadOnlyCollection<string> verbs) => new RuleField(name, RuleFieldType.Verb) { Verbs = verbs };
        public static RuleField Reference(string name, string table) => new RuleField(name, RuleFieldType.Reference) { Table = table };
        public static RuleField Section(string name, RuleSchema inside) => new RuleField(name, RuleFieldType.Section) { Inside = inside };

        /// <summary>A number, any.</summary>
        public static RuleField Number(string name) => new RuleField(name, RuleFieldType.Number);

        /// <summary>A number from <paramref name="min"/> (or above it), up to <paramref name="max"/>.</summary>
        public static RuleField Number(string name, double min, bool above = false, double max = double.MaxValue, bool required = false) =>
            new RuleField(name, RuleFieldType.Number) { Min = min, Above = above, Max = max, Ranged = true, Required = required };

        public static RuleField Whole(string name) => new RuleField(name, RuleFieldType.Whole);

        /// <summary>A whole number from <paramref name="min"/>, up to <paramref name="max"/>.</summary>
        public static RuleField Whole(string name, double min, double max = double.MaxValue, bool required = false) =>
            new RuleField(name, RuleFieldType.Whole) { Min = min, Max = max, Ranged = true, Required = required };

        /// <summary>This field, which a mod's value may not take over the budget at <paramref name="cap"/>.</summary>
        public RuleField CappedBy(string cap)
        {
            Cap = cap;
            return this;
        }

        /// <summary>This field, set by the shipped rules only: a mod may not.</summary>
        public RuleField ShippedOnly()
        {
            ModSets = false;
            return this;
        }
    }

    /// <summary>A rules table's fields, in the order its messages list them.</summary>
    public sealed class RuleSchema
    {
        public RuleSchema(params RuleField[] fields)
        {
            Fields = fields;
        }

        public IReadOnlyList<RuleField> Fields { get; }

        /// <summary>Every field's name.</summary>
        public IEnumerable<string> Names => Fields.Select(f => f.Name);

        /// <summary>The names of the fields a mod may set.</summary>
        public IEnumerable<string> ModNames => Fields.Where(f => f.ModSets).Select(f => f.Name);

        /// <summary>items.json (Items.cs).</summary>
        public static readonly RuleSchema Items = new RuleSchema(
            RuleField.Text("name", required: true),
            RuleField.Reference("material", "terrain"),
            RuleField.Components("components"),
            RuleField.Verb("use", ItemUses.All),
            RuleField.Text("message"),
            RuleField.Text("srd"),
            RuleField.Number("mass", 0, above: true),
            RuleField.Section("light", new RuleSchema(
                RuleField.Number("bright", 0),
                RuleField.Number("dim", 0),
                RuleField.Number("cone", 0, max: 179),
                RuleField.Colour("color"))),
            RuleField.Number("zoom", 1),
            RuleField.Number("reach", 0),
            // From 1 only for use dig (Rules.CheckItems), so no range here.
            RuleField.Whole("strength"),
            RuleField.Section("breaks", new RuleSchema(
                RuleField.Number("speed", 0, above: true, required: true),
                RuleField.Section("spill", new RuleSchema(
                    RuleField.Reference("cover", "covers"),
                    RuleField.Number("radius", 0),
                    RuleField.Number("fire", 0))))),
            RuleField.Look("look"));

        /// <summary>The ground a blast digs out ("crater" on a spell or an entity's "explodes").</summary>
        public static readonly RuleSchema Crater = new RuleSchema(
            RuleField.Whole("depth", 1, required: true),
            RuleField.Whole("radius", 0),
            RuleField.Whole("strength", 1));

        /// <summary>entities.json (RuleTables.cs ParseEntity).</summary>
        public static readonly RuleSchema Entities = new RuleSchema(
            RuleField.Text("name"),
            RuleField.Reference("material", "terrain"),
            RuleField.Components("components"),
            RuleField.Whole("hp"),
            RuleField.Whole("mass"),
            RuleField.Whole("str", 1, max: 30),
            RuleField.Whole("ac", 1, max: 30),
            RuleField.Section("explodes", new RuleSchema(
                RuleField.Reference("cover", "covers"),
                RuleField.Whole("radius"),
                RuleField.Whole("fire"),
                RuleField.Whole("push"),
                RuleField.Dice("damage"),
                RuleField.Section("crater", Crater))),
            RuleField.Section("raider", new RuleSchema(
                RuleField.Whole("ticksPerSquare", 1),
                RuleField.References("weak", "elements"),
                RuleField.References("resist", "elements"))),
            RuleField.Look("look"),
            // The wizard: one entity the game spawns for each player.
            RuleField.Flag("player").ShippedOnly());

        /// <summary>A reaction rule's "if" (and a spell's "needs"): each condition needs code.</summary>
        public static readonly RuleSchema Conditions = new RuleSchema(
            RuleField.References("cover", "covers"),
            RuleField.Value("fire"),
            RuleField.Number("fireBelow"),
            RuleField.Flag("water"),
            RuleField.Flag("frozen"),
            RuleField.Flag("melts"),
            RuleField.Flag("hot"),
            RuleField.Reference("gas", "gases"),
            RuleField.Text("terrainTag"),
            RuleField.Text("notTerrainTag"),
            RuleField.Flag("conductive"),
            RuleField.Text("status"),
            RuleField.Flag("flammable"),
            RuleField.Components("has"),
            RuleField.Components("lacks"),
            RuleField.Number("power"),
            RuleField.Number("chance", 0),
            RuleField.Text("verb"));

        /// <summary>One effect of a reaction rule's "do": each effect is a verb that needs code.</summary>
        public static readonly RuleSchema Effects = new RuleSchema(
            RuleField.Value("ignite").CappedBy("effects.ignite"),
            RuleField.Number("boil").CappedBy("effects.boil"),
            RuleField.Flag("melt"),
            RuleField.Flag("freeze"),
            RuleField.Flag("extinguish"),
            RuleField.Flag("condense"),
            RuleField.Value("addWater").CappedBy("effects.addWater"),
            RuleField.Reference("setCover", "covers"),
            RuleField.Number("spreadCover").CappedBy("effects.spreadCover"),
            RuleField.Flag("break"),
            RuleField.Flag("heat"),
            RuleField.Flag("bake"),
            RuleField.Flag("meltCover"),
            RuleField.Flag("crust"),
            RuleField.Flag("thaw"),
            RuleField.Flag("shatter"),
            RuleField.Text("dissolve"),
            RuleField.Flag("scour"),
            RuleField.Flag("pushCover"),
            RuleField.Reference("setTerrain", "terrain"),
            RuleField.Reference("raise", "terrain"),
            RuleField.Number("conduct").CappedBy("effects.conduct"),
            RuleField.Flag("fan"),
            RuleField.Flag("pushGas"),
            RuleField.Flag("clearGas"),
            RuleField.Text("mark"),
            RuleField.Reference("gas", "gases"),
            RuleField.Number("n").CappedBy("effects.n"),
            RuleField.Value("status"),
            RuleField.Text("statusOf"),
            RuleField.Text("clear"),
            RuleField.Whole("damageMul"),
            RuleField.Reference("grow", "terrain"),
            RuleField.Value("ticks"),
            RuleField.Number("wave").CappedBy("effects.wave"));

        /// <summary>reactions.json's cell and entity rules.</summary>
        public static readonly RuleSchema Reactions = new RuleSchema(
            RuleField.Text("id", required: true),
            RuleField.Reference("element", "elements"),
            RuleField.Section("if", Conditions),
            RuleField.Section("do", Effects),
            RuleField.Text("log"),
            RuleField.Flag("notable"),
            RuleField.Flag("stop"));

        /// <summary>The field called this, or null.</summary>
        public RuleField Field(string name) => Fields.FirstOrDefault(f => f.Name == name);

        /// <summary>The section called this, or null.</summary>
        public RuleSchema SectionOf(string name) => Field(name)?.Inside;
    }
}
