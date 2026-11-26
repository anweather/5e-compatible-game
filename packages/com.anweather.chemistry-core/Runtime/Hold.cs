// The Hold's data (docs/design/the-hold.md, phase 2): rules/hold.json, a map's hold.json, and a raider's adjectives.
// An adjective is a row: a raider with one is an entity of its own, "kind~adjective", made from the two when asked for
// (Rules.Entity), so its name, its weak and its resist reach everything that reads a raider, and nothing there changes.
// Nothing here names a castle or a raider: a map says what's fought over, the rules what comes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class AdjectiveDef
    {
        public string Id, Name, Keeps;
        public int Hp = 100;
        public int Damage = 100;
        public bool Lights;
        public bool Boss;
        public IReadOnlyList<string> Weak = Array.Empty<string>(), Resist = Array.Empty<string>();
        public IReadOnlyList<string> Never = Array.Empty<string>();
        public double[] Color = Array.Empty<double>();
    }

    public sealed class WaveThemeDef
    {
        public string Id, Name;
        public IReadOnlyList<string> Adjectives = Array.Empty<string>();
    }

    public sealed class MomentDef
    {
        public string Id, Name, Theme;
        public readonly string[] Holds = new string[HandsDef.Count];
        public int Wave = 1, Health, Broken;
    }

    public readonly struct WavePlan
    {
        public WavePlan(int wave, int count, string theme, bool boss, int kinds)
        {
            Wave = wave;
            Count = count;
            Theme = theme;
            Boss = boss;
            Kinds = kinds;
        }

        public int Wave { get; }
        public int Count { get; }
        public string Theme { get; }
        public bool Boss { get; }
        public int Kinds { get; }
    }

    public sealed class HoldParams
    {
        public int Health = 100, PlateTicks = 6, First = 4, Growth = 2, HpPerWave = 2, Warmup = 60, SpawnEvery = 9, BetweenWaves = 90;
        public int WaveTicks, Waves;
        public int TowerReach = 3;
        public int BatterTicks = 12, BatterCost = 12, Margin = 14, AdjectiveFrom = 2, AdjectivePercent = 34;
        public int DamagePerWave, KindEvery = 1, AdjectivePerWave, AdjectiveMost = 100, ThemeFrom = 1, ThemePercent, BossEvery, BossHpPerWave, TargetMinutes = 20;
        public string BossAdjective;
        public int OffRoadCost = 1, RouteEvery = 1;
        public IReadOnlyList<string> FlyerLands = Array.Empty<string>();
        public string BarricadeTerrain;
        public string Rubble = "rubble";
        public int BarricadeFrom, BarricadeFirst, BarricadePerWaves = 1, BarricadeMost, BarricadeRadius;
        public IReadOnlyList<int> PeerPercent = new[] { 100 };
        public IReadOnlyList<int> BossPeerHealth = new[] { 100 };
        public IReadOnlyList<WaveThemeDef> Themes = Array.Empty<WaveThemeDef>();
        public IReadOnlyList<MomentDef> Moments = Array.Empty<MomentDef>();
        public IReadOnlyList<UpgradeDef> Upgrades = Array.Empty<UpgradeDef>();
        public IReadOnlyList<UnlockDef> Unlocks = Array.Empty<UnlockDef>();
        public int Offer, PointsPerWave = 1;
        public IReadOnlyDictionary<string, int> Strength = new Dictionary<string, int>();
        public IReadOnlyList<AdjectiveDef> Adjectives = Array.Empty<AdjectiveDef>();
        public HandsDef Hands = new HandsDef();

        static UpgradeDef ParseUpgrade(JProperty p) => new UpgradeDef
        {
            Id = p.Name,
            Name = (string)p.Value["name"] ?? p.Name,
            Holds = (string)p.Value["holds"],
            Gesture = HandsDef.GestureOf((string)p.Value["gesture"]),
            BothHands = (string)p.Value["hands"] == "both",
            Cooldown = (int?)p.Value["cooldown"] ?? 0,
            BothCooldown = (int?)p.Value["bothCooldown"] ?? 0,
            Damage = (int?)p.Value["damage"] ?? 0,
            Range = (int?)p.Value["range"] ?? 0,
            Health = (int?)p.Value["health"] ?? 0,
        };

        static UnlockDef ParseUnlock(JObject o) => new UnlockDef
        {
            Id = (string)o["id"],
            Name = (string)o["name"] ?? (string)o["id"],
            Cost = (int?)o["cost"] ?? 0,
            Gesture = HandsDef.GestureOf((string)o["gesture"]),
            Hand = (string)o["hand"],
            Element = (string)o["element"],
            Kit = (string)o["kit"],
        };

        public UpgradeDef Upgrade(string id) => Upgrades.FirstOrDefault(u => u.Id == id);

        static MomentDef ParseMoment(JProperty p)
        {
            var m = new MomentDef
            {
                Id = p.Name,
                Name = (string)p.Value["name"] ?? p.Name,
                Theme = (string)p.Value["theme"],
                Wave = (int?)p.Value["wave"] ?? 1,
                Health = (int?)p.Value["health"] ?? 0,
                Broken = (int?)p.Value["broken"] ?? 0,
            };
            m.Holds[HandsDef.Left] = (string)p.Value["left"];
            m.Holds[HandsDef.Right] = (string)p.Value["right"];
            return m;
        }

        public MomentDef Moment(string id) => Moments.FirstOrDefault(m => m.Id == id);

        public WaveThemeDef Theme(string id) => Themes.FirstOrDefault(t => t.Id == id);

        public IEnumerable<AdjectiveDef> CommonAdjectives => Adjectives.Where(a => !a.Boss);

        internal static HoldParams Parse(JToken t)
        {
            var h = new HoldParams();
            if (!(t is JObject o)) return h;
            int Int(string key, int was) => (int?)o[key] ?? was;
            h.Health = Int("health", h.Health);
            h.PlateTicks = Int("plateTicks", h.PlateTicks);
            h.First = Int("first", h.First);
            h.Growth = Int("growth", h.Growth);
            h.HpPerWave = Int("hpPerWave", h.HpPerWave);
            h.Warmup = Int("warmup", h.Warmup);
            h.SpawnEvery = Int("spawnEvery", h.SpawnEvery);
            h.BetweenWaves = Int("betweenWaves", h.BetweenWaves);
            h.WaveTicks = Math.Max(0, Int("waveTicks", h.WaveTicks));
            h.Waves = Math.Max(0, Int("waves", h.Waves));
            h.TowerReach = Math.Max(0, Int("towerReach", h.TowerReach));
            h.BatterTicks = Int("batterTicks", h.BatterTicks);
            h.BatterCost = Int("batterCost", h.BatterCost);
            h.Margin = Int("margin", h.Margin);
            h.AdjectiveFrom = Int("adjectiveFrom", h.AdjectiveFrom);
            h.AdjectivePercent = Int("adjectivePercent", h.AdjectivePercent);
            h.Hands = HandsDef.Parse(o["hands"]);
            h.Offer = Int("offer", h.Offer);
            h.PointsPerWave = Int("pointsPerWave", h.PointsPerWave);
            h.Upgrades = (o["upgrades"] as JObject)?.Properties().Select(ParseUpgrade).ToList() ?? h.Upgrades;
            h.Unlocks = (o["unlocks"] as JArray)?.OfType<JObject>().Select(ParseUnlock).ToList() ?? h.Unlocks;
            h.DamagePerWave = Int("damagePerWave", h.DamagePerWave);
            h.OffRoadCost = Math.Max(1, Int("offRoadCost", h.OffRoadCost));
            h.RouteEvery = Math.Max(1, Int("routeEvery", h.RouteEvery));
            h.FlyerLands = o["flying"]?["lands"]?.Select(v => (string)v).ToList() ?? h.FlyerLands;
            h.BarricadeTerrain = (string)o["barricades"]?["terrain"];
            h.Rubble = (string)o["rubble"] ?? h.Rubble;
            h.BarricadeFrom = (int?)o["barricades"]?["from"] ?? 0;
            h.BarricadeFirst = (int?)o["barricades"]?["first"] ?? 0;
            h.BarricadePerWaves = Math.Max(1, (int?)o["barricades"]?["perWaves"] ?? 1);
            h.BarricadeMost = (int?)o["barricades"]?["most"] ?? 0;
            h.BarricadeRadius = (int?)o["barricades"]?["radius"] ?? 0;
            h.KindEvery = Math.Max(1, Int("kindEvery", h.KindEvery));
            h.AdjectivePerWave = Int("adjectivePerWave", h.AdjectivePerWave);
            h.AdjectiveMost = Int("adjectiveMost", h.AdjectiveMost);
            h.ThemeFrom = Int("themeFrom", h.ThemeFrom);
            h.ThemePercent = Int("themePercent", h.ThemePercent);
            h.TargetMinutes = Int("targetMinutes", h.TargetMinutes);
            h.BossEvery = (int?)o["boss"]?["every"] ?? 0;
            h.BossHpPerWave = (int?)o["boss"]?["hpPerWave"] ?? 0;
            h.BossAdjective = (string)o["boss"]?["adjective"];
            if (o["boss"]?["peerHealth"] is JArray bossPeers && bossPeers.Count > 0) h.BossPeerHealth = bossPeers.Select(v => (int)v).ToList();
            if (o["peerPercent"] is JArray peers && peers.Count > 0) h.PeerPercent = peers.Select(v => (int)v).ToList();
            h.Themes = (o["themes"] as JObject)?.Properties().Select(p => new WaveThemeDef
            {
                Id = p.Name,
                Name = (string)p.Value["name"] ?? p.Name,
                Adjectives = p.Value["adjectives"]?.Select(a => (string)a).ToList() ?? new List<string>(),
            }).ToList() ?? h.Themes;
            h.Moments = (o["moments"] as JObject)?.Properties().Select(ParseMoment).ToList() ?? h.Moments;
            h.Strength = (o["strength"] as JObject)?.Properties().ToDictionary(p => p.Name, p => (int)p.Value, StringComparer.Ordinal) ?? new Dictionary<string, int>();
            h.Adjectives = (o["adjectives"] as JObject)?.Properties().Select(p => new AdjectiveDef
            {
                Id = p.Name,
                Name = (string)p.Value["name"] ?? p.Name,
                Hp = (int?)p.Value["hp"] ?? 100,
                Damage = (int?)p.Value["damage"] ?? 100,
                Never = p.Value["never"]?.Select(v => (string)v).ToList() ?? new List<string>(),
                Boss = (bool?)p.Value["boss"] ?? false,
                Keeps = (string)p.Value["keeps"],
                Lights = (bool?)p.Value["lights"] ?? false,
                Weak = p.Value["weak"]?.Select(v => (string)v).ToList() ?? new List<string>(),
                Resist = p.Value["resist"]?.Select(v => (string)v).ToList() ?? new List<string>(),
                Color = p.Value["color"]?.Select(v => (double)v).ToArray() ?? Array.Empty<double>(),
            }).ToList() ?? new List<AdjectiveDef>();
            return h;
        }

        public AdjectiveDef Adjective(string id) => Adjectives.FirstOrDefault(a => a.Id == id);
    }

    public sealed class HoldSpawner
    {
        public string Id;
        public List<(int x, int y)> Cells = new List<(int, int)>();
        public IReadOnlyList<string> Kinds = Array.Empty<string>();
    }

    public sealed class HoldTower
    {
        public string At, Element;
        public int X, Y;
    }

    public sealed class WorldHold
    {
        public const string FileName = "hold.json";

        public string Castle { get; private set; }
        public (int x, int y) Heart { get; private set; }
        public (int x, int y) Plate { get; private set; }
        public (int x, int y) Rise { get; private set; }
        public IReadOnlyList<HoldTower> Towers { get; private set; } = Array.Empty<HoldTower>();
        public string RoadTerrain { get; private set; }
        public IReadOnlyList<(int x, int y)> Barricades { get; private set; } = Array.Empty<(int, int)>();
        public IReadOnlyList<HoldSpawner> Spawners { get; private set; } = Array.Empty<HoldSpawner>();
        public JObject Row { get; private set; }

        public static WorldHold Load(string worldDir)
        {
            string path = Path.Combine(worldDir ?? "", FileName);
            if (worldDir == null || !File.Exists(path)) return null;
            var o = JObject.Parse(File.ReadAllText(path));
            string castle = (string)o["castle"];
            var row = WorldBuildings.Load(worldDir).FileRows.FirstOrDefault(r => (string)r["id"] == castle) ?? throw new FormatException($"{FileName}: there's no row {castle}");
            var places = WorldPlaces.Load(worldDir);
            var spawners = new List<HoldSpawner>();
            foreach (var s in o["spawners"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                var place = places.Find((string)s["place"]) ?? throw new FormatException($"{FileName}: there's no place {(string)s["place"]}");
                spawners.Add(new HoldSpawner { Id = (string)s["id"], Cells = place.Cells().ToList(), Kinds = s["kinds"]?.Select(k => (string)k).ToList() ?? new List<string>() });
            }
            if (spawners.Count == 0) throw new FormatException($"{FileName}: no spawners");
            return new WorldHold
            {
                Castle = castle,
                Row = row,
                Heart = Mark(row, (string)o["heart"]),
                Plate = Mark(row, (string)o["plate"]),
                Rise = Mark(row, (string)o["rise"]),
                Towers = o["towers"]?.OfType<JObject>().Select(t => Tower(row, t)).ToList() ?? new List<HoldTower>(),
                RoadTerrain = (string)row["roads"]?[(string)o["road"] ?? ""]?["terrain"],
                Barricades = o["barricades"]?.Select(j => Mark(row, (string)j)).ToList() ?? new List<(int, int)>(),
                Spawners = spawners,
            };
        }

        static HoldTower Tower(JObject row, JObject t)
        {
            var (x, y) = Mark(row, (string)t["at"]);
            return new HoldTower { At = (string)t["at"], X = x, Y = y, Element = (string)t["element"] };
        }

        public HoldTower TowerAt(int x, int y, int reach) =>
            Towers.FirstOrDefault(t => Math.Max(Math.Abs(x - t.X), Math.Abs(y - t.Y)) <= reach);

        static (int x, int y) Mark(JObject row, string join)
        {
            var at = row["joins"]?[join ?? ""] as JArray ?? throw new FormatException($"{FileName}: the row has no join {join}");
            return ((int)row["at"][0] + (int)at[0] / 2, (int)row["at"][1] + (int)at[1] / 2);
        }

        public IEnumerable<string> Check(Rules rules) =>
            Spawners.SelectMany(s => s.Kinds).Distinct().Where(k => rules.Entity(k)?.Raider == null).Select(k => $"{FileName}: {k} isn't a raider")
                .Concat(Towers.Where(t => !rules.Combos.Elements.Contains(t.Element)).Select(t => $"{FileName}: the tower at {t.At} gives {t.Element ?? "nothing"}, which isn't an element"));
    }

    public sealed partial class Rules
    {
        sealed partial class Checker
        {
            static readonly HashSet<string> Routes = new HashSet<string> { "climb", "fly" };

            public void HoldRows()
            {
                const int mostMoment = 29;
                if (!(r["hold"] is JObject h) || h.Count == 0) return;
                var adjectives = h["adjectives"] as JObject ?? new JObject();
                var themes = h["themes"] as JObject ?? new JObject();
                foreach (var theme in themes.Properties())
                    foreach (var a in Strings(theme.Value["adjectives"]))
                        if (adjectives[a] == null) Errors.Add($"hold: theme {theme.Name}: unknown adjective {a}");
                if (h["boss"]?["adjective"] is JToken boss && adjectives[(string)boss] == null) Errors.Add($"hold: boss: unknown adjective {Show(boss)}");
                if (h["barricades"]?["terrain"] is JToken wood && Entry(r["terrain"], (string)wood) == null) Errors.Add($"hold: barricades: unknown terrain {Show(wood)}");
                foreach (var m in (h["moments"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                {
                    if (m.Name.Length > mostMoment) Errors.Add($"hold: moment {m.Name}: the id is too long");
                    if (m.Value["theme"] is JToken t && themes[(string)t] == null) Errors.Add($"hold: moment {m.Name}: unknown theme {Show(t)}");
                    if (!WholeFromOne(m.Value["wave"])) Errors.Add($"hold: moment {m.Name}: wave must be a whole number from 1");
                }
                foreach (var u in (h["upgrades"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                {
                    if (u.Value["gesture"] is JToken g && HandsDef.GestureOf((string)g) == HandsDef.NoGesture) Errors.Add($"hold: upgrade {u.Name}: unknown gesture {Show(g)}");
                    if (u.Value["hands"] is JToken hands && (string)hands != "both") Errors.Add($"hold: upgrade {u.Name}: hands must be \"both\", not {Show(hands)}");
                }
                foreach (var row in (h["unlocks"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                {
                    if (!WholeFromOne(row["cost"])) Errors.Add($"hold: unlock {Show(row["id"])}: cost must be a whole number from 1");
                    if (row["gesture"] is JToken g && HandsDef.GestureOf((string)g) == HandsDef.NoGesture) Errors.Add($"hold: unlock {Show(row["id"])}: unknown gesture {Show(g)}");
                    if (row["kit"] is JToken k && !SpellKnown(k)) Errors.Add($"hold: unlock {Show(row["id"])}: unknown spell {Show(k)}");
                }
                foreach (var e in (h["raiders"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    if (e.Value["raider"]?["route"] is JToken route && !Routes.Contains((string)route)) Errors.Add($"entity {e.Name}: unknown route {Show(route)}");
            }
        }

        public const char AdjectiveMark = '~';

        readonly Dictionary<string, EntityDef> holdVariants = new Dictionary<string, EntityDef>(StringComparer.Ordinal);
        readonly Dictionary<string, EntityDef> holdKinds = new Dictionary<string, EntityDef>(StringComparer.Ordinal);

        EntityDef HoldVariant(string id)
        {
            if (holdKinds.TryGetValue(id, out var kind)) return kind;
            int mark = id.IndexOf(AdjectiveMark);
            if (mark <= 0) return null;
            lock (holdVariants)
                return holdVariants.TryGetValue(id, out var made) ? made : MakeVariant(id, mark);
        }

        EntityDef MakeVariant(string id, int mark)
        {
            var adjective = Sim.Hold.Adjective(id.Substring(mark + 1));
            if (adjective == null || !(entities.TryGetValue(id.Substring(0, mark), out var plain) || holdKinds.TryGetValue(id.Substring(0, mark), out plain)) || plain.Raider == null) return null;
            var def = plain.Copy();
            def.Id = id;
            def.Name = $"{adjective.Name} {plain.Name.ToLowerInvariant()}";
            def.Hp = plain.Hp * adjective.Hp / 100;
            def.Raider = new RaiderDef
            {
                TicksPerSquare = plain.Raider.TicksPerSquare,
                Weak = plain.Raider.Weak.Concat(adjective.Weak).Where(e => !adjective.Resist.Contains(e)).Distinct().ToList(),
                Resist = plain.Raider.Resist.Concat(adjective.Resist).Where(e => !adjective.Weak.Contains(e)).Distinct().ToList(),
                AttackDice = plain.Raider.AttackDice,
                AttackEvery = plain.Raider.AttackEvery,
                Route = plain.Raider.Route,
            };
            return holdVariants[id] = def;
        }

        public static string PlainKind(string kind)
        {
            int mark = kind?.IndexOf(AdjectiveMark) ?? -1;
            return mark > 0 ? kind.Substring(0, mark) : kind;
        }

        public AdjectiveDef AdjectiveOf(string kind)
        {
            int mark = kind?.IndexOf(AdjectiveMark) ?? -1;
            return mark > 0 ? Sim.Hold.Adjective(kind.Substring(mark + 1)) : null;
        }
    }
}
