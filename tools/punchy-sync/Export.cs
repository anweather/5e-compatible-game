// Punchy's assets as our rows: the same mapping as the one-off CreatureRowExport that made
// rules/fauna.json and rules/parts.json at Punchy c52d9ff (git show f053183f^:unity/Assets/
// Prototype/Scripts/Editor/CreatureRowExport.cs), with the water life's habitats from f053183f.
// No "spawn": where a species comes out is ours.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace PunchySync
{
    public sealed class Exported
    {
        public readonly JsonObject Fauna = new(), Parts = new();
        /// <summary>By species id: upstream features we have no code for, so a new one is left out.</summary>
        public readonly Dictionary<string, List<string>> NeedsCode = new();
        /// <summary>By species id: upstream fields set that our rows don't carry (the animal works without them).</summary>
        public readonly Dictionary<string, List<string>> Extras = new();
        /// <summary>Part ids whose role our view hasn't got.</summary>
        public readonly HashSet<string> UnsupportedParts = new();
    }

    public static class Export
    {
        // LimbRole in Punchy's order; wing came after our port (flight).
        static readonly string[] Roles = { "leg", "arm", "tail", "neck", "head", "ear", "horn", "eye", "feature", "wing" };
        static readonly string[] Actives = { "day", "night", "always" };
        static readonly string[] PoseFields = { "Idle", "WalkReach", "WalkPush", "WalkLift", "RunReach", "RunPush", "RunLift" };
        static readonly string[] PoseKeys = { "idle", "walkReach", "walkPush", "walkLift", "runReach", "runPush", "runLift" };
        const int Leg = 0, Arm = 1, Ear = 5, Horn = 6, Eye = 7, Feature = 8, Wing = 9;

        // How one differs from the next (life-variety): a touch for animals, more for folk.
        static JsonObject AnimalVary() => new() { ["shade"] = 0.14, ["warm"] = 0.06, ["girth"] = 0.08 };
        static JsonObject FolkVary() => new() { ["shade"] = 0.1, ["warm"] = 0.05, ["girth"] = 0.14, ["mix"] = true };

        // Fields a species can't do without, that our animator and brain haven't got: one of these
        // set leaves a new species out (its code is ported by hand first, ThirdParty/Punchy/README.md).
        static readonly (string field, string what)[] Required =
        {
            ("Tunnels", "tunnelling under the ground"), ("Rooted", "rooted to the spot"), ("Haunts", "a ghost's drift"),
            ("LavaDweller", "living in lava"), ("Breaches", "breaching out of the water to fly"), ("Disguised", "passing for a plant"),
            ("DrawnToLight", "flitting round cave lights"), ("Hawks", "hawking over water"),
        };

        // Fields that give an animal more to do but that it works without; listed, not ported.
        static readonly (string field, string unset)[] Optional =
        {
            ("Gallop", "0"), ("Climbing", "0"), ("SpinsWebs", "0"), ("Burrows", "0"), ("CaveDweller", "0"), ("ChamberFolk", "0"),
            ("Blind", "0"), ("Wades", "0"), ("ShunsLight", "0"), ("Crawls", "0"), ("Roosts", "0"), ("Friendly", "0"),
            ("CrossesWater", "0"), ("Nests", "0"), ("DrivenOff", "0"), ("GuardKit", "0"), ("FleeGas", "0"), ("SeenFrom", "0"),
            ("Child", "0"), ("Soars", "0"), ("RaidsHives", "0"), ("Hooves", "0"), ("ClimbsAs", "0"), ("Voice", "0"),
            ("EmoteReaction", "0"), ("CaveAffinity", "0"), ("HuntAggression", "0"), ("Role", "0"), ("Idles", "2048"),
        };

        public static Exported Rows(Snapshot snap)
        {
            var e = new Exported();
            foreach (var limb in snap.Limbs)
            {
                var id = Id(limb.Name);
                e.Parts[id] = Part(limb.Fields);
                if ((int)limb.Fields.Num("Role", 0) == Wing) e.UnsupportedParts.Add(id);
            }
            foreach (var s in snap.Species)
            {
                var id = Id(s.Name);
                e.Fauna[id] = Species(snap, s);
                e.NeedsCode[id] = NeedsCode(snap, s);
                e.Extras[id] = Optional.Where(o => (s.Fields.Text(o.field) ?? o.unset) != o.unset).Select(o => o.field).ToList();
                if (s.Fields.Items("Calls").Count > 0) e.Extras[id].Add("Calls");
                if (s.Fields.Items("Regard").Count > 0) e.Extras[id].Add("Regard");
            }
            return e;
        }

        static List<string> NeedsCode(Snapshot snap, Asset s)
        {
            var f = s.Fields;
            var needs = Required.Where(r => f.Flag(r.field)).Select(r => r.what).ToList();
            if ((int)f.Num("Habitat", 0) == 3) needs.Add("flight");
            if (f.Items("Parts").OfType<Dictionary<string, object>>().Any(p => (int)(snap.Of(p.GetValueOrDefault("Limb"))?.Fields.Num("Role", 0) ?? 0) == Wing)) needs.Add("wings");
            if (snap.Of(f.GetValueOrDefault("VariantOf")) != null)
                foreach (var v in new[] { "VariantHead", "VariantThickness", "VariantLegs" })
                    if (f.Num(v, 1) != 1) needs.Add($"a variant's own {v.Substring(7).ToLowerInvariant()} ({v} {Num(f.Num(v, 1))})");
            return needs;
        }

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

        static string Words(string assetName)
        {
            var words = Id(assetName).Replace('-', ' ');
            return char.ToUpperInvariant(words[0]) + words[1..];
        }

        // ---------- species ----------

        static JsonObject Species(Snapshot snap, Asset asset)
        {
            var s = asset.Fields;
            var variantOf = snap.NameOf(s.GetValueOrDefault("VariantOf"));
            var row = new JsonObject { ["name"] = Words(asset.Name) };
            if (variantOf != null)
            {
                row["variantOf"] = Id(variantOf);
                row["scale"] = s.Num("VariantScale", 1);
            }
            else
            {
                row["size"] = Arr(s.Vec("Size", 0.9, 1.1));
                row["body"] = new JsonObject { ["length"] = s.Num("BodyLength", 0.8), ["radius"] = s.Num("BodyRadius", 0.15), ["taper"] = s.Num("BodyTaper", 1), ["flatten"] = s.Num("BodyFlatten", 1.1) };
            }
            var coats = s.Items("Coats").OfType<Dictionary<string, object>>().ToList();
            if (coats.Count == 0) row["coat"] = Coat(null, UnityYaml.Rgb(s.Sub("Base"), new[] { 0.55, 0.4, 0.25 }), UnityYaml.Rgb(s.Sub("Accent"), new[] { 0.9, 0.85, 0.75 }), s.Num("AccentGlow", 0), 1);
            else row["coats"] = new JsonArray(coats.Select(c => (JsonNode)Coat(c.Text("Name") ?? "Coat", UnityYaml.Rgb(c.Sub("Base"), new[] { 0.55, 0.4, 0.25 }), UnityYaml.Rgb(c.Sub("Accent"), new[] { 0.9, 0.85, 0.75 }), c.Num("AccentGlow", 0), c.Num("Weight", 1))).ToArray());
            var hair = s.Items("HairColors").OfType<Dictionary<string, object>>().ToList();
            if (hair.Count > 0) row["hair"] = new JsonArray(hair.Select(c => (JsonNode)Arr(UnityYaml.Rgb(c, new double[3]))).ToArray());
            bool upright = s.Flag("Upright");
            if (variantOf == null)
            {
                row["vary"] = upright ? FolkVary() : AnimalVary();
                row["parts"] = Slots(snap, s);
                row["move"] = Move(snap, s);
            }
            row["behave"] = Behave(snap, s, variantOf != null);
            return row;
        }

        static JsonObject Coat(string name, double[] a, double[] b, double glow, double weight)
        {
            var c = new JsonObject();
            if (name != null) c["name"] = name;
            c["base"] = Arr(a);
            c["accent"] = Arr(b);
            if (glow != 0) c["glow"] = glow;
            if (name != null && weight != 1) c["weight"] = weight;
            return c;
        }

        // A slot by a name that reads: a leg by where it stands, else by its role, or a feature by its
        // limb; a second of a name takes a number. Rows keep the slots' order, which parents need.
        static JsonObject Slots(Snapshot snap, Dictionary<string, object> s)
        {
            var slots = s.Items("Parts").OfType<Dictionary<string, object>>().ToList();
            var names = new string[slots.Count];
            var rows = new JsonObject();
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var limb = snap.Of(slot.GetValueOrDefault("Limb"));
                if (limb == null) continue;
                var at = slot.Vec("Socket", 0, 0, 0);
                var dir = slot.Vec("Direction", 0, 0, 0);
                names[i] = Unique(SlotName(limb, at, slot.Flag("Mirror")), names);
                var row = new JsonObject { ["part"] = Id(limb.Name) };
                int parent = (int)slot.Num("Parent", -1);
                if (parent >= 0 && parent < i && names[parent] != null) row["on"] = names[parent];
                if (at.Any(v => v != 0)) row["at"] = Arr(at);
                if (dir.Any(v => v != 0)) row["dir"] = Arr(dir);
                if (slot.Flag("Mirror")) row["mirror"] = true;
                if (slot.Num("Phase", 0) != 0) row["phase"] = slot.Num("Phase", 0);
                if (slot.Num("MirrorPhase", 0.5) != 0.5) row["mirrorPhase"] = slot.Num("MirrorPhase", 0.5);
                if (slot.Num("Scale", 1) != 1) row["scale"] = slot.Num("Scale", 1);
                if (slot.Num("Thickness", 1) != 1) row["thickness"] = slot.Num("Thickness", 1);
                if (slot.Num("StyleGroup", 0) != 0) row["style"] = (int)slot.Num("StyleGroup", 0);
                rows[names[i]] = row;
            }
            return rows;
        }

        static string SlotName(Asset limb, double[] at, bool mirror)
        {
            string many = mirror ? "s" : "";
            int role = (int)limb.Fields.Num("Role", 0);
            return role switch
            {
                // CreatureRowExport compared floats with 0.2f; the same as doubles on the asset's digits.
                Leg => at[2] > 0.2 ? "fore" : at[2] < -0.2 ? "hind" : "legs",
                Arm => "arm" + many,
                Ear => "ear" + many,
                Horn => "horn" + many,
                Eye => "eye" + many,
                Feature => Id(limb.Name),
                _ => role < Roles.Length ? Roles[role] : "part",
            };
        }

        static string Unique(string name, string[] taken)
        {
            string tried = name;
            for (int n = 2; taken.Contains(tried); n++) tried = $"{name}-{n}";
            return tried;
        }

        static JsonObject Move(Snapshot snap, Dictionary<string, object> s)
        {
            var bobs = s.Vec("BobsPerCycle", 2, 2);
            var m = new JsonObject { ["walk"] = s.Num("WalkSpeed", 1.4), ["run"] = s.Num("RunSpeed", 6), ["turn"] = s.Num("TurnRate", 140), ["bob"] = Arr(s.Vec("Bob", 0.02, 0.06)), ["bobs"] = Arr(bobs) };
            if (s.Num("RunRock", 0) != 0) m["rock"] = s.Num("RunRock", 0);
            if (s.Flag("Upright")) m["upright"] = true;
            int habitat = (int)s.Num("Habitat", 0);
            double leaps = s.Num("Leaps", 0);
            if (habitat == 1 || habitat == 2)
            {
                var habitats = new JsonArray(habitat == 1 ? "swims" : "floats");
                if (habitat == 1 && leaps > 0) habitats.Add("leaps");
                m["habitats"] = habitats;
                if (habitat == 1) m["depth"] = s.Num("SwimDepth", 0.6);
                if (habitat == 1 && leaps > 0) m["leaps"] = leaps;
            }
            bool legs = s.Items("Parts").OfType<Dictionary<string, object>>().Any(p => (int)p.Num("Parent", -1) < 0 && (int)(snap.Of(p.GetValueOrDefault("Limb"))?.Fields.Num("Role", -1) ?? -1) == Leg);
            if (!legs) m["stride"] = Arr(s.Vec("LeglessStride", 0.6, 0.9));
            return m;
        }

        // A variant's behaviour comes from what it's based on, but for its activities, which are its own.
        static JsonObject Behave(Snapshot snap, Dictionary<string, object> s, bool variant)
        {
            var b = new JsonObject();
            if (!variant)
            {
                b["wander"] = s.Num("WanderRadius", 25);
                b["idle"] = Arr(s.Vec("IdleTime", 2, 8));
                b["flee"] = s.Num("FleeDistance", 12);
                b["group"] = Arr(s.Vec("GroupSize", 1, 1));
            }
            int active = (int)s.Num("Active", 0);
            b["active"] = active < Actives.Length ? Actives[active] : "day";
            b["graze"] = s.Num("Grazing", 0);
            b["thirst"] = s.Num("Thirst", 0.3);
            b["play"] = s.Num("Playfulness", 0);
            b["laze"] = s.Num("Laziness", 0.25);
            var hunts = s.Items("Hunts").Select(snap.NameOf).Where(n => n != null).Select(Id).ToList();
            if (hunts.Count > 0) b["hunts"] = new JsonArray(hunts.Select(h => (JsonNode)h).ToArray());
            b["hunger"] = s.Num("Hunger", 0.3);
            b["chase"] = s.Num("ChaseTime", 10);
            return b;
        }

        // ---------- parts ----------

        static JsonObject Part(Dictionary<string, object> limb)
        {
            int role = (int)limb.Num("Role", 0);
            var row = new JsonObject { ["role"] = role < Roles.Length ? Roles[role] : role.ToString() };
            if (limb.Num("Spring", 0) != 0) row["spring"] = limb.Num("Spring", 0);
            if (limb.Num("JointPhaseStep", 0) != 0) row["ripple"] = limb.Num("JointPhaseStep", 0);
            if (limb.Flag("FollowGround")) row["follows"] = "ground";
            row["segments"] = new JsonArray(limb.Items("Segments").OfType<Dictionary<string, object>>().Select(s => (JsonNode)Segment(s)).ToArray());
            var poses = new JsonObject();
            for (int i = 0; i < PoseFields.Length; i++)
            {
                var joints = limb.Sub(PoseFields[i])?.Items("Joints").OfType<Dictionary<string, object>>().ToList();
                if (joints == null || joints.Count == 0) continue;
                poses[PoseKeys[i]] = new JsonArray(joints.Select(j => (JsonNode)Arr(new[] { j.Num("x", 0), j.Num("y", 0), j.Num("z", 0) })).ToArray());
            }
            if (poses.Count > 0) row["poses"] = poses;
            return row;
        }

        static JsonObject Segment(Dictionary<string, object> s)
        {
            var row = new JsonObject { ["length"] = s.Num("Length", 0.2), ["radii"] = Arr(new[] { s.Num("RadiusStart", 0.04), s.Num("RadiusEnd", 0.03) }) };
            if (s.Num("Flatten", 1) != 1) row["flatten"] = s.Num("Flatten", 1);
            if (s.Num("Accent", 0) != 0) row["accent"] = s.Num("Accent", 0);
            if (s.Flag("UseFixedColor")) row["colour"] = Arr(UnityYaml.Rgb(s.Sub("FixedColor"), new[] { 0.1, 0.08, 0.07 }));
            if (s.Num("Glow", 0) != 0) row["glow"] = s.Num("Glow", 0);
            if (s.Flag("Hair")) row["hair"] = true;
            return row;
        }

        static JsonArray Arr(double[] v) => new(v.Select(x => (JsonNode)x).ToArray());

        static string Num(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }
}
