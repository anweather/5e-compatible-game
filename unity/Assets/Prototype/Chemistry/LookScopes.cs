// Scoped and mixed looks (docs/design/world-tools.md, "Scoped and mixed looks";
// docs/RULES_MCP.md "Looks"). A look of an element, cover, gas or terrain may give
// a "mix" (several colours with weights in place of its one colour, each cell
// picking by a hash of where it is) and "scoped" looks (a colour or a mix that
// holds only around a place, a cell or a player, the latest that covers a cell
// winning, its edge blending over EdgeCells), and looks "by" player (a colour or
// mix for what one player's casts make, or one colour per player in join order),
// which win over everything else. The view's only: looks stay out of
// Rules.Fingerprint and the cell hash, and nothing here draws from the world's RNG,
// so every peer sees the same patchwork from the same rules.
//
// LookScopes compiles the rules' looks once a rule switch (a place's cells and
// the distance round it are worked out then); CellLook.At is the pure function
// the view calls per cell, which allocates nothing.
//
// Not a port: the sandbox has no looks.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>A colour, each channel 0 to 1.</summary>
    public readonly struct LookColour
    {
        public readonly float R, G, B;

        public LookColour(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static LookColour Of(LookDef look) => look == null ? new LookColour(1, 1, 1) : new LookColour(look.R, look.G, look.B);

        /// <summary>Whether two colours are the same to within a hair.</summary>
        public bool Near(LookColour o) => Math.Abs(R - o.R) < 1e-5f && Math.Abs(G - o.G) < 1e-5f && Math.Abs(B - o.B) < 1e-5f;
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "[{0:0.###}, {1:0.###}, {2:0.###}]", R, G, B);

        internal static LookColour? Parse(JToken t) =>
            t is JArray c && c.Count == 3 && c.All(IsNumber) ? new LookColour((float)c[0], (float)c[1], (float)c[2]) : (LookColour?)null;

        internal static bool IsNumber(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);
    }

    /// <summary>
    /// A player in the game: the name the core keys players by (what an "around" look follows), their
    /// slot (0, 1, 2... in join order, kept while they stay: what a "by" look names) and where they stand.
    /// </summary>
    public readonly struct PlayerSpot
    {
        public readonly string Player;
        public readonly int Slot;
        public readonly int X, Y;

        public PlayerSpot(string player, int x, int y, int slot = -1)
        {
            Player = player;
            Slot = slot;
            X = x;
            Y = y;
        }
    }

    /// <summary>A look's "mix": several colours with weights, one picked per cell.</summary>
    public sealed class LookMix
    {
        /// <summary>Weights are kept as whole thousandths, so a pick is integer maths.</summary>
        const double Scale = 1000;

        readonly int[] upTo;

        LookMix(LookColour[] colours, int[] weights)
        {
            Colours = colours;
            upTo = new int[weights.Length];
            for (int i = 0, sum = 0; i < weights.Length; i++) upTo[i] = sum += weights[i];
        }

        public IReadOnlyList<LookColour> Colours { get; }
        public int Total => upTo[upTo.Length - 1];

        /// <summary>The share of cells that pick colour <paramref name="i"/>, 0 to 1.</summary>
        public double Share(int i) => (double)(upTo[i] - (i == 0 ? 0 : upTo[i - 1])) / Total;

        /// <summary>The colour a cell with this hash picks.</summary>
        public LookColour Pick(uint hash)
        {
            int v = (int)(hash % (uint)Total);
            for (int i = 0; i < upTo.Length; i++)
                if (v < upTo[i]) return Colours[i];
            return Colours[Colours.Count - 1];
        }

        /// <summary>
        /// A mix as a look gives it: [{"color": [r, g, b], "weight": 2}, ...], or plain colours
        /// [[r, g, b], ...] weighing the same. Null when it isn't one (LookScopes.ShapeProblems says why).
        /// </summary>
        public static LookMix Parse(JToken t)
        {
            if (!(t is JArray items) || items.Count == 0) return null;
            var colours = new LookColour[items.Count];
            var weights = new int[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var colour = LookColour.Parse(item is JObject o ? o["color"] : item);
                double weight = 1;
                if (item is JObject w && w["weight"] != null) weight = LookColour.IsNumber(w["weight"]) ? (double)w["weight"] : -1;
                if (colour == null || weight <= 0) return null;
                colours[i] = colour.Value;
                weights[i] = Math.Max(1, (int)Math.Round(weight * Scale));
            }
            return new LookMix(colours, weights);
        }
    }

    /// <summary>A scoped look's "where": around a place, a cell or a player, within so many cells.</summary>
    public sealed class LookWhere
    {
        public string Place;
        public int X, Y;
        public bool At;
        /// <summary>The player it follows, by name; the look lapses while they aren't in the game.</summary>
        public string Around;
        public int Within;

        public static LookWhere Parse(JToken t)
        {
            if (!(t is JObject o)) return null;
            var w = new LookWhere { Place = Str(o["place"]), Around = Str(o["around"]), Within = LookColour.IsNumber(o["within"]) ? (int)Math.Round((double)o["within"]) : 0 };
            if (o["at"] is JArray at && at.Count == 2 && at.All(LookColour.IsNumber))
            {
                w.At = true;
                w.X = (int)at[0];
                w.Y = (int)at[1];
            }
            return w;
        }

        public override string ToString()
        {
            string what = string.Format(CultureInfo.InvariantCulture, "cell ({0}, {1})", X, Y);
            if (Place != null) what = $"the place {Place}";
            else if (Around != null) what = $"the player {Around}";
            return Within > 0 ? $"within {Within} cells of {what}" : $"on {what}";
        }

        static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;
    }

    /// <summary>One of a look's "scoped" looks: a colour or a mix that holds only where it says.</summary>
    public sealed class ScopedLook
    {
        /// <summary>A "by" look's slot when it gives "each": one colour per player, by slot.</summary>
        public const int Each = -1;

        /// <summary>Where it holds; null for a look by player.</summary>
        public LookWhere Where;
        /// <summary>For a look by player (Where null): the slot whose casts it colours, or Each.</summary>
        public int By;
        public LookColour Colour;
        /// <summary>Null when it gives one colour.</summary>
        public LookMix Mix;

        /// <summary>The scoped looks a look gives, earliest first; the ones that can't be read are left out (Rules.Check refuses them).</summary>
        public static IReadOnlyList<ScopedLook> ParseAll(JToken t)
        {
            if (!(t is JArray list)) return Array.Empty<ScopedLook>();
            var all = new List<ScopedLook>();
            foreach (var o in list.OfType<JObject>())
            {
                var where = LookWhere.Parse(o["where"]);
                var colour = LookColour.Parse(o["color"]);
                var mix = LookMix.Parse(o["mix"]);
                int? by = BySlot(o["by"]);
                if ((where == null) == (by == null) || (colour == null && mix == null) || (by == Each && mix == null)) continue;
                all.Add(new ScopedLook { Where = where, By = by ?? 0, Colour = colour ?? mix.Colours[0], Mix = mix });
            }
            return all;
        }

        /// <summary>A "by" as a slot: a whole number from 0, or Each for "each"; null if it's neither.</summary>
        public static int? BySlot(JToken t)
        {
            if (t == null) return null;
            if (t.Type == JTokenType.String && (string)t == "each") return Each;
            return t.Type == JTokenType.Integer && (long)t >= 0 && (long)t < int.MaxValue ? (int)(long)t : (int?)null;
        }

        /// <summary>What it is in words, for describe.</summary>
        public override string ToString()
        {
            if (Where != null) return Where.ToString();
            return By == Each ? "by each player, one colour per slot in join order" : string.Format(CultureInfo.InvariantCulture, "by the player in slot {0}", By);
        }
    }

    /// <summary>
    /// The rules' looks compiled for drawing: for each element, cover, gas and terrain, its plain look
    /// (one colour or a mix) and its scoped looks, with each place's distances worked out.
    /// </summary>
    public sealed class LookScopes
    {
        /// <summary>A scope's edge blends from its look into what's under it over this many cells.</summary>
        public const int EdgeCells = 3;

        /// <summary>The tables a look may be scoped by place or mixed in, by the names set_look takes.</summary>
        public static readonly string[] Tables = { "elements", "covers", "gases", "terrain" };

        /// <summary>The tables a look may be given by player in: what a cast makes, and the cast's own show.</summary>
        public static readonly string[] ByTables = { "elements", "covers", "gases", "spells" };

        readonly Dictionary<string, CellLook> looks = new Dictionary<string, CellLook>(StringComparer.Ordinal);
        readonly Dictionary<string, PlaceField> fields = new Dictionary<string, PlaceField>(StringComparer.Ordinal);
        readonly WorldPlaces places;

        LookScopes(WorldPlaces places) { this.places = places ?? WorldPlaces.Empty; }

        /// <summary>Compiles every look in the rules; a "where" naming a place the world doesn't have never holds.</summary>
        public static LookScopes Compile(Rules rules, WorldPlaces places)
        {
            var s = new LookScopes(places);
            foreach (var p in rules.ElementLooks.OrderBy(p => p.Key, StringComparer.Ordinal)) s.Add("elements", p.Key, p.Value);
            foreach (var p in rules.Covers.OrderBy(p => p.Key, StringComparer.Ordinal)) s.Add("covers", p.Key, p.Value.Look);
            foreach (var p in rules.Gases.OrderBy(p => p.Key, StringComparer.Ordinal)) s.Add("gases", p.Key, p.Value.Look);
            foreach (var p in rules.Terrain.OrderBy(p => p.Key, StringComparer.Ordinal)) s.Add("terrain", p.Key, p.Value.Look);
            foreach (var spell in rules.Spells) s.Add("spells", spell.Id, spell.Look);
            return s;
        }

        /// <summary>
        /// The look of an element, cover, gas or terrain by id, to call At on per cell; one with no look
        /// draws white. Look it up once a frame, not per cell.
        /// </summary>
        public CellLook Of(string table, string id) =>
            looks.TryGetValue(Key(table, id), out var look) ? look : new CellLook(LookColour.Of(null), null, Array.Empty<CellLook.Scope>(), Array.Empty<CellLook.ByPlayer>(), Salt(table, id));

        /// <summary>Whether any look is mixed or scoped: if not, the view can draw as it always did.</summary>
        public bool Any => looks.Values.Any(l => !l.Plain);

        static string Key(string table, string id) => table + "/" + id;

        void Add(string table, string id, LookDef look)
        {
            if (look == null) return;
            uint salt = Salt(table, id);
            var scopes = new List<CellLook.Scope>();
            var by = new List<CellLook.ByPlayer>();
            for (int i = 0; i < look.Scoped.Count; i++)
            {
                var s = look.Scoped[i];
                if (s.Where == null)
                {
                    if (ByTables.Contains(table)) by.Add(new CellLook.ByPlayer(s, Mixed(salt, i + 1)));
                    continue;
                }
                if (table == "spells") continue;
                var field = s.Where.Place != null ? Field(s.Where.Place, s.Where.Within) : null;
                if (s.Where.Place != null && field == null) continue;
                scopes.Add(new CellLook.Scope(s, field, Mixed(salt, i + 1)));
            }
            looks[Key(table, id)] = new CellLook(LookColour.Of(look), table == "spells" ? null : look.Mix, scopes.ToArray(), by.ToArray(), salt);
        }

        PlaceField Field(string placeId, int within)
        {
            string key = placeId + "@" + within.ToString(CultureInfo.InvariantCulture);
            if (fields.TryGetValue(key, out var field)) return field;
            var place = places.Find(placeId);
            return fields[key] = place == null ? null : new PlaceField(place, within + EdgeCells);
        }

        // FNV-1a over "table/id", so two looks mixing the same colours don't make the same patchwork.
        static uint Salt(string table, string id)
        {
            unchecked
            {
                uint h = 0x811c9dc5;
                foreach (char c in Key(table, id)) h = (h ^ c) * 0x01000193u;
                return h;
            }
        }

        static uint Mixed(uint salt, int n) => unchecked(salt ^ ((uint)n * 0x9E3779B9u));

        /// <summary>A cell's hash for a mix's pick: integer maths on where it is, nothing from the world's RNG.</summary>
        public static uint CellHash(int x, int y, uint salt)
        {
            unchecked
            {
                uint h = salt ^ ((uint)x * 0x9E3779B1u) ^ (((uint)y + 0x632BE5ABu) * 0x85EBCA77u);
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        // ---------- the shape a look's mix and scoped looks must have (Rules.Check) ----------

        /// <summary>What's wrong with a look's "mix" and "scoped", one terse message each (the caller names the look).</summary>
        public static IEnumerable<string> ShapeProblems(JObject look)
        {
            if (look["mix"] != null && LookMix.Parse(look["mix"]) == null) yield return MixShape("look.mix");
            if (look["scoped"] == null) yield break;
            if (!(look["scoped"] is JArray scoped))
            {
                yield return "look.scoped must be a list of {\"where\" or \"by\": ..., \"color\" or \"mix\"}";
                yield break;
            }
            for (int i = 0; i < scoped.Count; i++)
            {
                string at = $"look.scoped[{i}]";
                if (!(scoped[i] is JObject s))
                {
                    yield return $"{at} must be an object with where or by, and a color or mix";
                    continue;
                }
                foreach (var key in s.Properties().Select(p => p.Name).Where(k => k != "where" && k != "by" && k != "color" && k != "mix"))
                    yield return $"{at}.{key} unknown (a scoped look gives where or by, and a color or a mix)";
                if ((s["where"] == null) == (s["by"] == null)) yield return $"{at} gives one of where (a place, a cell or round a player) or by (a player's slot, or each)";
                if (s["by"] != null && ScopedLook.BySlot(s["by"]) == null) yield return $"{at}.by must be a player's slot (a whole number from 0) or \"each\"";
                if (ScopedLook.BySlot(s["by"]) == ScopedLook.Each && s["mix"] == null) yield return $"{at}: by each needs a mix, one colour per player";
                if (s["color"] == null && s["mix"] == null) yield return $"{at} needs a color or a mix";
                if (s["color"] != null && s["mix"] != null) yield return $"{at} gives both a color and a mix; give one";
                if (s["color"] != null && !(s["color"] is JArray c && c.Count == 3 && c.All(x => LookColour.IsNumber(x) && (double)x >= 0 && (double)x <= 1)))
                    yield return $"{at}.color must be three numbers from 0 to 1";
                if (s["mix"] != null && LookMix.Parse(s["mix"]) == null) yield return MixShape($"{at}.mix");
                if (s["where"] != null)
                    foreach (var m in WhereProblems(s["where"])) yield return $"{at}.where {m}";
            }
        }

        static string MixShape(string what) => $"{what} must be a list of colours with weights, such as [{{\"color\": [1, 0.3, 0.1], \"weight\": 2}}, {{\"color\": [0.2, 0.4, 1], \"weight\": 1}}]";

        static IEnumerable<string> WhereProblems(JToken t)
        {
            if (!(t is JObject w))
            {
                yield return "must be an object: {\"place\": id, \"within\": n}, {\"at\": [x, y], \"within\": n} or {\"around\": player, \"within\": n}";
                yield break;
            }
            int named = (w["place"] != null ? 1 : 0) + (w["at"] != null ? 1 : 0) + (w["around"] != null ? 1 : 0);
            if (named != 1) yield return "names one of place, at or around";
            foreach (var key in w.Properties().Select(p => p.Name).Where(k => k != "place" && k != "at" && k != "around" && k != "within"))
                yield return $"{key} unknown (place, at, around, within)";
            if (w["place"] != null && w["place"].Type != JTokenType.String) yield return "place must be a place's id";
            if (w["around"] != null && w["around"].Type != JTokenType.String) yield return "around must be a player's name";
            if (w["at"] != null && !(w["at"] is JArray a && a.Count == 2 && a.All(x => x.Type == JTokenType.Integer && (int)x >= 0)))
                yield return "at must be a cell [x, y]";
            var within = w["within"];
            if (within != null && !(within.Type == JTokenType.Integer && (int)within >= 0)) yield return "within must be a whole number of cells from 0";
            if (within == null && w["place"] == null) yield return "needs within (cells) round a cell or a player";
        }
    }

    /// <summary>
    /// How far each cell round a place is from it (0 inside), over the place's bounds grown by a reach;
    /// worked out once, when the looks are compiled.
    /// </summary>
    sealed class PlaceField
    {
        readonly int x0, y0, w, h;
        readonly float[] distance;

        public PlaceField(Place place, int reach)
        {
            x0 = place.Bounds.X - reach;
            y0 = place.Bounds.Y - reach;
            w = place.Bounds.W + 2 * reach;
            h = place.Bounds.H + 2 * reach;
            distance = new float[w * h];
            // Dead reckoning: each cell keeps the nearest place cell found, passed on to its neighbours
            // in a forward and a backward sweep, so the distance is (nearly) true Euclidean.
            var near = new int[w * h];
            for (int i = 0; i < near.Length; i++) near[i] = place.Contains(x0 + i % w, y0 + i / w) ? i : -1;
            Sweep(near, 1, new[] { (-1, -1), (0, -1), (1, -1), (-1, 0) });
            Sweep(near, -1, new[] { (1, 1), (0, 1), (-1, 1), (1, 0) });
            for (int i = 0; i < near.Length; i++) distance[i] = near[i] < 0 ? float.PositiveInfinity : (float)Dist(i, near[i]);
        }

        void Sweep(int[] near, int dir, (int dx, int dy)[] from)
        {
            for (int n = 0; n < near.Length; n++)
            {
                int i = dir > 0 ? n : near.Length - 1 - n, x = i % w, y = i / w;
                foreach (var (dx, dy) in from)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int source = near[ny * w + nx];
                    if (source >= 0 && (near[i] < 0 || Dist(i, source) < Dist(i, near[i]))) near[i] = source;
                }
            }
        }

        double Dist(int a, int b)
        {
            int dx = a % w - b % w, dy = a / w - b / w;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>How far the cell is from the place; infinite past the reach.</summary>
        public float At(int x, int y)
        {
            int i = x - x0, j = y - y0;
            return i < 0 || j < 0 || i >= w || j >= h ? float.PositiveInfinity : distance[j * w + i];
        }
    }

    /// <summary>One element's, cover's, gas's, terrain's or spell's look, compiled: At gives a cell's colour.</summary>
    public sealed class CellLook
    {
        // A look by player: the slot whose casts it colours, or every slot, one colour each.
        internal sealed class ByPlayer
        {
            readonly int slot;
            readonly LookColour colour;
            readonly LookMix mix;
            readonly uint salt;

            public ByPlayer(ScopedLook look, uint salt)
            {
                slot = look.By;
                colour = look.Colour;
                mix = look.Mix;
                this.salt = salt;
            }

            // The colour for what this slot cast, or null if this look isn't for that slot.
            public LookColour? For(int caster, int x, int y)
            {
                if (slot == ScopedLook.Each) return mix.Colours[caster % mix.Colours.Count];
                if (slot != caster) return null;
                return mix == null ? colour : mix.Pick(LookScopes.CellHash(x, y, salt));
            }
        }

        internal sealed class Scope
        {
            readonly LookWhere where;
            readonly PlaceField field;
            readonly LookColour colour;
            readonly LookMix mix;
            readonly uint salt;

            public Scope(ScopedLook look, PlaceField field, uint salt)
            {
                where = look.Where;
                this.field = field;
                colour = look.Colour;
                mix = look.Mix;
                this.salt = salt;
            }

            // 1 inside the scope, falling to 0 over EdgeCells past it; 0 for a player not in the game.
            public float Weight(int x, int y, IReadOnlyList<PlayerSpot> players)
            {
                double d;
                if (field != null) d = field.At(x, y);
                else if (where.At) d = Hypot(x - where.X, y - where.Y);
                else
                {
                    d = double.PositiveInfinity;
                    if (players != null)
                        for (int i = 0; i < players.Count; i++)
                            if (string.Equals(players[i].Player, where.Around, StringComparison.Ordinal))
                            {
                                d = Hypot(x - players[i].X, y - players[i].Y);
                                break;
                            }
                }
                if (d <= where.Within) return 1;
                return (float)Math.Max(0, Math.Min(1, (where.Within + LookScopes.EdgeCells - d) / LookScopes.EdgeCells));
            }

            public LookColour ColourAt(int x, int y) => mix == null ? colour : mix.Pick(LookScopes.CellHash(x, y, salt));

            static double Hypot(int dx, int dy) => Math.Sqrt((double)dx * dx + (double)dy * dy);
        }

        readonly LookColour colour;
        readonly LookMix mix;
        readonly Scope[] scopes;
        readonly ByPlayer[] by;
        readonly uint salt;

        internal CellLook(LookColour colour, LookMix mix, Scope[] scopes, ByPlayer[] by, uint salt)
        {
            this.colour = colour;
            this.mix = mix;
            this.scopes = scopes;
            this.by = by;
            this.salt = salt;
        }

        /// <summary>One colour everywhere, no mix, no scopes and none by player: the view needn't call At per cell.</summary>
        public bool Plain => mix == null && scopes.Length == 0 && by.Length == 0;

        /// <summary>Looks by player: the view needs to know who cast what this draws.</summary>
        public bool ByPlayers => by.Length > 0;

        /// <summary>Scoped looks that hold somewhere (one naming a place the world lacks is left out).</summary>
        public int Scopes => scopes.Length;

        /// <summary>
        /// The colour at cell (x, y), strongest first: the latest look by the player whose cast made
        /// what's there (<paramref name="caster"/>, a slot; it lapses once that slot isn't in
        /// <paramref name="players"/>), else a spell's own colour if the thing there has one (J-052), else
        /// the latest scoped look covering the cell, blended at its edge into the earlier ones and the
        /// plain look under them; a mix picks by a hash of the cell. <paramref name="players"/> is who is
        /// in the game and where each stands. Allocates nothing.
        /// </summary>
        public LookColour At(int x, int y, IReadOnlyList<PlayerSpot> players, LookColour? own = null, int? caster = null)
        {
            if (caster is int slot && slot >= 0 && by.Length > 0 && InGame(players, slot))
                for (int i = by.Length - 1; i >= 0; i--)
                    if (by[i].For(slot, x, y) is LookColour mine) return mine;
            if (own.HasValue) return own.Value;
            float r = 0, g = 0, b = 0, left = 1;
            for (int i = scopes.Length - 1; i >= 0; i--)
            {
                float w = scopes[i].Weight(x, y, players);
                if (w <= 0) continue;
                var c = scopes[i].ColourAt(x, y);
                if (left >= 1 && w >= 1) return c;
                r += left * w * c.R;
                g += left * w * c.G;
                b += left * w * c.B;
                left *= 1 - w;
                if (left <= 0) return new LookColour(r, g, b);
            }
            var plain = mix == null ? colour : mix.Pick(LookScopes.CellHash(x, y, salt));
            if (left >= 1) return plain;
            return new LookColour(r + left * plain.R, g + left * plain.G, b + left * plain.B);
        }

        static bool InGame(IReadOnlyList<PlayerSpot> players, int slot)
        {
            if (players == null) return false;
            for (int i = 0; i < players.Count; i++)
                if (players[i].Slot == slot) return true;
            return false;
        }
    }
}
