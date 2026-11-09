// What the world plan is solved from (docs/design/world-squares.md section 2, J-080 phase 2): world/bands.json and the
// square templates in world/squares/. Ported from the paper prototype (tools/world-paper/index.html, its TEMPLATES and
// buildOptions): a template turned and with each of its "may" features on or off is an option, and the solver picks
// one option per world square. Weights are integers (thousandths), so the roll is the same on every peer.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>A feature's kind where it crosses a side.</summary>
    public enum EdgeKind
    {
        River,
        Road,
        Range,
        Canyon,
    }

    /// <summary>One feature at one slot of a side; a river flows in or out of the square it's on.</summary>
    public sealed class EdgeFeature
    {
        public EdgeKind Kind;
        public int Slot;
        /// <summary>For a river: +1 flows into this square, -1 out of it; 0 for the rest.</summary>
        public int Flow;
        /// <summary>For a road: part of a main road, or only a trail.</summary>
        public bool Main, Trail;

        public EdgeFeature Copy() => new EdgeFeature { Kind = Kind, Slot = Slot, Flow = Flow, Main = Main, Trail = Trail };

        internal char Letter => Kind switch { EdgeKind.River => 'r', EdgeKind.Road => 'd', EdgeKind.Range => 'g', _ => 'c' };

        string FlowLetter => Flow switch { > 0 => "i", < 0 => "o", _ => "" };

        internal string Token => Letter.ToString() + Slot.ToString(CultureInfo.InvariantCulture) + FlowLetter;
    }

    /// <summary>A landmark intent a template asks for, and where it may go.</summary>
    public sealed class TemplateLandmark
    {
        public string Intent;
        /// <summary>"water", "land" or null: what the site wants beside it.</summary>
        public string Near;
        /// <summary>The band a site wants, or -1.</summary>
        public int Band = -1;
        /// <summary>A fixed site in the square's cells (the valley's castle), or null.</summary>
        public int[] At;
    }

    /// <summary>One world/squares/&lt;id&gt;.json.</summary>
    public sealed class SquareTemplate
    {
        public string Id;
        public int[] Bands = Array.Empty<int>();
        public string Biome;
        /// <summary>Its weight in the roll, in thousandths.</summary>
        public int Weight;
        public List<TemplateLandmark> Landmarks = new List<TemplateLandmark>();
        /// <summary>Per side, n e s w unturned, what it offers: (feature, must).</summary>
        public List<(EdgeFeature feature, bool must)>[] Offers = { new List<(EdgeFeature, bool)>(), new List<(EdgeFeature, bool)>(), new List<(EdgeFeature, bool)>(), new List<(EdgeFeature, bool)>() };
        public bool[] Coast = new bool[4];
        public bool RiverEndsInLake, RangeFades, CanyonCloses;
        public bool Rotate, Repeats, Filler, Pinned, Pass;
        /// <summary>0, or the wall it is: 1 north (ice), 2 south (a dune sea).</summary>
        public int Wall;

        /// <summary>Its first village intent's want: "water", "land", "spire", "castle" or null.</summary>
        public string Lm
        {
            get
            {
                foreach (var l in Landmarks)
                {
                    if (l.Intent == "castle") return "castle";
                    if (l.Intent == "sea-spire") return "spire";
                    if (l.Intent == WorldPlan.VillageIntent) return l.Near ?? "land";
                }
                return null;
            }
        }

        public bool AsksFor(string intent) => Landmarks.Any(l => l.Intent == intent);
    }

    /// <summary>A template on one band, turned, with its "may" features chosen: what the solver picks per square.</summary>
    public sealed class SquareOption
    {
        public int Index;
        public SquareTemplate Template;
        public int Band;
        /// <summary>Quarter turns clockwise.</summary>
        public int Turn;
        /// <summary>Per side (n e s w as it lies), its features but roads, by slot.</summary>
        public EdgeFeature[][] Features;
        /// <summary>Per side, an id for its features (equal ids, equal sides); <see cref="SquareLibrary.Wild"/> fits any.</summary>
        public int[] Sig;
        public bool[] Coast;
        /// <summary>Per side, the slots it offers a road that happens to cross it.</summary>
        public int[][] RoadOffer;
        /// <summary>Its weight in thousandths, with each "may" it keeps counting extra.</summary>
        public long Weight;
        public bool Sea, Filler;
    }

    /// <summary>The bands and templates, and every option made from them.</summary>
    public sealed class SquareLibrary
    {
        public const string Folder = "squares";
        public const int Sides = 4;
        /// <summary>A side id that fits any (a filler's).</summary>
        public const int Wild = -1;
        /// <summary>A side id of the sea.</summary>
        public const int SeaSide = -2;

        public string[] BandIds = Array.Empty<string>();
        public int[] BandHeights = Array.Empty<int>();
        public int WallBand, Step = 1, Slots = 9;
        public int[] Shore = { 48, 112 };
        public List<SquareTemplate> Templates = new List<SquareTemplate>();
        public List<SquareOption> Options = new List<SquareOption>();
        /// <summary>Per band, the options a square of it may take (not the valley, a wall or a filler).</summary>
        public List<int>[] ByBand = Array.Empty<List<int>>();
        public int ValleyOption = -1;
        public int[] FillerOption = Array.Empty<int>(), WallOption = { -1, -1, -1 };
        /// <summary>The village intent, to name a site's form by biome; null if world/landmarks has none.</summary>
        public LandmarkIntent Village;

        readonly List<string> sigs = new List<string>();
        readonly Dictionary<string, int> sigIds = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<long, bool> compat = new Dictionary<long, bool>();

        public int Lands => BandIds.Length;

        /// <summary>world/bands.json, world/squares/*.json and world/landmarks/ under <paramref name="worldDir"/>.</summary>
        public static SquareLibrary Load(string worldDir)
        {
            var files = Directory.GetFiles(Path.Combine(worldDir, Folder), "*.json").OrderBy(f => f, StringComparer.Ordinal);
            var lib = Parse(File.ReadAllText(Path.Combine(worldDir, "bands.json")), files.Select(File.ReadAllText));
            string village = Path.Combine(worldDir, LandmarkIntent.Folder, WorldPlan.VillageIntent + ".json");
            if (File.Exists(village)) lib.Village = LandmarkIntent.Parse(File.ReadAllText(village));
            return lib;
        }

        /// <summary>The bands' text and every template's. Throws <see cref="FormatException"/> for one it can't use.</summary>
        public static SquareLibrary Parse(string bandsJson, IEnumerable<string> templateJsons)
        {
            var lib = new SquareLibrary();
            var b = Read(bandsJson, "bands.json");
            var bands = (JArray)b["bands"] ?? throw new FormatException("bands.json: no \"bands\"");
            lib.BandIds = bands.Select(x => (string)x["id"]).ToArray();
            lib.BandHeights = bands.Select(x => (int)x["height"]).ToArray();
            lib.WallBand = lib.BandIds.Length;
            lib.Step = (int?)b["step"] ?? 1;
            lib.Slots = (int?)b["slots"] ?? 9;
            if (b["shore"] is JArray shore) lib.Shore = shore.Select(x => (int)x).ToArray();
            if (lib.BandIds.Length < 2 || lib.Shore.Length < 1 || lib.Step < 1) throw new FormatException("bands.json: needs two bands, a shore class and a step");
            foreach (string json in templateJsons) lib.Templates.Add(lib.ReadTemplate(json));
            lib.Templates.Sort((p, q) => string.CompareOrdinal(p.Id, q.Id));
            for (int k = 1; k < lib.Templates.Count; k++)
                if (lib.Templates[k].Id == lib.Templates[k - 1].Id) throw new FormatException($"squares: {lib.Templates[k].Id} twice");
            lib.BuildOptions();
            return lib;
        }

        static JObject Read(string json, string what)
        {
            try { return JObject.Parse(json); }
            catch (JsonException e) { throw new FormatException($"{what}: {e.Message}", e); }
        }

        int BandOf(string id, string where)
        {
            if (id == "wall") return WallBand;
            int k = Array.IndexOf(BandIds, id);
            if (k < 0) throw new FormatException($"{where}: no band {id}");
            return k;
        }

        SquareTemplate ReadTemplate(string json)
        {
            var o = Read(json, "squares");
            string id = (string)o["id"];
            if (string.IsNullOrEmpty(id)) throw new FormatException("squares: a template needs an \"id\"");
            var t = new SquareTemplate
            {
                Id = id,
                Biome = (string)o["biome"],
                Weight = (int)Math.Round(((double?)o["weight"] ?? 1) * 1000, MidpointRounding.AwayFromZero),
                Rotate = (bool?)o["rotate"] ?? true,
                Repeats = (bool?)o["repeats"] ?? false,
                Filler = (bool?)o["filler"] ?? false,
                Pinned = (bool?)o["pinned"] ?? false,
                Pass = (bool?)o["pass"] ?? false,
                Wall = (string)o["wall"] switch { "north" => 1, "south" => 2, _ => 0 },
            };
            t.Bands = ((JArray)o["bands"] ?? throw new FormatException($"squares {id}: no \"bands\"")).Select(x => BandOf((string)x, "squares " + id)).ToArray();
            if (o["landmarks"] is JArray lms)
                foreach (var l in lms)
                {
                    var where = l["where"] as JObject;
                    t.Landmarks.Add(new TemplateLandmark
                    {
                        Intent = (string)l["intent"],
                        Near = (string)where?["near"],
                        Band = where?["band"] != null ? BandOf((string)where["band"], "squares " + id) : -1,
                        At = (where?["at"] as JArray)?.Select(x => (int)x).ToArray(),
                    });
                }
            if (t.Landmarks.Count > 2) throw new FormatException($"squares {id}: one or two landmark intents, not {t.Landmarks.Count}");
            var ends = o["ends"] as JObject;
            t.RiverEndsInLake = (string)ends?["river"] == "lake";
            t.RangeFades = (string)ends?["range"] == "fade";
            t.CanyonCloses = (string)ends?["canyon"] == "close";
            var edges = o["edges"] as JObject;
            for (int s = 0; s < Sides; s++)
            {
                var side = edges?["nesw".Substring(s, 1)] as JObject;
                if (side == null) continue;
                t.Coast[s] = (bool?)side["coast"] ?? false;
                if (!(side["offers"] is JArray offers)) continue;
                foreach (var f in offers)
                {
                    var kind = (string)f["kind"] switch
                    {
                        "river" => EdgeKind.River,
                        "road" => EdgeKind.Road,
                        "range" => EdgeKind.Range,
                        "canyon" => EdgeKind.Canyon,
                        _ => throw new FormatException($"squares {id}: no feature kind {(string)f["kind"]}"),
                    };
                    int slot = (int)f["slot"];
                    if (slot < 0 || slot >= Slots) throw new FormatException($"squares {id}: slot {slot} isn't one of {Slots}");
                    int flow = 0;
                    if (kind == EdgeKind.River) flow = (string)f["flow"] == "out" ? -1 : 1;
                    bool must = (bool?)f["must"] ?? false;
                    if (must == ((bool?)f["may"] ?? false)) throw new FormatException($"squares {id}: a feature is a must or a may");
                    t.Offers[s].Add((new EdgeFeature { Kind = kind, Slot = slot, Flow = flow }, must));
                }
            }
            return t;
        }

        /// <summary>Where a side's slot lands after <paramref name="turns"/> quarter turns clockwise.</summary>
        public int TurnSide(int side, int slot, int turns, out int turnedSlot)
        {
            for (int k = 0; k < turns; k++)
            {
                if (side == 1 || side == 3) slot = Slots - 1 - slot;
                side = (side + 1) % Sides;
            }
            turnedSlot = slot;
            return side;
        }

        internal int SigId(EdgeFeature[] side)
        {
            string key = string.Join(",", side.Select(f => f.Token).OrderBy(x => x, StringComparer.Ordinal));
            if (sigIds.TryGetValue(key, out int id)) return id;
            id = sigs.Count;
            sigs.Add(key);
            sigIds[key] = id;
            return id;
        }

        /// <summary>
        /// Whether side ids <paramref name="x"/> (on a square of band <paramref name="bx"/>) and <paramref name="y"/> (its
        /// neighbour's, band <paramref name="by"/>) meet: the same slots and kinds, and a river leaving one enters the other,
        /// never uphill.
        /// </summary>
        public bool Compatible(int x, int y, int bx, int by)
        {
            if (x < 0 || y < 0) return true;
            int sign = Math.Sign(bx - by);
            long key = ((long)x * 1_000_003 + y) * 3 + sign + 1;
            if (compat.TryGetValue(key, out bool v)) return v;
            v = SidesMeet(sigs[x], sigs[y], sign);
            compat[key] = v;
            return v;
        }

        static bool SidesMeet(string a, string b, int sign)
        {
            var fa = Parse(a);
            var fb = Parse(b);
            if (fa.Count != fb.Count) return false;
            foreach (var kv in fa)
            {
                if (!fb.TryGetValue(kv.Key, out string g) || g[0] != kv.Value[0]) return false;
                if (kv.Value[0] != 'r') continue;
                bool ok = (kv.Value[1] == 'o' && g[1] == 'i' && sign >= 0) || (kv.Value[1] == 'i' && g[1] == 'o' && sign <= 0);
                if (!ok) return false;
            }
            return true;
        }

        static Dictionary<int, string> Parse(string sig)
        {
            var m = new Dictionary<int, string>();
            if (sig.Length == 0) return m;
            foreach (string t in sig.Split(',')) m[t[1] - '0'] = t[0] + (t.Length > 2 ? t.Substring(2) : "");
            return m;
        }

        /// <summary>
        /// Whether a template's features make sense inside it: a river in needs a way out or a lake, a spring is in the
        /// hills or higher, and a lone range or canyon end needs the template to let it fade or close.
        /// </summary>
        internal static bool WholeInside(SquareTemplate t, EdgeFeature[][] feats, int band)
        {
            int ri = 0, ro = 0, g = 0, c = 0;
            foreach (var side in feats)
                foreach (var f in side)
                {
                    if (f.Kind == EdgeKind.River) { if (f.Flow > 0) ri++; else ro++; }
                    else if (f.Kind == EdgeKind.Range) g++;
                    else if (f.Kind == EdgeKind.Canyon) c++;
                }
            if (ri > 0 && ro == 0 && !t.RiverEndsInLake) return false;
            if (ro > 0 && ri == 0 && band < 2) return false;
            if (g == 1 && !t.RangeFades) return false;
            if (c == 1 && !t.CanyonCloses) return false;
            return true;
        }

        void BuildOptions()
        {
            ByBand = new List<int>[BandIds.Length];
            for (int b = 0; b < ByBand.Length; b++) ByBand[b] = new List<int>();
            FillerOption = Enumerable.Repeat(-1, BandIds.Length).ToArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in Templates)
            {
                if (t.Wall != 0) { WallOption[t.Wall] = Add(Plain(t, WallBand, wild: false)); continue; }
                if (t.Filler)
                {
                    foreach (int band in t.Bands) FillerOption[band] = Add(Plain(t, band, wild: true));
                    continue;
                }
                var baseFeats = new List<(int side, EdgeFeature f, bool must)>();
                var roads = new List<(int side, int slot)>();
                for (int s = 0; s < Sides; s++)
                    foreach (var (f, must) in t.Offers[s])
                        if (f.Kind == EdgeKind.Road) roads.Add((s, f.Slot));
                        else baseFeats.Add((s, f, must));
                int mays = baseFeats.Count(f => !f.must);
                int turns = t.Rotate ? 4 : 1;
                foreach (int band in t.Bands)
                    for (int r = 0; r < turns; r++)
                    {
                        var offer = new List<int>[Sides];
                        for (int s = 0; s < Sides; s++) offer[s] = new List<int>();
                        foreach (var (side, slot) in roads) offer[TurnSide(side, slot, r, out int sl)].Add(sl);
                        for (int mask = 0; mask < 1 << mays; mask++)
                        {
                            var feats = new List<EdgeFeature>[Sides];
                            for (int s = 0; s < Sides; s++) feats[s] = new List<EdgeFeature>();
                            int mi = 0, on = 0;
                            foreach (var (side, f, must) in baseFeats)
                            {
                                bool keep = must;
                                if (!must)
                                {
                                    keep = (mask & (1 << mi)) != 0;
                                    mi++;
                                    if (keep) on++;
                                }
                                if (!keep) continue;
                                int s2 = TurnSide(side, f.Slot, r, out int sl2);
                                feats[s2].Add(new EdgeFeature { Kind = f.Kind, Slot = sl2, Flow = f.Flow });
                            }
                            var arr = feats.Select(l => l.OrderBy(f => f.Slot).ToArray()).ToArray();
                            if (!WholeInside(t, arr, band)) continue;
                            var coast = new bool[Sides];
                            for (int s = 0; s < Sides; s++) if (t.Coast[s]) coast[(s + r) % Sides] = true;
                            var sig = arr.Select(SigId).ToArray();
                            string key = t.Id + "|" + band + "|" + string.Join("/", sig) + "|" + string.Join("", coast.Select(x => x ? '1' : '0'));
                            if (!seen.Add(key)) continue;
                            int idx = Add(new SquareOption
                            {
                                Template = t,
                                Band = band,
                                Turn = r,
                                Features = arr,
                                Sig = sig,
                                Coast = coast,
                                RoadOffer = offer.Select(l => l.ToArray()).ToArray(),
                                Weight = (long)t.Weight * (10 + 6 * on) / 10,
                                Sea = band == 0,
                            });
                            if (t.Pinned) ValleyOption = idx;
                            else ByBand[band].Add(idx);
                        }
                    }
            }
            if (ValleyOption < 0) throw new FormatException("squares: no pinned valley template");
            for (int b = 0; b < FillerOption.Length; b++)
                if (FillerOption[b] < 0) throw new FormatException($"squares: no filler for the {BandIds[b]} band");
        }

        SquareOption Plain(SquareTemplate t, int band, bool wild)
        {
            var empty = new EdgeFeature[Sides][];
            for (int s = 0; s < Sides; s++) empty[s] = Array.Empty<EdgeFeature>();
            int id = wild ? Wild : SigId(Array.Empty<EdgeFeature>());
            return new SquareOption
            {
                Template = t,
                Band = band,
                Features = empty,
                Sig = new[] { id, id, id, id },
                Coast = new[] { true, true, true, true },
                RoadOffer = new[] { Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>() },
                Sea = band == 0,
                Filler = t.Filler,
            };
        }

        int Add(SquareOption o)
        {
            o.Index = Options.Count;
            Options.Add(o);
            return o.Index;
        }
    }
}
