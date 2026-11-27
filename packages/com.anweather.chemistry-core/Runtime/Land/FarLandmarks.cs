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
    public sealed class HorizonRow
    {
        public string Type, Shape;
        public int SeenFrom;
        public int Height, Radius;
    }

    public sealed class HorizonTable
    {
        public const string FileName = "horizon.json";

        readonly Dictionary<string, HorizonRow> rows = new Dictionary<string, HorizonRow>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, HorizonRow> Rows => rows;

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

    public sealed class FarLandmark
    {
        public string Type;
        public string Name;
        public string From;
        public int Index;
        public int Square;
        public int X, Y;
        public int SeenFrom, Height, Radius;
        public string Shape;
        public int WrapCells;

        public bool SeenAt(int x, int y)
        {
            long dx = DxTo(x), dy = Y - y, r = (long)SeenFrom * ChunkGen.Size;
            return dx * dx + dy * dy <= r * r;
        }

        public int DxTo(int x)
        {
            int d = X - x;
            if (WrapCells <= 0) return d;
            d = PlanGrid.Mod(d, WrapCells);
            return d >= WrapCells - WrapCells / 2 ? d - WrapCells : d;
        }
    }

    public sealed class FarLandmarks
    {
        public readonly IReadOnlyList<FarLandmark> All;

        FarLandmarks(List<FarLandmark> all) => All = all;

        public static FarLandmarks Make(ChunkGen gen, HorizonTable table)
        {
            var plan = gen.Plan;
            var all = new List<FarLandmark>();
            if (plan == null) return new FarLandmarks(all);
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
            var inner = gen.Pinned?.Inner;
            if (inner != null)
                foreach (var l in Make(inner, table).All)
                {
                    if (l.X < 0 || l.Y < 0 || l.X >= inner.CellsW || l.Y >= inner.CellsH) continue;
                    l.X += gen.PinnedX;
                    l.Y += gen.PinnedY;
                    l.Square = plan.Grid.Index(gen.OriginX + l.X / ChunkGen.Square, gen.OriginY + l.Y / ChunkGen.Square);
                    all.Add(l);
                }
            return new FarLandmarks(all);
        }

        public static string TypeOf(WorldPlan plan, PlanLandmark l)
        {
            if (l.Port) return "port";
            if (l.Intent == WorldPlan.VillageIntent) return plan.Library.Village?.LandmarkOf(l.Kind) ?? l.Kind;
            return l.Kind;
        }

        public IEnumerable<FarLandmark> SeenAt(int x, int y) => All.Where(l => l.SeenAt(x, y));
    }
}
