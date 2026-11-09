// The far ring's landmarks (docs/design/world-squares.md section 4): every landmark the plan places that shows from far
// off (world/horizon.json says which types do, how far each is seen and roughly how big it is), with its type, where it
// stands on the map and its seenFrom, a radius in chunks. The view draws them as far meshes with their own haze out to
// seenFrom, past the known ring. A pure function of the seed, the plan and the file; nothing shared reads it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>One type's row of world/horizon.json.</summary>
    public sealed class HorizonRow
    {
        public string Type, Shape;
        /// <summary>How far off it shows: a radius in chunks from the character.</summary>
        public int SeenFrom;
        /// <summary>Metres: how high it stands above the ground at it, and how far it reaches from its middle.</summary>
        public int Height, Radius;
    }

    /// <summary>world/horizon.json: the landmark types that show from far off.</summary>
    public sealed class HorizonTable
    {
        public const string FileName = "horizon.json";

        readonly Dictionary<string, HorizonRow> rows = new Dictionary<string, HorizonRow>(StringComparer.Ordinal);

        /// <summary>Each type's row.</summary>
        public IReadOnlyDictionary<string, HorizonRow> Rows => rows;

        /// <summary>world/horizon.json under <paramref name="worldDir"/>.</summary>
        public static HorizonTable Load(string worldDir) => Parse(File.ReadAllText(Path.Combine(worldDir, FileName)));

        public static HorizonTable Parse(string json)
        {
            var t = new HorizonTable();
            if (!(JObject.Parse(json)["landmarks"] is JObject rows)) throw new FormatException($"{FileName} needs \"landmarks\"");
            foreach (var p in rows.Properties())
            {
                var o = p.Value as JObject ?? throw new FormatException($"{FileName}: {p.Name} isn't an object");
                int Whole(string key, int least)
                {
                    if (!(o[key] is JValue v) || v.Type != JTokenType.Integer || (int)v < least)
                        throw new FormatException($"{FileName}: {p.Name}'s {key} must be a whole number, at least {least}");
                    return (int)v;
                }
                t.rows[p.Name] = new HorizonRow
                {
                    Type = p.Name,
                    SeenFrom = Whole("seenFrom", 1),
                    Height = Whole("height", 1),
                    Radius = Whole("radius", 1),
                    Shape = (string)o["shape"] ?? "keep",
                };
            }
            return t;
        }
    }

    /// <summary>A landmark the view draws far off.</summary>
    public sealed class FarLandmark
    {
        /// <summary>Its type, a key of world/horizon.json ("volcano", "castle", "rock spire", ...).</summary>
        public string Type;
        /// <summary>Its name where the plan's rows give one ("Mount Hearth"), else null.</summary>
        public string Name;
        /// <summary>"landmark", "outcrop" or "waystation", and its index in the plan's list of them.</summary>
        public string From;
        public int Index;
        /// <summary>The world square it's in.</summary>
        public int Square;
        /// <summary>Its middle, a map cell (a village landmark's first stamp's middle, else the plan's site).</summary>
        public int X, Y;
        public int SeenFrom, Height, Radius;
        public string Shape;
        /// <summary>The map's width in cells where it wraps east-west, else 0.</summary>
        public int WrapCells;

        /// <summary>
        /// Whether it shows from map cell (<paramref name="x"/>, <paramref name="y"/>): within its seenFrom radius, the
        /// short way round where the map wraps.
        /// </summary>
        public bool SeenAt(int x, int y)
        {
            long dx = DxTo(x), dy = Y - y, r = (long)SeenFrom * ChunkGen.Size;
            return dx * dx + dy * dy <= r * r;
        }

        /// <summary>Its x less <paramref name="x"/>, in cells: the short way round where the map wraps.</summary>
        public int DxTo(int x)
        {
            int d = X - x;
            if (WrapCells <= 0) return d;
            d = PlanGrid.Mod(d, WrapCells);
            return d >= WrapCells - WrapCells / 2 ? d - WrapCells : d;
        }
    }

    /// <summary>The plan's far landmarks, in the plan's order: its landmarks, then outcrops, then waystations.</summary>
    public sealed class FarLandmarks
    {
        public readonly IReadOnlyList<FarLandmark> All;

        FarLandmarks(List<FarLandmark> all) => All = all;

        public static FarLandmarks Make(ChunkGen gen, HorizonTable table)
        {
            var plan = gen.Plan;
            var all = new List<FarLandmark>();
            void Add(string type, string from, int index, int square, int siteX, int siteY, LandmarkRows rows)
            {
                if (type == null || !table.Rows.TryGetValue(type, out var row)) return;
                var (x, y) = rows != null ? rows.Centre : gen.MapCell(square, siteX, siteY);
                all.Add(new FarLandmark
                {
                    Type = type,
                    Name = rows?.Name,
                    From = from,
                    Index = index,
                    Square = square,
                    X = x,
                    Y = y,
                    SeenFrom = row.SeenFrom,
                    Height = row.Height,
                    Radius = row.Radius,
                    Shape = row.Shape,
                    WrapCells = gen.WrapX ? gen.CellsW : 0,
                });
            }
            for (int k = 0; k < plan.Landmarks.Count; k++)
            {
                var l = plan.Landmarks[k];
                Add(TypeOf(plan, l), "landmark", k, l.Square, l.SiteX, l.SiteY, gen.LandmarkRows[k]);
            }
            for (int k = 0; k < plan.Outcrops.Count; k++)
            {
                var o = plan.Outcrops[k];
                Add(o.Kind, "outcrop", k, o.Square, o.SiteX, o.SiteY, null);
            }
            for (int k = 0; k < plan.Waystations.Count; k++)
            {
                var w = plan.Waystations[k];
                Add(w.Kind, "waystation", k, w.Square, w.SiteX, w.SiteY, null);
            }
            return new FarLandmarks(all);
        }

        /// <summary>A plan landmark's type: a port, a village form's landmark ("great tree"), else its kind ("castle").</summary>
        public static string TypeOf(WorldPlan plan, PlanLandmark l)
        {
            if (l.Port) return "port";
            if (l.Intent == WorldPlan.VillageIntent) return plan.Library.Village?.LandmarkOf(l.Kind) ?? l.Kind;
            return l.Kind;
        }

        /// <summary>The far landmarks that show from map cell (<paramref name="x"/>, <paramref name="y"/>), in order.</summary>
        public IEnumerable<FarLandmark> SeenAt(int x, int y) => All.Where(l => l.SeenAt(x, y));
    }
}
