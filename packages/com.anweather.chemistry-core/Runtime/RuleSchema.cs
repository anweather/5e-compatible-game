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
    public enum RuleFieldType { Text, Number, Whole, Flag, Dice, Value, Verb, Reference, References, Components, Section, Colour, Look }

    public sealed class RuleField
    {
        RuleField(string name, RuleFieldType type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; }
        public RuleFieldType Type { get; }
        public double Min { get; private set; }
        public bool Above { get; private set; }
        public double Max { get; private set; } = double.MaxValue;
        public bool Ranged { get; private set; }
        public bool Required { get; private set; }
        public bool ModSets { get; private set; } = true;
        public IReadOnlyCollection<string> Verbs { get; private set; } = System.Array.Empty<string>();
        public string Table { get; private set; }
        public string Cap { get; private set; }
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

        public static RuleField Number(string name) => new RuleField(name, RuleFieldType.Number);

        public static RuleField Number(string name, double min, bool above = false, double max = double.MaxValue, bool required = false) =>
            new RuleField(name, RuleFieldType.Number) { Min = min, Above = above, Max = max, Ranged = true, Required = required };

        public static RuleField Whole(string name) => new RuleField(name, RuleFieldType.Whole);

        public static RuleField Whole(string name, double min, double max = double.MaxValue, bool required = false) =>
            new RuleField(name, RuleFieldType.Whole) { Min = min, Max = max, Ranged = true, Required = required };

        public RuleField CappedBy(string cap)
        {
            Cap = cap;
            return this;
        }

        public RuleField ShippedOnly()
        {
            ModSets = false;
            return this;
        }
    }

    public sealed class RuleSchema
    {
        public RuleSchema(params RuleField[] fields)
        {
            Fields = fields;
        }

        public IReadOnlyList<RuleField> Fields { get; }

        public IEnumerable<string> Names => Fields.Select(f => f.Name);

        public IEnumerable<string> ModNames => Fields.Where(f => f.ModSets).Select(f => f.Name);

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
            RuleField.Whole("strength"),
            RuleField.Section("breaks", new RuleSchema(
                RuleField.Number("speed", 0, above: true, required: true),
                RuleField.Section("spill", new RuleSchema(
                    RuleField.Reference("cover", "covers"),
                    RuleField.Number("radius", 0),
                    RuleField.Number("fire", 0))))),
            RuleField.Look("look"));

        public static readonly RuleSchema Crater = new RuleSchema(
            RuleField.Whole("depth", 1, required: true),
            RuleField.Whole("radius", 0),
            RuleField.Whole("strength", 1));

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
                RuleField.References("resist", "elements"),
                RuleField.Text("body"),
                RuleField.Whole("size", 1))),
            RuleField.Look("look"),
            RuleField.Flag("player").ShippedOnly());

        public static readonly RuleSchema Conditions = new RuleSchema(
            RuleField.References("cover", "covers"),
            RuleField.Value("fire"),
            RuleField.Number("fireBelow"),
            RuleField.Flag("water"),
            RuleField.Flag("frozen"),
            RuleField.Flag("melts"),
            RuleField.Flag("hot"),
            RuleField.Flag("freezes"),
            RuleField.Reference("gas", "gases"),
            RuleField.Text("liquid"),
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

        public static readonly RuleSchema Effects = new RuleSchema(
            RuleField.Value("ignite").CappedBy("effects.ignite"),
            RuleField.Number("boil").CappedBy("effects.boil"),
            RuleField.Flag("melt"),
            RuleField.Flag("freeze"),
            RuleField.Flag("extinguish"),
            RuleField.Flag("condense"),
            RuleField.Value("addWater").CappedBy("effects.addWater"),
            RuleField.Reference("liquid", "liquids"),
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

        public static readonly RuleSchema Liquids = new RuleSchema(
            RuleField.Text("name"),
            RuleField.Value("tags"),
            RuleField.Whole("thick", 0, max: 600),
            RuleField.Whole("heap", 2, max: 16),
            RuleField.Flag("soaks"),
            RuleField.Flag("floats"),
            RuleField.Flag("drowns"),
            RuleField.Reference("boils", "gases"),
            RuleField.Reference("touch", "elements"),
            RuleField.Value("hot"),
            RuleField.Value("freezes"),
            RuleField.Value("sets"),
            RuleField.Number("speed", 0, max: 1),
            RuleField.Text("mix"),
            RuleField.Look("look"),
            RuleField.Value("lapse"));

        public static readonly RuleSchema Reactions = new RuleSchema(
            RuleField.Text("id", required: true),
            RuleField.Reference("element", "elements"),
            RuleField.Section("if", Conditions),
            RuleField.Section("do", Effects),
            RuleField.Text("log"),
            RuleField.Flag("notable"),
            RuleField.Flag("stop"));

        public RuleField Field(string name) => Fields.FirstOrDefault(f => f.Name == name);

        public RuleSchema SectionOf(string name) => Field(name)?.Inside;
    }
}
