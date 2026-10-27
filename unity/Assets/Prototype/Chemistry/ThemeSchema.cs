// The world-theme DSL (D11): what a theme file in world/themes/ may say, the
// validator that checks it, and the merge of its mix-ins. A theme is a list
// of goals for a continent: zones, mountains, lakes, rivers, roads,
// settlements, influences (soft look fields) and a start. Port of
// PocketTactics' src/core/world/theme/schema.ts, with the same messages.
// Places are compass words, continent fractions ({x, y} in [-1, 1], x east,
// y SOUTH) or exact cells ({cell: [x, y]}, PocketTactics' "tile"). Features
// stay JSON objects, as in the original, so a mix-in override is a plain
// key-by-key merge.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>A theme with its mix-ins merged in: what the blueprint solver reads.</summary>
    public sealed class Theme
    {
        public string Id = "", Name = "", Description = "";
        public int Revision = 1;
        public bool Mixin;
        public JObject Start = new JObject();
        public JObject Climate;
        public string Tracks = "sparse";
        /// <summary>Core regions across (1, 3 or 5) and fade regions around them (0, 1 or 2).</summary>
        public int CoreRegions = 3, FadeRegions = 1;
        public List<JObject> Features = new List<JObject>();

        public string StartSetting => (string)Start["setting"] ?? "meadow";
        public string StartSettlement => (string)Start["settlement"] ?? "town";
        public JObject Feature(string id) => Features.FirstOrDefault(f => (string)f["id"] == id);
    }

    public static class ThemeSchema
    {
        public static readonly string[] Compass = { "center", "north", "south", "east", "west", "northeast", "northwest", "southeast", "southwest" };
        public static readonly string[] Distances = { "near", "mid", "far" };
        public static readonly string[] SizeWords = { "small", "medium", "large", "huge" };
        public static readonly string[] Materials = { "water", "lava", "ice" };
        public static readonly string[] Meanders = { "none", "low", "medium", "high" };
        public static readonly string[] Widths = { "narrow", "medium", "wide" };
        public static readonly string[] Orientations = { "ew", "ns", "nesw", "nwse" };
        public static readonly string[] StartSettings = { "meadow", "field", "desert", "forest", "riverbank", "lakeshore", "tundra", "ashland" };
        public static readonly string[] HeartTerrains = { "water", "grass", "forest", "sand", "rock", "burned", "rubble", "ice" };
        public static readonly string[] PatchSizes = { "small", "medium", "large" };

        static readonly string[] PlacedKeys = { "id", "kind", "at", "distance", "size", "stretch", "orientation", "near", "farFrom", "between" };
        static readonly Dictionary<string, string[]> Keys = new Dictionary<string, string[]>
        {
            ["zone"] = PlacedKeys.Concat(new[] { "biome" }).ToArray(),
            ["mountains"] = PlacedKeys.Concat(new[] { "peaks" }).ToArray(),
            ["lake"] = PlacedKeys.Concat(new[] { "material", "island" }).ToArray(),
            ["river"] = new[] { "id", "kind", "from", "to", "meander", "width", "material", "through" },
            ["road"] = new[] { "id", "kind", "from", "to", "rank", "via", "branches" },
            ["city"] = PlacedKeys.Concat(new[] { "count", "on", "minSpacing" }).ToArray(),
            ["town"] = PlacedKeys.Concat(new[] { "count", "on", "minSpacing" }).ToArray(),
            ["influence"] = PlacedKeys.Concat(new[] { "look", "area", "around", "along", "reach", "strength", "falloff", "edge", "patches", "heart", "onto" }).ToArray(),
        };
        static readonly string[] ThemeKeys = { "$schema", "id", "name", "description", "revision", "mixin", "mixins", "continent", "climate", "tracks", "start", "features" };
        static readonly Regex KebabId = new Regex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        static bool OneOf(string[] list, JToken v) => v != null && v.Type == JTokenType.String && list.Contains((string)v);
        static bool OneOf(IReadOnlyCollection<string> list, JToken v) => v != null && v.Type == JTokenType.String && list.Contains((string)v);
        static bool IsNumber(JToken v) => v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float);
        static bool IsInt(JToken v) => v != null && v.Type == JTokenType.Integer;
        static bool Num(JToken v, double lo, double hi) => IsNumber(v) && (double)v >= lo && (double)v <= hi;
        static bool IsStringList(JToken v) => v is JArray a && a.All(x => x.Type == JTokenType.String);

        static void CheckPlace(JToken v, string path, List<string> errors)
        {
            if (OneOf(Compass, v)) return;
            if (v is JObject o && o["cell"] != null)
            {
                if (!(o["cell"] is JArray t) || t.Count != 2 || !t.All(IsInt)) errors.Add($"{path}.cell must be [x, y] integers");
                if (o.Count != 1) errors.Add($"{path}: a cell place has only \"cell\"");
                return;
            }
            if (v is JObject f && IsNumber(f["x"]) && IsNumber(f["y"]))
            {
                if (Math.Abs((double)f["x"]) > 1.6 || Math.Abs((double)f["y"]) > 1.6) errors.Add($"{path}: fractions must be within [-1.6, 1.6] (the core is [-1, 1])");
                if (f.Count != 2) errors.Add($"{path}: a fraction place has only \"x\" and \"y\"");
                return;
            }
            errors.Add($"{path} must be a compass word ({string.Join(", ", Compass)}), {{x, y}} fractions, or {{cell: [x, y]}}");
        }

        static void CheckSize(JToken v, string path, List<string> errors)
        {
            if (v == null || OneOf(SizeWords, v)) return;
            if (Num(v, 0.02, 1)) return;
            errors.Add($"{path} must be {string.Join("/", SizeWords)} or a number in [0.02, 1]");
        }

        static void CheckRelations(JObject f, string path, List<string> errors)
        {
            if (f["near"] != null && f["near"].Type != JTokenType.String) errors.Add($"{path}.near must be a feature id");
            var far = f["farFrom"];
            if (far != null)
            {
                bool ok = far.Type == JTokenType.String
                    || (far is JObject fo && fo["id"]?.Type == JTokenType.String && (fo["min"] == null || (IsNumber(fo["min"]) && (double)fo["min"] > 0 && (double)fo["min"] <= 2)));
                if (!ok) errors.Add($"{path}.farFrom must be an id or {{id, min}}");
            }
            if (f["between"] != null && !(f["between"] is JArray b && b.Count == 2 && b.All(s => s.Type == JTokenType.String)))
                errors.Add($"{path}.between must be [id, id]");
        }

        /// <summary>Structural check of one theme FILE (before mix-ins are merged); <paramref name="looks"/> are the biome ids a zone or influence may name.</summary>
        public static List<string> ValidateFile(JToken json, IReadOnlyCollection<string> looks)
        {
            var errors = new List<string>();
            if (!(json is JObject t)) return new List<string> { "theme must be an object" };
            CheckTop(t, looks, errors);
            CheckFeatures(t, looks, errors);
            return errors;
        }

        static void CheckTop(JObject t, IReadOnlyCollection<string> looks, List<string> errors)
        {
            foreach (var p in t.Properties()) if (!ThemeKeys.Contains(p.Name)) errors.Add($"unknown key \"{p.Name}\"");
            if (t["id"]?.Type != JTokenType.String || !KebabId.IsMatch((string)t["id"])) errors.Add("id must be kebab-case");
            if (t["name"]?.Type != JTokenType.String || string.IsNullOrEmpty((string)t["name"])) errors.Add("name is required");
            if (!IsInt(t["revision"]) || (int)t["revision"] < 1) errors.Add("revision must be an integer >= 1");
            if (t["mixins"] != null && !IsStringList(t["mixins"])) errors.Add("mixins must be a list of theme ids");
            CheckContinent(t["continent"], errors);
            CheckClimate(t["climate"], errors);
            if (t["tracks"] != null && !OneOf(new[] { "dense", "normal", "sparse" }, t["tracks"])) errors.Add("tracks must be dense/normal/sparse");
            CheckStart(t["start"], looks, errors);
            if (t["features"] != null && !(t["features"] is JArray)) errors.Add("features must be a list");
        }

        static void CheckFeatures(JObject t, IReadOnlyCollection<string> looks, List<string> errors)
        {
            var features = t["features"] as JArray ?? new JArray();
            var ids = new HashSet<string>();
            var allKeys = new HashSet<string>(Keys.Values.SelectMany(k => k));
            bool hasMixins = t["mixins"] is JArray m && m.Count > 0;
            for (int i = 0; i < features.Count; i++)
            {
                string path = $"features[{i}]";
                if (features[i] is JObject raw) CheckFeatureEntry(raw, path, (ids, allKeys, hasMixins), looks, errors);
                else errors.Add($"{path} must be an object");
            }
        }

        static void CheckFeatureEntry(JObject raw, string path, (HashSet<string> ids, HashSet<string> allKeys, bool hasMixins) seen, IReadOnlyCollection<string> looks, List<string> errors)
        {
            var id = raw["id"];
            if (id?.Type != JTokenType.String || string.IsNullOrEmpty((string)id)) errors.Add($"{path}.id is required");
            else if (!seen.ids.Add((string)id)) errors.Add($"{path}: duplicate id \"{(string)id}\"");
            // No kind = an OVERRIDE of a mix-in's feature with the same id; checked once merged (Validate).
            if (raw["kind"] == null && seen.hasMixins)
            {
                foreach (var p in raw.Properties())
                    if (!seen.allKeys.Contains(p.Name)) errors.Add($"{path} ({(string)id}): unknown key \"{p.Name}\"");
                return;
            }
            CheckFeature(raw, path, looks, errors);
        }

        static void CheckContinent(JToken c, List<string> errors)
        {
            if (c == null) return;
            if (!(c is JObject co))
            {
                errors.Add("continent must be an object");
                return;
            }
            if (co["coreRegions"] != null && !new[] { 1, 3, 5 }.Contains(IsInt(co["coreRegions"]) ? (int)co["coreRegions"] : 0)) errors.Add("continent.coreRegions must be 1, 3 or 5");
            if (co["fadeRegions"] != null && !new[] { 0, 1, 2 }.Contains(IsInt(co["fadeRegions"]) ? (int)co["fadeRegions"] : -1)) errors.Add("continent.fadeRegions must be 0, 1 or 2");
        }

        static void CheckClimate(JToken c, List<string> errors)
        {
            if (c == null) return;
            if (!(c is JObject co))
            {
                errors.Add("climate must be an object");
                return;
            }
            foreach (var p in co.Properties())
            {
                if (p.Name == "temperate")
                {
                    if (!Num(p.Value, 0, 1)) errors.Add("climate.temperate must be a number in [0, 1]");
                }
                else if (p.Name != "temperature" && p.Name != "moisture") errors.Add($"climate.{p.Name}: unknown key");
                else if (!Num(p.Value, -0.3, 0.3)) errors.Add($"climate.{p.Name} must be a number in [-0.3, 0.3]");
            }
        }

        static void CheckStart(JToken s, IReadOnlyCollection<string> looks, List<string> errors)
        {
            if (s == null) return;
            if (!(s is JObject so))
            {
                errors.Add("start must be an object");
                return;
            }
            foreach (var p in so.Properties())
                if (p.Name != "setting" && p.Name != "settlement" && p.Name != "biome") errors.Add($"start.{p.Name}: unknown key");
            CheckStartValues(so, looks, errors);
        }

        static void CheckStartValues(JObject so, IReadOnlyCollection<string> looks, List<string> errors)
        {
            if (so["biome"] != null && !OneOf(looks, so["biome"])) errors.Add($"start.biome must be a world biome ({string.Join(", ", looks)})");
            if (!OneOf(StartSettings, so["setting"])) errors.Add($"start.setting must be one of {string.Join(", ", StartSettings)}");
            if (so["settlement"] != null && !OneOf(new[] { "town", "city" }, so["settlement"])) errors.Add("start.settlement must be \"town\" or \"city\"");
        }

        static void CheckFeature(JObject f, string path, IReadOnlyCollection<string> looks, List<string> errors)
        {
            string kind = (string)f["kind"];
            if (kind == null || !Keys.ContainsKey(kind))
            {
                errors.Add($"{path}.kind must be one of {string.Join(", ", Keys.Keys)}");
                return;
            }
            string p = $"{path} ({(string)f["id"] ?? kind})";
            CheckShapeKeys(f, kind, p, errors);
            CheckKind(kind, f, p, looks, errors);
            if (kind == "river" || kind == "road") return;
            if (kind == "influence" && f["at"] == null)
            {
                CheckRelations(f, p, errors);
                return;
            }
            if (f["at"] != null) CheckPlace(f["at"], $"{p}.at", errors);
            CheckRelations(f, p, errors);
        }

        static void CheckShapeKeys(JObject f, string kind, string p, List<string> errors)
        {
            foreach (var prop in f.Properties()) if (!Keys[kind].Contains(prop.Name)) errors.Add($"{p}: unknown key \"{prop.Name}\" for a {kind}");
            if (f["distance"] != null && !OneOf(Distances, f["distance"])) errors.Add($"{p}.distance must be {string.Join("/", Distances)}");
            if (f["orientation"] != null && !OneOf(Orientations, f["orientation"])) errors.Add($"{p}.orientation must be {string.Join("/", Orientations)}");
            if (f["stretch"] != null && !Num(f["stretch"], 1, 8)) errors.Add($"{p}.stretch must be a number in [1, 8]");
            CheckSize(f["size"], $"{p}.size", errors);
        }

        static void CheckKind(string kind, JObject f, string p, IReadOnlyCollection<string> looks, List<string> errors)
        {
            switch (kind)
            {
                case "zone":
                    if (!OneOf(looks, f["biome"])) errors.Add($"{p}.biome must be one of {string.Join(", ", looks)}");
                    if (f["at"] == null) errors.Add($"{p}.at is required");
                    break;
                case "mountains":
                    if (f["at"] == null) errors.Add($"{p}.at is required");
                    if (f["peaks"] != null && f["peaks"].Type != JTokenType.Boolean) errors.Add($"{p}.peaks must be true/false");
                    break;
                case "lake":
                    CheckLake(f, p, looks, errors);
                    break;
                case "river":
                    CheckRiver(f, p, errors);
                    break;
                case "road":
                    CheckRoad(f, p, errors);
                    break;
                case "influence":
                    CheckInfluence(f, p, looks, errors);
                    break;
                default: // city, town
                    CheckSettlement(f, p, errors);
                    break;
            }
        }

        static void CheckRiver(JObject f, string p, List<string> errors)
        {
            CheckPlace(f["from"], $"{p}.from", errors);
            CheckPlace(f["to"], $"{p}.to", errors);
            if (f["meander"] != null && !OneOf(Meanders, f["meander"])) errors.Add($"{p}.meander must be {string.Join("/", Meanders)}");
            if (f["width"] != null && !OneOf(Widths, f["width"])) errors.Add($"{p}.width must be {string.Join("/", Widths)}");
            if (f["material"] != null && !OneOf(Materials, f["material"])) errors.Add($"{p}.material must be {string.Join("/", Materials)}");
            if (f["through"] != null && !IsStringList(f["through"])) errors.Add($"{p}.through must be a list of lake ids");
        }

        static void CheckSettlement(JObject f, string p, List<string> errors)
        {
            if (f["count"] != null && !(IsInt(f["count"]) && (int)f["count"] >= 1 && (int)f["count"] <= 16)) errors.Add($"{p}.count must be 1..16");
            if (f["on"] != null && f["on"].Type != JTokenType.String) errors.Add($"{p}.on must be a road id");
            if (f["minSpacing"] != null && !Num(f["minSpacing"], double.Epsilon, 1.5)) errors.Add($"{p}.minSpacing must be in (0, 1.5]");
        }

        static void CheckLake(JObject f, string p, IReadOnlyCollection<string> looks, List<string> errors)
        {
            if (f["at"] == null) errors.Add($"{p}.at is required");
            if (f["material"] != null && !OneOf(Materials, f["material"])) errors.Add($"{p}.material must be {string.Join("/", Materials)}");
            var island = f["island"];
            if (island == null || island.Type == JTokenType.Boolean) return;
            if (!(island is JObject io))
            {
                errors.Add($"{p}.island must be true/false or {{size, biome}}");
                return;
            }
            CheckSize(io["size"], $"{p}.island.size", errors);
            if (io["biome"] != null && !OneOf(looks, io["biome"])) errors.Add($"{p}.island.biome must be a zone biome");
        }

        static void CheckRoad(JObject f, string p, List<string> errors)
        {
            if (!OneOf(Compass, f["from"]) || (string)f["from"] == "center") errors.Add($"{p}.from must be a compass side");
            if (!OneOf(Compass, f["to"]) || (string)f["to"] == "center") errors.Add($"{p}.to must be a compass side");
            if (f["from"] != null && JToken.DeepEquals(f["from"], f["to"])) errors.Add($"{p}: from and to must differ");
            if (f["rank"] != null && !OneOf(new[] { "main", "minor" }, f["rank"])) errors.Add($"{p}.rank must be main/minor");
            if (f["via"] != null && !IsStringList(f["via"])) errors.Add($"{p}.via must be a list of ids");
            if (f["branches"] != null && !(IsInt(f["branches"]) && (int)f["branches"] >= 0 && (int)f["branches"] <= 12)) errors.Add($"{p}.branches must be 0..12");
        }

        static void CheckInfluence(JObject f, string p, IReadOnlyCollection<string> looks, List<string> errors)
        {
            if (!OneOf(looks, f["look"])) errors.Add($"{p}.look must be a world biome ({string.Join(", ", looks)})");
            CheckInfluenceArea(f, p, errors);
            CheckInfluenceNumbers(f, p, errors);
            CheckPatches(f["patches"], p, errors);
            CheckHeart(f["heart"], p, errors);
            CheckOnto(f["onto"], p, looks, errors);
        }

        static void CheckInfluenceArea(JObject f, string p, List<string> errors)
        {
            var areas = new[] { "area", "at", "around", "along" }.Where(k => f[k] != null).ToList();
            if (areas.Count != 1)
                errors.Add($"{p}: an influence needs exactly one area: area \"continent\", at, around or along (got {(areas.Count > 0 ? string.Join(", ", areas) : "none")})");
            if (f["area"] != null && (string)f["area"] != "continent") errors.Add($"{p}.area must be \"continent\"");
            if (f["around"] != null && f["around"].Type != JTokenType.String) errors.Add($"{p}.around must be a feature id");
            if (f["along"] != null && f["along"].Type != JTokenType.String) errors.Add($"{p}.along must be a river or road id");
        }

        static void CheckInfluenceNumbers(JObject f, string p, List<string> errors)
        {
            if (f["reach"] != null)
            {
                if (!Num(f["reach"], 0.02, 1)) errors.Add($"{p}.reach must be a number in [0.02, 1]");
                if (f["around"] == null && f["along"] == null) errors.Add($"{p}.reach only goes with around / along");
            }
            if (f["strength"] != null && !Num(f["strength"], 0, 1)) errors.Add($"{p}.strength must be a number in [0, 1]");
            if (f["falloff"] != null && !Num(f["falloff"], 0.05, 1)) errors.Add($"{p}.falloff must be a number in [0.05, 1]");
            if (f["edge"] != null && !Num(f["edge"], 0, 0.6)) errors.Add($"{p}.edge must be a number in [0, 0.6]");
        }

        static void CheckOnto(JToken onto, string p, IReadOnlyCollection<string> looks, List<string> errors)
        {
            if (onto == null || OneOf(new[] { "filler", "any" }, onto)) return;
            if (onto is JArray oa && oa.Count > 0 && oa.All(b => (string)b == "filler" || OneOf(looks, b))) return;
            errors.Add($"{p}.onto must be \"filler\", \"any\" or a list of world biome ids (\"filler\" allowed)");
        }

        static void CheckPatches(JToken pt, string p, List<string> errors)
        {
            if (pt == null) return;
            if (!(pt is JObject po) || !Num(po["share"], 0.02, 0.8))
            {
                errors.Add($"{p}.patches must be {{share: 0.02..0.8, size?}}");
                return;
            }
            foreach (var prop in po.Properties()) if (prop.Name != "share" && prop.Name != "size") errors.Add($"{p}.patches.{prop.Name}: unknown key");
            if (po["size"] != null && !OneOf(PatchSizes, po["size"])) errors.Add($"{p}.patches.size must be {string.Join("/", PatchSizes)}");
        }

        static void CheckHeart(JToken h, string p, List<string> errors)
        {
            if (h == null) return;
            if (!(h is JObject ho) || !OneOf(HeartTerrains, ho["terrain"]))
            {
                errors.Add($"{p}.heart must be {{terrain: {string.Join("/", HeartTerrains)}, above?}}");
                return;
            }
            foreach (var prop in ho.Properties()) if (prop.Name != "terrain" && prop.Name != "above") errors.Add($"{p}.heart.{prop.Name}: unknown key");
            if (ho["above"] != null && !Num(ho["above"], 0.3, 1)) errors.Add($"{p}.heart.above must be a number in [0.3, 1]");
        }

        /// <summary>Check of a MERGED theme: every feature (overrides resolved) is well formed, and what relations, rivers and roads point at exists and fits.</summary>
        public static List<string> Validate(Theme theme, IReadOnlyCollection<string> looks)
        {
            var errors = new List<string>();
            for (int i = 0; i < theme.Features.Count; i++) CheckFeature(theme.Features[i], $"features[{i}]", looks, errors);
            var byId = new Dictionary<string, JObject>();
            foreach (var f in theme.Features) byId[(string)f["id"] ?? ""] = f;
            foreach (var f in theme.Features) CheckRefs(f, byId, errors);
            if (theme.StartSettlement == "city" && !theme.Features.Any(f => (string)f["kind"] == "city"))
                errors.Add("start.settlement is \"city\" but the theme has no city feature");
            return errors;
        }

        // What one feature points at exists, and is the right kind.
        static void CheckRefs(JObject f, Dictionary<string, JObject> byId, List<string> errors)
        {
            string p = $"feature {(string)f["id"]}";
            string kind = (string)f["kind"];
            if (kind == "river")
            {
                foreach (var l in f["through"] as JArray ?? new JArray()) Ref(byId, (string)l, $"{p}.through", new[] { "lake" }, errors);
                return;
            }
            if (kind == "road")
            {
                foreach (var v in f["via"] as JArray ?? new JArray()) Ref(byId, (string)v, $"{p}.via", new[] { "city", "town" }, errors);
                return;
            }
            if (kind == "influence")
            {
                if (f["around"] != null) Ref(byId, (string)f["around"], $"{p}.around", new[] { "zone", "mountains", "lake", "city", "town" }, errors);
                if (f["along"] != null) Ref(byId, (string)f["along"], $"{p}.along", new[] { "river", "road" }, errors);
            }
            RelationRefs(f, p, byId, errors);
            if ((kind == "city" || kind == "town") && f["on"] != null) Ref(byId, (string)f["on"], $"{p}.on", new[] { "road" }, errors);
        }

        static void RelationRefs(JObject f, string p, Dictionary<string, JObject> byId, List<string> errors)
        {
            if (f["near"] != null) Ref(byId, (string)f["near"], $"{p}.near", null, errors);
            var far = f["farFrom"];
            if (far != null) Ref(byId, far.Type == JTokenType.String ? (string)far : (string)far["id"], $"{p}.farFrom", null, errors);
            foreach (var x in f["between"] as JArray ?? new JArray()) Ref(byId, (string)x, $"{p}.between", null, errors);
        }

        static void Ref(Dictionary<string, JObject> byId, string id, string path, string[] kinds, List<string> errors)
        {
            if (id == "start") return;
            if (!byId.TryGetValue(id, out var target)) errors.Add($"{path}: unknown feature \"{id}\"");
            else if (kinds != null && !kinds.Contains((string)target["kind"])) errors.Add($"{path}: \"{id}\" must be a {string.Join(" or ", kinds)}");
        }

        /// <summary>
        /// Merges mix-ins (depth-first, in order) under the theme's own fields: features BY ID (a later
        /// feature with the same id overrides the earlier one's keys), start, climate and continent by key.
        /// </summary>
        public static Theme Merge(JObject file, Func<string, JObject> lookup) => Merge(file, lookup, new List<string>());

        static Theme Merge(JObject file, Func<string, JObject> lookup, List<string> seen)
        {
            string id = (string)file["id"];
            if (seen.Contains(id)) throw new InvalidOperationException($"world theme mixin cycle: {string.Join(" -> ", seen.Concat(new[] { id }))}");
            var layers = new List<JObject>();
            foreach (var m in file["mixins"] as JArray ?? new JArray())
            {
                var mf = lookup((string)m) ?? throw new InvalidOperationException($"world theme \"{id}\": unknown mixin \"{(string)m}\"");
                layers.Add(ToJson(Merge(mf, lookup, seen.Concat(new[] { id }).ToList())));
            }
            layers.Add(file);
            var features = new List<JObject>();
            JObject start = null, climate = null, continent = null;
            string tracks = null;
            foreach (var l in layers)
            {
                MergeFeatures(features, l["features"] as JArray);
                if (l["start"] is JObject s) start = Over(start, s);
                if (l["climate"] is JObject c) climate = Over(climate, c);
                if (l["tracks"] != null) tracks = (string)l["tracks"];
                if (l["continent"] is JObject co) continent = Over(continent, co);
            }
            return new Theme
            {
                Id = id,
                Name = (string)file["name"] ?? id,
                Description = (string)file["description"] ?? "",
                Revision = (int?)file["revision"] ?? 1,
                Mixin = (bool?)file["mixin"] ?? false,
                Start = start ?? new JObject { ["setting"] = "meadow", ["settlement"] = "town" },
                Climate = climate,
                Tracks = tracks ?? "sparse",
                CoreRegions = (int?)continent?["coreRegions"] ?? 3,
                FadeRegions = (int?)continent?["fadeRegions"] ?? 1,
                Features = features,
            };
        }

        // A layer's features over those so far: one with the same id overrides its keys, a new one is added.
        static void MergeFeatures(List<JObject> features, JArray layer)
        {
            foreach (var f in (layer ?? new JArray()).OfType<JObject>())
            {
                int i = features.FindIndex(g => (string)g["id"] == (string)f["id"]);
                if (i >= 0) features[i] = Over(features[i], f);
                else features.Add((JObject)f.DeepClone());
            }
        }

        static JObject Over(JObject under, JObject over)
        {
            var result = under != null ? (JObject)under.DeepClone() : new JObject();
            foreach (var p in over.Properties()) result[p.Name] = p.Value.DeepClone();
            return result;
        }

        // A merged mix-in, back as a file layer for the theme that pulls it in.
        static JObject ToJson(Theme t) => new JObject
        {
            ["id"] = t.Id,
            ["start"] = t.Start,
            ["climate"] = t.Climate,
            ["tracks"] = t.Tracks,
            ["continent"] = new JObject { ["coreRegions"] = t.CoreRegions, ["fadeRegions"] = t.FadeRegions },
            ["features"] = new JArray(t.Features),
        };
    }
}
