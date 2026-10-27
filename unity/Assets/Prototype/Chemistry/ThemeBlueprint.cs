// The blueprint solver (D11): a theme's goals made concrete geometry for one
// seed. Parametric (noisy ellipses for zones, mountains, lakes and islands;
// width-profiled curves for rivers; soft influence fields), it holds no
// cells: ThemeField asks it about each cell, so a chunk stays a pure function
// of (seed, theme, cell). Port of PocketTactics'
// src/core/world/theme/blueprint.ts, in our cells (one cell for its macro
// cell), for area features, the start zone, rivers and influences. Not yet
// ported: waypoints, roads, settlements, and solving several candidates and
// keeping the one meeting the most goals (goals.ts); so one candidate is
// solved, and the start is the origin rather than a start town.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>The continent's core (the theme in full) and fade band, in cells round the origin.</summary>
    public sealed class Continent
    {
        public double CoreHalf, FadeEnd;

        /// <summary>1 inside the core, easing to 0 across the fade band, 0 beyond (Chebyshev, cell centres).</summary>
        public double FadeAt(int x, int y)
        {
            double d = Math.Max(Math.Abs(x + 0.5), Math.Abs(y + 0.5));
            if (d <= CoreHalf) return 1;
            if (d >= FadeEnd) return 0;
            double t = (FadeEnd - d) / (FadeEnd - CoreHalf);
            return t * t * (3 - 2 * t);
        }
    }

    public sealed class BpZone
    {
        public string Id, Look;
        public Blob Blob;
        /// <summary>Higher paints over lower.</summary>
        public int Priority;
        /// <summary>The start setting's zone ignores the fade and always paints.</summary>
        public bool Start;
    }

    public sealed class BpLake
    {
        public string Id, Material, IslandLook;
        public Blob Blob, Island;
    }

    public sealed class BpRiver
    {
        public string Id, Material;
        public GeoPath Path;
        /// <summary>The in-core anchors (source, lakes passed through, mouth).</summary>
        public List<Pt> Anchors;
    }

    /// <summary>A solved influence: a soft field over the continent tilting the land toward a look.</summary>
    public sealed class BpInfluence
    {
        public string Id, Look;
        public double Strength, Falloff, Edge;
        /// <summary>continent, blob, around or along.</summary>
        public string Shape;
        public Blob Blob;
        public GeoPath Path;
        /// <summary>Cells (around / along).</summary>
        public double Reach;
        /// <summary>Scattered blobs covering a share of the field; lattice in cells. Zero share for none.</summary>
        public double PatchShare, PatchLattice;
        public string HeartTerrain;
        public double HeartAbove;
        public bool OntoAny, OntoFiller;
        public List<string> OntoLooks = new List<string>();
        /// <summary>Declaration order: later influences sit inside earlier ones where they overlap.</summary>
        public int Order;
    }

    public sealed class Blueprint
    {
        public string ThemeId;
        public int Seed;
        public Continent Continent;
        public string StartSetting;
        /// <summary>Lowest priority first.</summary>
        public List<BpZone> Zones = new List<BpZone>();
        public List<BpLake> Lakes = new List<BpLake>();
        public List<BpRiver> Rivers = new List<BpRiver>();
        public List<BpInfluence> Influences = new List<BpInfluence>();
        /// <summary>Feature id → centre, as continent fractions.</summary>
        public Dictionary<string, Pt> Centres = new Dictionary<string, Pt>();
    }

    public static class ThemeBlueprint
    {
        /// <summary>Water keeps this far (cells) from the start; hazards (lava) this far.</summary>
        public const double StartWaterClear = 24, StartHazardClear = 104;
        const double RiverbankClear = 14, LakeshoreEdge = 16, StartTownHalf = 4;
        const int SaltTheme = 0x54484d45;

        static readonly Dictionary<string, (int dx, int dy)> Dirs = new Dictionary<string, (int, int)>
        {
            ["center"] = (0, 0),
            ["north"] = (0, -1),
            ["south"] = (0, 1),
            ["east"] = (1, 0),
            ["west"] = (-1, 0),
            ["northeast"] = (1, -1),
            ["northwest"] = (-1, -1),
            ["southeast"] = (1, 1),
            ["southwest"] = (-1, 1),
        };
        static readonly Dictionary<string, double> DistR = new Dictionary<string, double> { ["near"] = 0.3, ["mid"] = 0.58, ["far"] = 0.8 };
        static readonly Dictionary<string, double> ZoneSize = new Dictionary<string, double> { ["small"] = 0.12, ["medium"] = 0.2, ["large"] = 0.3, ["huge"] = 0.6 };
        static readonly Dictionary<string, double> LakeSize = new Dictionary<string, double> { ["small"] = 0.06, ["medium"] = 0.11, ["large"] = 0.17, ["huge"] = 0.24 };
        static readonly Dictionary<string, double> IslandSize = new Dictionary<string, double> { ["small"] = 0.24, ["medium"] = 0.34, ["large"] = 0.45 };
        static readonly Dictionary<string, double> MeanderOf = new Dictionary<string, double> { ["none"] = 0, ["low"] = 0.35, ["medium"] = 0.7, ["high"] = 1.15 };
        static readonly Dictionary<string, double> RiverHalf = new Dictionary<string, double> { ["narrow"] = 1.5, ["medium"] = 2.4, ["wide"] = 3.4 };
        static readonly Dictionary<string, double> Orient = new Dictionary<string, double>
        {
            ["ew"] = 0,
            ["ns"] = Math.PI / 2,
            ["nesw"] = -Math.PI / 4,
            ["nwse"] = Math.PI / 4,
        };
        static readonly Dictionary<string, double> DefaultStretch = new Dictionary<string, double> { ["zone"] = 1.3, ["mountains"] = 3, ["lake"] = 1.35 };
        /// <summary>The start zone's radius (cells) by setting; 38 for the rest.</summary>
        static readonly Dictionary<string, double> StartZoneRadius = new Dictionary<string, double> { ["desert"] = 46, ["lakeshore"] = 30 };
        static readonly Dictionary<string, double> PatchLattice = new Dictionary<string, double> { ["small"] = 6, ["medium"] = 10, ["large"] = 16 };

        /// <summary>The look a start setting paints round the start (null: none, the meadow start).</summary>
        public static readonly IReadOnlyDictionary<string, string> StartSettingLook = new Dictionary<string, string>
        {
            ["meadow"] = null,
            ["field"] = "plains",
            ["desert"] = "desert",
            ["forest"] = "deepwood",
            ["riverbank"] = "plains",
            ["lakeshore"] = "grassland",
            ["tundra"] = "frostreach",
            ["ashland"] = "ashflats",
        };

        /// <summary>The continent for a theme, with regions <paramref name="regionCells"/> across.</summary>
        public static Continent ContinentFor(Theme theme, double regionCells)
        {
            double r = (theme.CoreRegions - 1) / 2.0;
            double coreHalf = regionCells * (r + 0.5);
            return new Continent { CoreHalf = coreHalf, FadeEnd = coreHalf + regionCells * theme.FadeRegions };
        }

        static int ThemeHash(string id)
        {
            unchecked
            {
                int h = (int)0x811c9dc5u;
                foreach (char ch in id) h = (int)((uint)(h ^ ch) * 0x01000193u);
                return h;
            }
        }

        // Scratch state while solving.
        sealed class Draft
        {
            public int Seed;
            public Theme Theme;
            public Continent C;
            public ThemeRng Rng;
            public Blueprint Bp;
            public readonly List<(string id, Blob blob)> Mountains = new List<(string, Blob)>();
        }

        /// <summary>Solves a theme for a seed. <paramref name="regionCells"/> sizes the continent (see ContinentFor).</summary>
        public static Blueprint Solve(int seed, Theme theme, double regionCells)
        {
            var c = ContinentFor(theme, regionCells);
            var d = new Draft
            {
                Seed = seed,
                Theme = theme,
                C = c,
                Rng = new ThemeRng(WorldHash.Hash4(seed, SaltTheme, ThemeHash(theme.Id), 0)),
                Bp = new Blueprint { ThemeId = theme.Id, Seed = seed, Continent = c, StartSetting = theme.StartSetting },
            };
            d.Bp.Centres["start"] = new Pt(0, 0);
            PlaceAreaFeatures(d);
            PlaceStartZone(d);
            foreach (var f in theme.Features)
                if ((string)f["kind"] == "river") PlaceRiver(d, f);
            PlaceInfluences(d);
            d.Bp.Zones = d.Bp.Zones.OrderBy(z => z.Priority).ToList();
            return d.Bp;
        }

        // ---------- places and sizes ----------

        /// <summary>A place as continent fractions, jittered for variety (compass) or near-exact (coordinates).</summary>
        static Pt PlacePoint(Draft d, JToken place, string distance)
        {
            if (place == null || place.Type == JTokenType.String)
            {
                string word = (string)place ?? "center";
                var (dx, dy) = Dirs[word];
                if (word == "center") return new Pt(d.Rng.Range(-0.14, 0.14), d.Rng.Range(-0.14, 0.14));
                double r = DistR[distance ?? "mid"] * (dx != 0 && dy != 0 ? 0.85 : 1);
                double along = d.Rng.Range(-0.14, 0.14);
                double rad = r + d.Rng.Range(-0.06, 0.06);
                return new Pt(dx * rad - dy * along, dy * rad + dx * along);
            }
            if (place["cell"] is JArray cell) return new Pt((double)cell[0] / d.C.CoreHalf, (double)cell[1] / d.C.CoreHalf);
            double px = (double)place["x"] + d.Rng.Range(-0.015, 0.015);
            double py = (double)place["y"] + d.Rng.Range(-0.015, 0.015);
            return new Pt(px, py);
        }

        static double SizeOf(JToken size, Dictionary<string, double> table, string fallback)
        {
            if (size != null && (size.Type == JTokenType.Float || size.Type == JTokenType.Integer)) return (double)size;
            return table[(string)size ?? fallback];
        }

        static Pt ToCells(Draft d, Pt p) => new Pt(p.X * d.C.CoreHalf, p.Y * d.C.CoreHalf);
        static Pt ToFrac(Draft d, Pt p) => new Pt(p.X / d.C.CoreHalf, p.Y / d.C.CoreHalf);

        static double RelationPenalty(Draft d, JObject f, Pt p)
        {
            double pen = 0;
            var centres = d.Bp.Centres;
            if (f["near"] != null && centres.TryGetValue((string)f["near"], out var n))
                pen += Math.Max(0, ThemeGeometry.Hypot(p.X - n.X, p.Y - n.Y) - 0.3);
            var far = f["farFrom"];
            if (far != null)
            {
                string id = far.Type == JTokenType.String ? (string)far : (string)far["id"];
                double min = far.Type == JTokenType.String ? 0.5 : (double?)far["min"] ?? 0.5;
                if (centres.TryGetValue(id, out var t)) pen += Math.Max(0, min - ThemeGeometry.Hypot(p.X - t.X, p.Y - t.Y));
            }
            if (f["between"] is JArray b && centres.TryGetValue((string)b[0], out var a) && centres.TryGetValue((string)b[1], out var e))
                pen += Math.Max(0, ThemeGeometry.SegmentDistance(p, a, e) - 0.18);
            return pen;
        }

        // ---------- area features ----------

        static void PlaceAreaFeatures(Draft d)
        {
            int order = 0;
            // Lakeshore start: the theme's first water lake is the one the start sits beside.
            string shoreLake = d.Theme.StartSetting == "lakeshore"
                ? (string)d.Theme.Features.FirstOrDefault(f => (string)f["kind"] == "lake" && ((string)f["material"] ?? "water") == "water")?["id"]
                : null;
            foreach (var f in d.Theme.Features)
            {
                string kind = (string)f["kind"];
                if (kind != "zone" && kind != "mountains" && kind != "lake") continue;
                order++;
                PlaceArea(d, f, kind, order, shoreLake);
            }
        }

        static void PlaceArea(Draft d, JObject f, string kind, int order, string shoreLake)
        {
            bool isLake = kind == "lake";
            bool shore = isLake && (string)f["id"] == shoreLake;
            double r = SizeOf(f["size"], isLake ? LakeSize : ZoneSize, isLake ? "medium" : "large") * d.Rng.Range(0.9, 1.1);
            double stretch = (double?)f["stretch"] ?? DefaultStretch[kind];
            double angle = f["orientation"] != null ? Orient[(string)f["orientation"]] + d.Rng.Range(-0.15, 0.15) : d.Rng.Range(0, Math.PI);
            double rx = r * Math.Sqrt(stretch) * d.C.CoreHalf;
            double ry = r / Math.Sqrt(stretch) * d.C.CoreHalf;
            string material = isLake ? (string)f["material"] ?? "water" : "water";
            var best = BestSpot(d, f, p => Judge(d, f, p, (rx, ry, angle), isLake ? material : null, shore));
            var centre = ToCells(d, best);
            var blob = ThemeGeometry.MakeBlob(d.Rng, centre.X, centre.Y, rx, ry, angle, isLake ? 0.12 : 0.16);
            string id = (string)f["id"];
            d.Bp.Centres[id] = best;
            if (kind == "zone") d.Bp.Zones.Add(new BpZone { Id = id, Look = (string)f["biome"], Blob = blob, Priority = order });
            else if (kind == "mountains") AddMountains(d, f, id, blob, order);
            else AddLake(d, f, id, blob, material);
        }

        // An area's spot and how bad it is: lakes kept clear of the start (a lakeshore start's lake on its shore).
        static (Pt p, double pen) Judge(Draft d, JObject f, Pt p, (double rx, double ry, double angle) shape, string lakeMaterial, bool shore)
        {
            var (rx, ry, angle) = shape;
            if (shore)
            {
                p = OnShoreOfStart(d, p, rx, ry, angle);
                return (p, RelationPenalty(d, f, p) * 4 + ShoreOriginPenalty(d, p, rx, ry, angle));
            }
            if (lakeMaterial == null) return (p, RelationPenalty(d, f, p) * 4);
            p = ClearOfStart(d, p, rx, ry, lakeMaterial);
            return (p, RelationPenalty(d, f, p) * 4 + LakeStartPenalty(d, p, rx, ry, lakeMaterial));
        }

        // Up to 40 tries at a place against the feature's relations: the first that breaks nothing, else the least bad.
        static Pt BestSpot(Draft d, JObject f, Func<Pt, (Pt p, double pen)> judge)
        {
            Pt best = default;
            double bestPen = double.MaxValue;
            for (int tries = 0; tries < 40; tries++)
            {
                var p = PlacePoint(d, f["at"], (string)f["distance"]);
                if (f["near"] != null && tries % 2 == 1 && d.Bp.Centres.TryGetValue((string)f["near"], out var t))
                    p = new Pt(t.X + d.Rng.Range(-0.22, 0.22), t.Y + d.Rng.Range(-0.22, 0.22));
                var (q, pen) = judge(p);
                if (pen < bestPen)
                {
                    best = q;
                    bestPen = pen;
                }
                if (pen == 0) break;
            }
            return best;
        }

        // Foothills (highlands) round the range, the range itself, snow on its core.
        static void AddMountains(Draft d, JObject f, string id, Blob blob, int order)
        {
            d.Mountains.Add((id, blob));
            d.Bp.Zones.Add(new BpZone { Id = id + ":foothills", Look = "highlands", Blob = blob.Scaled(1.25, 1.45), Priority = 99 + order });
            d.Bp.Zones.Add(new BpZone { Id = id, Look = "mountains", Blob = blob, Priority = 100 + order });
            if ((bool?)f["peaks"] ?? true)
                d.Bp.Zones.Add(new BpZone { Id = id + ":peaks", Look = "snowpeaks", Blob = blob.Scaled(0.62, 0.55), Priority = 101 + order });
        }

        static void AddLake(Draft d, JObject f, string id, Blob blob, string material)
        {
            Blob island = null;
            string islandLook = "deepwood";
            var spec = f["island"];
            if (spec != null && !(spec.Type == JTokenType.Boolean && !(bool)spec))
            {
                var so = spec as JObject;
                double ir = SizeOf(so?["size"], IslandSize, "medium");
                double off = d.Rng.Range(0, 0.3);
                double a = d.Rng.Range(0, Math.PI * 2);
                double cx = blob.Cx + ThemeGeometry.Cos(a) * off * blob.Rx;
                double cy = blob.Cy + ThemeGeometry.Sin(a) * off * blob.Ry;
                double ry = blob.Ry * ir * d.Rng.Range(0.8, 1.2);
                island = ThemeGeometry.MakeBlob(d.Rng, cx, cy, blob.Rx * ir, ry, d.Rng.Range(0, Math.PI), 0.2);
                islandLook = (string)so?["biome"] ?? (material == "lava" ? "ashflats" : "deepwood");
            }
            d.Bp.Lakes.Add(new BpLake { Id = id, Material = material, Blob = blob, Island = island, IslandLook = islandLook });
        }

        /// <summary>Pushes a lake centre (fractions) straight away from the start until its outline keeps clear.</summary>
        static Pt ClearOfStart(Draft d, Pt p, double rx, double ry, string material)
        {
            double clear = material == "lava" ? StartHazardClear : StartWaterClear;
            double need = (clear + Math.Max(rx, ry) * 1.25) / d.C.CoreHalf;
            double r = ThemeGeometry.Hypot(p.X, p.Y);
            var q = p;
            if (r < need)
            {
                double a = r > 1e-6 ? ThemeGeometry.Atan2(p.Y, p.X) : d.Rng.Range(0, Math.PI * 2);
                q = new Pt(ThemeGeometry.Cos(a) * need, ThemeGeometry.Sin(a) * need);
            }
            // ...and clear of the start itself, with room for a start town.
            double needT = (StartTownHalf + 10 + Math.Max(rx, ry) * 1.25) / d.C.CoreHalf;
            double rt = ThemeGeometry.Hypot(q.X, q.Y);
            if (rt >= needT || rt == 0) return q;
            return new Pt(q.X / rt * needT, q.Y / rt * needT);
        }

        /// <summary>Lakeshore start: moves a lake along its bearing from the start so its near edge sits LakeshoreEdge cells away.</summary>
        static Pt OnShoreOfStart(Draft d, Pt p, double rx, double ry, double angle)
        {
            var m = ToCells(d, p);
            double a = ThemeGeometry.Hypot(m.X, m.Y) > 1e-6 ? ThemeGeometry.Atan2(m.Y, m.X) : d.Rng.Range(0, Math.PI * 2);
            double dist = LakeshoreEdge + EllipseRadius(rx, ry, a - angle) * 1.22;
            return ToFrac(d, new Pt(ThemeGeometry.Cos(a) * dist, ThemeGeometry.Sin(a) * dist));
        }

        static double EllipseRadius(double rx, double ry, double phi)
        {
            double c = ThemeGeometry.Cos(phi) / rx, s = ThemeGeometry.Sin(phi) / ry;
            return 1 / Math.Sqrt(c * c + s * s);
        }

        static double ShoreOriginPenalty(Draft d, Pt p, double rx, double ry, double angle)
        {
            var m = ToCells(d, p);
            double toOrigin = ThemeGeometry.Atan2(-m.Y, -m.X);
            double edge = ThemeGeometry.Hypot(m.X, m.Y) - EllipseRadius(rx, ry, toOrigin - angle) * 1.22 - 2.5;
            return Math.Max(0, 14 - edge) / d.C.CoreHalf * 10;
        }

        static double LakeStartPenalty(Draft d, Pt p, double rx, double ry, string material)
        {
            var m = ToCells(d, p);
            double reach = Math.Max(rx, ry) * 1.25;
            double gap = ThemeGeometry.Hypot(m.X, m.Y) - reach;
            double clear = material == "lava" ? StartHazardClear : StartWaterClear;
            return Math.Max(0, clear - gap) / d.C.CoreHalf * 3;
        }

        /// <summary>An implicit zone round the start painting the start setting's look.</summary>
        static void PlaceStartZone(Draft d)
        {
            string look = (string)d.Theme.Start["biome"] ?? StartSettingLook[d.Theme.StartSetting];
            if (look == null) return;
            string setting = d.Theme.StartSetting;
            double r = StartZoneRadius.TryGetValue(setting, out var sized) ? sized : 38;
            double cx = d.Rng.Range(-6, 6), cy = d.Rng.Range(-6, 6);
            double rx = r * d.Rng.Range(1, 1.2), ry = r * d.Rng.Range(0.85, 1);
            var blob = ThemeGeometry.MakeBlob(d.Rng, cx, cy, rx, ry, d.Rng.Range(0, Math.PI), 0.12);
            d.Bp.Zones.Add(new BpZone { Id = "start", Look = look, Blob = blob, Priority = 1000, Start = true });
        }

        // ---------- rivers ----------

        /// <summary>A point on the core's rim in a compass direction (fractions), jittered along the rim.</summary>
        static Pt RimPoint(Draft d, string side)
        {
            var (dx, dy) = Dirs[side];
            if (dx != 0 && dy != 0)
            {
                double t = d.Rng.Range(-0.22, 0.22);
                return new Pt(dx * 0.94 + (dx > 0 ? -1 : 1) * Math.Max(0, t), dy * 0.94 + (dy > 0 ? -1 : 1) * Math.Max(0, -t));
            }
            double along = d.Rng.Range(-0.35, 0.35);
            return new Pt(dx != 0 ? dx * 0.97 : along, dy != 0 ? dy * 0.97 : along);
        }

        static (Pt p, bool rim) RiverEnd(Draft d, JToken place) =>
            place.Type == JTokenType.String && (string)place != "center" ? (RimPoint(d, (string)place), true) : (PlacePoint(d, place, "mid"), false);

        static void PlaceRiver(Draft d, JObject f)
        {
            string material = (string)f["material"] ?? "water";
            var from = RiverEnd(d, f["from"]);
            var to = RiverEnd(d, f["to"]);
            var anchors = new List<Pt> { ToCells(d, from.p) };
            foreach (var id in f["through"] as JArray ?? new JArray())
            {
                var lake = d.Bp.Lakes.FirstOrDefault(l => l.Id == (string)id);
                if (lake != null) anchors.Add(new Pt(lake.Blob.Cx, lake.Blob.Cy));
            }
            anchors.Add(ToCells(d, to.p));
            if (d.Theme.StartSetting == "riverbank" && d.Bp.Rivers.Count == 0) BendPastStart(d, anchors);
            var pts = ThemeGeometry.MeanderCurve(d.Rng, anchors, MeanderOf[(string)f["meander"] ?? "medium"], 3);
            // Keep the start clear (lava much further than water).
            double clear = d.Theme.StartSetting == "riverbank" ? RiverbankClear : StartWaterClear;
            if (material == "lava") clear = StartHazardClear + 6;
            double townClear = StartTownHalf + (d.Theme.StartSetting == "riverbank" ? 5 : 10);
            pts = pts.Select(p => PushFrom(PushFrom(PushFrom(p, clear), townClear), clear)).ToList();
            double half = RiverHalf[(string)f["width"] ?? "medium"];
            var halves = pts.Select(_ => half).ToList();
            double band = d.C.FadeEnd - d.C.CoreHalf;
            if (band > 0)
            {
                // Tails into the fade band, tapering to nothing, so a river doesn't stop dead at the core's rim.
                var head = Tail(d, pts[0], from.rim, half, band);
                var foot = Tail(d, pts[pts.Count - 1], to.rim, half, band);
                head.pts.Reverse();
                head.half.Reverse();
                pts = head.pts.Concat(pts).Concat(foot.pts).ToList();
                halves = head.half.Concat(halves).Concat(foot.half).ToList();
            }
            // Never reach past the fade band.
            double limit = d.C.FadeEnd - 16;
            int lo = pts.FindIndex(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) < limit);
            int hi = pts.FindLastIndex(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) < limit);
            var path = new GeoPath
            {
                Points = pts.Skip(lo).Take(hi - lo + 1).ToArray(),
                Half = halves.Skip(lo).Take(hi - lo + 1).ToArray(),
            };
            string rid = (string)f["id"];
            d.Bp.Rivers.Add(new BpRiver { Id = rid, Material = material, Path = path, Anchors = anchors });
            d.Bp.Centres[rid] = ToFrac(d, anchors[anchors.Count / 2]);
        }

        static Pt PushFrom(Pt p, double r0)
        {
            double r = ThemeGeometry.Hypot(p.X, p.Y);
            return r >= r0 || r == 0 ? p : new Pt(p.X / r * r0, p.Y / r * r0);
        }

        // Riverbank start: bend the first river past the start, a short walk away.
        static void BendPastStart(Draft d, List<Pt> anchors)
        {
            var a = anchors[0];
            var b = anchors[anchors.Count - 1];
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double len = ThemeGeometry.Hypot(vx, vy);
            if (len == 0) len = 1;
            double side = d.Rng.Next() < 0.5 ? -1 : 1;
            double off = d.Rng.Range(RiverbankClear + 4, RiverbankClear + 10) * side;
            var bank = new Pt(-vy / len * off, vx / len * off);
            double Proj(Pt p) => (p.X - a.X) * vx + (p.Y - a.Y) * vy;
            int at = 1;
            while (at < anchors.Count - 1 && Proj(anchors[at]) < Proj(bank)) at++;
            anchors.Insert(at, bank);
        }

        static (List<Pt> pts, List<double> half) Tail(Draft d, Pt end, bool rim, double half, double band)
        {
            var pts = new List<Pt>();
            var halves = new List<double>();
            if (!rim) return (pts, halves);
            double r = ThemeGeometry.Hypot(end.X, end.Y);
            if (r == 0) r = 1;
            double len = Math.Min(band * 0.7, 150);
            for (double s = 4; s <= len; s += 4)
            {
                double wob = ThemeGeometry.Sin(s / 17 + d.Rng.Range(0, 0.2)) * 5;
                pts.Add(new Pt(end.X + end.X / r * s - end.Y / r * wob, end.Y + end.Y / r * s + end.X / r * wob));
                halves.Add(half * (1 - s / len) + 0.35 * (s / len));
            }
            return (pts, halves);
        }

        // ---------- influences ----------

        /// <summary>
        /// Every influence, after everything it can refer to is placed. Draws from the stream only here,
        /// at the end, so adding an influence to a theme never moves its other features.
        /// </summary>
        static void PlaceInfluences(Draft d)
        {
            int order = 0;
            foreach (var f in d.Theme.Features.Where(x => (string)x["kind"] == "influence"))
            {
                var inf = new BpInfluence
                {
                    Id = (string)f["id"],
                    Look = (string)f["look"],
                    Strength = (double?)f["strength"] ?? 1,
                    Falloff = (double?)f["falloff"] ?? 0.5,
                    Edge = (double?)f["edge"] ?? 0.2,
                    Order = order++,
                };
                Shape(d, f, inf);
                if (f["patches"] is JObject patches)
                {
                    inf.PatchShare = (double)patches["share"];
                    inf.PatchLattice = PatchLattice[(string)patches["size"] ?? "small"];
                }
                if (f["heart"] is JObject heart)
                {
                    inf.HeartTerrain = (string)heart["terrain"];
                    inf.HeartAbove = (double?)heart["above"] ?? 0.75;
                }
                Onto(f["onto"], inf);
                d.Bp.Influences.Add(inf);
            }
        }

        static void Onto(JToken onto, BpInfluence inf)
        {
            if (onto == null || (string)onto == "filler") inf.OntoFiller = true;
            else if (onto.Type == JTokenType.String && (string)onto == "any")
            {
                inf.OntoAny = true;
                inf.OntoFiller = true;
            }
            else
            {
                var list = ((JArray)onto).Select(x => (string)x).ToList();
                inf.OntoFiller = list.Contains("filler");
                inf.OntoLooks = list.Where(x => x != "filler").ToList();
            }
        }

        static void Shape(Draft d, JObject f, BpInfluence inf)
        {
            inf.Reach = ((double?)f["reach"] ?? 0.2) * d.C.CoreHalf;
            if ((string)f["area"] == "continent")
            {
                inf.Shape = "continent";
                inf.Reach = 0;
                return;
            }
            if (f["along"] != null)
            {
                // A river; a road has no geometry yet (roads aren't ported), so its field is empty.
                var river = d.Bp.Rivers.FirstOrDefault(r => r.Id == (string)f["along"]);
                inf.Shape = "along";
                inf.Path = river?.Path ?? new GeoPath();
                return;
            }
            if (f["around"] != null)
            {
                string id = (string)f["around"];
                var blob = d.Bp.Zones.FirstOrDefault(z => z.Id == id)?.Blob
                    ?? d.Bp.Lakes.FirstOrDefault(l => l.Id == id)?.Blob
                    ?? d.Mountains.FirstOrDefault(m => m.id == id).blob
                    // A settlement or the start: a small disc (settlements aren't ported: at the start).
                    ?? new Blob { Cx = 0, Cy = 0, Rx = 5, Ry = 5 };
                inf.Shape = "around";
                inf.Blob = blob;
                return;
            }
            // A placed ellipse, against its relations like a zone.
            double r = SizeOf(f["size"], ZoneSize, "large") * d.Rng.Range(0.9, 1.1);
            double stretch = (double?)f["stretch"] ?? 1.3;
            double angle = f["orientation"] != null ? Orient[(string)f["orientation"]] + d.Rng.Range(-0.15, 0.15) : d.Rng.Range(0, Math.PI);
            var best = BestSpot(d, f, p => (p, RelationPenalty(d, f, p)));
            d.Bp.Centres[(string)f["id"]] = best;
            var centre = ToCells(d, best);
            inf.Shape = "blob";
            inf.Reach = 0;
            inf.Blob = ThemeGeometry.MakeBlob(d.Rng, centre.X, centre.Y, r * Math.Sqrt(stretch) * d.C.CoreHalf, r / Math.Sqrt(stretch) * d.C.CoreHalf, angle, 0.16);
        }
    }
}
