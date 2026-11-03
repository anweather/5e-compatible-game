// What a rules table's fields are (cleanup item 7): each field's name, type, range and whether
// a mod may set it, written once and read by Rules.Check (the numbers it holds a table to),
// ModValidator (through Rules.WithMods, which runs those checks) and the rules MCP
// (RuleFields, which refuses a field the engine wouldn't read). One table so far: items.
// The rules that tie one field to another (use zoom needs a zoom above 1) stay in code.
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    /// <summary>What a field holds.</summary>
    public enum RuleFieldType { Text, Number, Whole, Verb, Reference, Components, Section, Colour, Look }

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
        /// <summary>A section's own fields.</summary>
        public RuleSchema Inside { get; private set; }

        public static RuleField Text(string name, bool required = false) => new RuleField(name, RuleFieldType.Text) { Required = required };
        public static RuleField Whole(string name) => new RuleField(name, RuleFieldType.Whole);
        public static RuleField Colour(string name) => new RuleField(name, RuleFieldType.Colour);
        public static RuleField Look(string name) => new RuleField(name, RuleFieldType.Look);
        public static RuleField Components(string name) => new RuleField(name, RuleFieldType.Components);
        public static RuleField Verb(string name, IReadOnlyCollection<string> verbs) => new RuleField(name, RuleFieldType.Verb) { Verbs = verbs };
        public static RuleField Reference(string name, string table) => new RuleField(name, RuleFieldType.Reference) { Table = table };
        public static RuleField Section(string name, RuleSchema inside) => new RuleField(name, RuleFieldType.Section) { Inside = inside };

        /// <summary>A number from <paramref name="min"/> (or above it), up to <paramref name="max"/>.</summary>
        public static RuleField Number(string name, double min, bool above = false, double max = double.MaxValue, bool required = false) =>
            new RuleField(name, RuleFieldType.Number) { Min = min, Above = above, Max = max, Ranged = true, Required = required };
    }

    /// <summary>A rules table's fields, in the order its messages list them.</summary>
    public sealed class RuleSchema
    {
        public RuleSchema(params RuleField[] fields)
        {
            Fields = fields;
        }

        public IReadOnlyList<RuleField> Fields { get; }

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

        /// <summary>The section called this, or null.</summary>
        public RuleSchema SectionOf(string name) => Fields.FirstOrDefault(f => f.Name == name && f.Type == RuleFieldType.Section)?.Inside;
    }
}
