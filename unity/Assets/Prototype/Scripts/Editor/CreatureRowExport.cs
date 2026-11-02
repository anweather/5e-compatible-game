using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chem;
using Newtonsoft.Json.Linq;
using Punchy.Fauna;
using UnityEditor;
using UnityEngine;

// Writes Punchy's Editor assets out as rows, once: every species to rules/fauna.json
// and every limb to rules/parts.json, with the same numbers (CreatureRowTests proves
// the bodies match), the spawners' table as each row's "spawn" and Hunts as ids. Colours
// stay [r, g, b], since hex would round them. Run it to bring in new assets, and only
// over rows nobody has edited by hand: it writes the files whole.
public static class CreatureRowExport
{
    const string Folder = "Assets/Prototype/ThirdParty/Punchy/Fauna";

    // The table ChemYardBuilder held: species and how many per hectare, out in the wilds (fewer
    // round the start: ValleyFauna.homeDensity). Frogs and herons only by water. In this order.
    static readonly (string name, float density, bool nearWater)[] Spawners =
    {
        ("Deer", 0.7f, false), ("Rabbit", 1.8f, false), ("Squirrel", 1.5f, false), ("RedFox", 0.3f, false),
        ("Turkey", 0.6f, false), ("Quail", 0.8f, false), ("Marmot", 0.4f, false), ("Skunk", 0.2f, false),
        ("GarterSnake", 0.3f, false), ("BlackBear", 0.08f, false), ("Wolf", 0.1f, false),
        ("Frog", 4f, true), ("Heron", 0.4f, true),
    };

    [MenuItem("Prototype/Export Creature Rows")]
    public static void Export()
    {
        string rules = ChemGrid.FindRulesDir();
        File.WriteAllText(Path.Combine(rules, "fauna.json"), Fauna());
        File.WriteAllText(Path.Combine(rules, "parts.json"), Parts());
        Debug.Log($"Wrote the creature rows to {rules}");
    }

    /// <summary>Every species asset by its id, spawners first, then the rest by name.</summary>
    public static List<(string id, CreatureSpecies asset)> SpeciesAssets()
    {
        var all = Assets<CreatureSpecies>().ToDictionary(a => a.name);
        var order = Spawners.Select(s => s.name).Concat(all.Keys.Except(Spawners.Select(s => s.name)).OrderBy(n => n, System.StringComparer.Ordinal));
        return order.Select(n => (Id(n), all[n])).ToList();
    }

    static IEnumerable<T> Assets<T>() where T : ScriptableObject =>
        AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { Folder }).Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)));

    /// <summary>"RedFox" as an id: "red-fox".</summary>
    public static string Id(string assetName)
    {
        var id = new StringBuilder();
        foreach (char c in assetName)
        {
            if (char.IsUpper(c) && id.Length > 0) id.Append('-');
            id.Append(char.ToLowerInvariant(c));
        }
        return id.ToString();
    }

    // "RedFox" in words: "Red fox".
    static string Words(string assetName)
    {
        var words = Id(assetName).Replace('-', ' ');
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    // ---------- species ----------

    static string Fauna()
    {
        var rows = new List<string>();
        foreach (var (id, s) in SpeciesAssets())
        {
            var lines = new List<string> { $"\"name\": \"{Words(s.name)}\", " + (s.VariantOf != null ? $"\"variantOf\": \"{Id(s.VariantOf.name)}\", \"scale\": {Num(s.VariantScale)}" : $"\"size\": {Pair(s.Size)}") };
            if (s.VariantOf == null) lines.Add("\"body\": " + Inline(new JObject { ["length"] = F(s.BodyLength), ["radius"] = F(s.BodyRadius), ["taper"] = F(s.BodyTaper), ["flatten"] = F(s.BodyFlatten) }));
            if (s.Coats.Count == 0) lines.Add("\"coat\": " + Inline(Coat(null, s.Base, s.Accent, s.AccentGlow, 1f)));
            else lines.Add("\"coats\": " + Inline(new JArray(s.Coats.Select(c => Coat(c.Name, c.Base, c.Accent, c.AccentGlow, c.Weight)))));
            if (s.VariantOf == null)
            {
                lines.Add(Slots(s));
                lines.Add("\"move\": " + Inline(Move(s)));
            }
            lines.Add("\"behave\": " + Inline(Behave(s)));
            int spawner = System.Array.FindIndex(Spawners, x => x.name == s.name);
            if (spawner >= 0)
            {
                var spawn = new JObject { ["density"] = F(Spawners[spawner].density) };
                if (Spawners[spawner].nearWater) spawn["nearWater"] = true;
                lines.Add("\"spawn\": " + Inline(spawn));
            }
            rows.Add($"  \"{id}\": {{\n    {string.Join(",\n    ", lines)}\n  }}");
        }
        return "{\n" + string.Join(",\n", rows) + "\n}\n";
    }

    static JObject Coat(string name, Color a, Color b, float glow, float weight)
    {
        var c = new JObject();
        if (name != null) c["name"] = name;
        c["base"] = Rgb(a);
        c["accent"] = Rgb(b);
        if (glow != 0f) c["glow"] = F(glow);
        if (name != null && weight != 1f) c["weight"] = F(weight);
        return c;
    }

    // Slots by a name that reads: a leg by where it stands, anything else by its role, or a feature by
    // its limb; a second of a name takes a number. Rows keep the slots' order, which parents need.
    static string Slots(CreatureSpecies s)
    {
        var names = new string[s.Parts.Count];
        var lines = new List<string>();
        for (int i = 0; i < s.Parts.Count; i++)
        {
            var slot = s.Parts[i];
            if (slot.Limb == null) continue;
            names[i] = Unique(SlotName(slot), names);
            var row = new JObject { ["part"] = Id(slot.Limb.name) };
            bool onPart = slot.Parent >= 0 && slot.Parent < i && names[slot.Parent] != null;
            if (onPart) row["on"] = names[slot.Parent];
            if (slot.Socket != Vector3.zero) row["at"] = Vec(slot.Socket);
            if (slot.Direction != Vector3.zero) row["dir"] = Vec(slot.Direction);
            if (slot.Mirror) row["mirror"] = true;
            if (slot.Phase != 0f) row["phase"] = F(slot.Phase);
            if (slot.MirrorPhase != 0.5f) row["mirrorPhase"] = F(slot.MirrorPhase);
            if (slot.Scale != 1f) row["scale"] = F(slot.Scale);
            if (slot.Thickness != 1f) row["thickness"] = F(slot.Thickness);
            lines.Add($"      \"{names[i]}\": {Inline(row)}");
        }
        return "\"parts\": {\n" + string.Join(",\n", lines) + "\n    }";
    }

    static string SlotName(LimbSlot slot)
    {
        string many = slot.Mirror ? "s" : "";
        return slot.Limb.Role switch
        {
            LimbRole.Leg => slot.Socket.z > 0.2f ? "fore" : slot.Socket.z < -0.2f ? "hind" : "legs",
            LimbRole.Arm => "arm" + many,
            LimbRole.Ear => "ear" + many,
            LimbRole.Horn => "horn" + many,
            LimbRole.Eye => "eye" + many,
            LimbRole.Feature => Id(slot.Limb.name),
            _ => slot.Limb.Role.ToString().ToLowerInvariant(),
        };
    }

    static string Unique(string name, string[] taken)
    {
        string tried = name;
        for (int n = 2; taken.Contains(tried); n++) tried = $"{name}-{n}";
        return tried;
    }

    static JObject Move(CreatureSpecies s)
    {
        var m = new JObject { ["walk"] = F(s.WalkSpeed), ["run"] = F(s.RunSpeed), ["turn"] = F(s.TurnRate), ["bob"] = Vec(s.Bob), ["bobs"] = new JArray(s.BobsPerCycle.x, s.BobsPerCycle.y) };
        if (s.RunRock != 0f) m["rock"] = F(s.RunRock);
        if (s.Upright) m["upright"] = true;
        if (!s.Parts.Exists(p => p.Limb != null && p.Limb.Role == LimbRole.Leg && p.Parent < 0)) m["stride"] = Vec(s.LeglessStride);
        return m;
    }

    // A variant's behaviour comes from what it's based on, but for its activities, which are its own.
    static JObject Behave(CreatureSpecies s)
    {
        var b = new JObject();
        if (s.VariantOf == null)
        {
            b["wander"] = F(s.WanderRadius);
            b["idle"] = Vec(s.IdleTime);
            b["flee"] = F(s.FleeDistance);
            b["group"] = new JArray(s.GroupSize.x, s.GroupSize.y);
        }
        b["active"] = s.Active.ToString().ToLowerInvariant();
        b["graze"] = F(s.Grazing);
        b["thirst"] = F(s.Thirst);
        b["play"] = F(s.Playfulness);
        b["laze"] = F(s.Laziness);
        if (s.Hunts.Count > 0) b["hunts"] = new JArray(s.Hunts.Where(h => h != null).Select(h => Id(h.name)));
        b["hunger"] = F(s.Hunger);
        b["chase"] = F(s.ChaseTime);
        return b;
    }

    // ---------- parts ----------

    static string Parts()
    {
        var rows = new List<string>();
        foreach (var limb in Assets<LimbType>().OrderBy(l => l.name, System.StringComparer.Ordinal))
        {
            var lines = new List<string> { $"\"role\": \"{limb.Role.ToString().ToLowerInvariant()}\"" };
            if (limb.Spring != 0f) lines.Add($"\"spring\": {Num(limb.Spring)}");
            if (limb.JointPhaseStep != 0f) lines.Add($"\"ripple\": {Num(limb.JointPhaseStep)}");
            if (limb.FollowGround) lines.Add("\"follows\": \"ground\"");
            lines.Add("\"segments\": [\n      " + string.Join(",\n      ", limb.Segments.Select(s => Inline(Segment(s)))) + "\n    ]");
            var poses = new[] { limb.Idle, limb.WalkReach, limb.WalkPush, limb.WalkLift, limb.RunReach, limb.RunPush, limb.RunLift };
            var keyed = PartDef.PoseKeys.Select((k, i) => (k, poses[i])).Where(p => p.Item2.Joints.Count > 0).Select(p => $"\"{p.k}\": {Inline(new JArray(p.Item2.Joints.Select(Vec)))}");
            if (keyed.Any()) lines.Add("\"poses\": {\n      " + string.Join(",\n      ", keyed) + "\n    }");
            rows.Add($"  \"{Id(limb.name)}\": {{\n    {string.Join(",\n    ", lines)}\n  }}");
        }
        return "{\n" + string.Join(",\n", rows) + "\n}\n";
    }

    static JObject Segment(LimbSegment s)
    {
        var row = new JObject { ["length"] = F(s.Length), ["radii"] = new JArray(F(s.RadiusStart), F(s.RadiusEnd)) };
        if (s.Flatten != 1f) row["flatten"] = F(s.Flatten);
        if (s.Accent != 0f) row["accent"] = F(s.Accent);
        if (s.UseFixedColor) row["colour"] = Rgb(s.FixedColor);
        if (s.Glow != 0f) row["glow"] = F(s.Glow);
        return row;
    }

    // ---------- numbers and layout ----------

    // A float by its shortest digits, so 0.55 reads 0.55 and comes back the same float.
    static double F(float v) => double.Parse(v.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    static JArray Vec(Vector3 v) => new(F(v.x), F(v.y), F(v.z));
    static JArray Vec(Vector2 v) => new(F(v.x), F(v.y));
    static JArray Rgb(Color c) => new(F(c.r), F(c.g), F(c.b));
    static string Num(float v) => Inline(new JValue(F(v)));
    static string Pair(Vector2 v) => Inline(Vec(v));

    // One line, with the space the hand-written rules have.
    static string Inline(JToken t) => t switch
    {
        JObject o => "{ " + string.Join(", ", o.Properties().Select(p => $"\"{p.Name}\": {Inline(p.Value)}")) + " }",
        JArray a => "[" + string.Join(", ", a.Select(Inline)) + "]",
        JValue { Type: JTokenType.Float } v => ((double)v).ToString("R", CultureInfo.InvariantCulture),
        _ => t.ToString(Newtonsoft.Json.Formatting.None),
    };
}
