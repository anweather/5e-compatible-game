// A player's stats (docs/RULES_MCP.md "Player stats"): how fast they move, how big they are,
// how high they jump and how many jumps they have before they land, as rules, so a mod changes
// them like anything else and every peer switches at the same tick (a rule switch). sim.json
// "player" holds everyone's; "player.by" a slot's own, field by field over everyone's. The
// core simulates none of it: a wizard's body is its own peer's (D4), so the view reads
// Rules.Sim.Player.Of(slot) at each rule switch and scales its controller by it. Bounds are in
// budgets.json sim (player.speed and the rest), the same for a slot's own.
//
// Not a port: the sandbox has no players.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public readonly struct PlayerStats
    {
        public PlayerStats(double speed, double size, double jump, int jumps)
        {
            Speed = speed;
            Size = size;
            Jump = jump;
            Jumps = jumps;
        }

        public static readonly PlayerStats Normal = new PlayerStats(1, 1, 1, 2);

        public double Speed { get; }
        public double Size { get; }
        public double Jump { get; }
        public int Jumps { get; }

        public bool IsNormal => Same(Normal);

        public bool Same(PlayerStats o) => Speed.CompareTo(o.Speed) == 0 && Size.CompareTo(o.Size) == 0 && Jump.CompareTo(o.Jump) == 0 && Jumps == o.Jumps;
    }

    public sealed class PlayerParams
    {
        public static readonly string[] Stats = { "speed", "size", "jump", "jumps" };

        public const string ByKey = "by";

        readonly SortedDictionary<int, PlayerStats> bySlot = new SortedDictionary<int, PlayerStats>();

        public PlayerStats All { get; private set; } = PlayerStats.Normal;

        public IEnumerable<int> Slots => bySlot.Keys;

        public PlayerStats Of(int slot) => bySlot.TryGetValue(slot, out var own) ? own : All;

        internal static PlayerParams Parse(JToken t)
        {
            var p = new PlayerParams();
            if (!(t is JObject o)) return p;
            p.All = Read(o, PlayerStats.Normal);
            if (o[ByKey] is JObject by)
                foreach (var slot in by.Properties())
                    if (slot.Value is JObject own && int.TryParse(slot.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                        p.bySlot[n] = Read(own, p.All);
            return p;
        }

        static PlayerStats Read(JObject o, PlayerStats under) => new PlayerStats(
            Positive(o["speed"], under.Speed),
            Positive(o["size"], under.Size),
            Positive(o["jump"], under.Jump),
            Math.Max(1, o["jumps"]?.Type == JTokenType.Integer ? (int)o["jumps"] : under.Jumps));

        static double Positive(JToken t, double under) =>
            t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) && (double)t > 0 ? (double)t : under;

        public static string Words(PlayerStats s)
        {
            var said = new List<string>();
            if (s.Speed.CompareTo(1.0) != 0) said.Add(string.Format(CultureInfo.InvariantCulture, "speed {0:0.##}x", s.Speed));
            if (s.Size.CompareTo(1.0) != 0) said.Add(string.Format(CultureInfo.InvariantCulture, "size {0:0.##}x", s.Size));
            if (s.Jump.CompareTo(1.0) != 0) said.Add(string.Format(CultureInfo.InvariantCulture, "jump {0:0.##}x", s.Jump));
            if (s.Jumps != 2) said.Add(string.Format(CultureInfo.InvariantCulture, "jumps {0}", s.Jumps));
            return string.Join(", ", said);
        }

        public IEnumerable<string> Lines()
        {
            if (!All.IsNormal) yield return "Every player: " + Words(All);
            foreach (var slot in bySlot.Where(s => !s.Value.Same(All)))
                yield return string.Format(CultureInfo.InvariantCulture, "The player in slot {0}: {1}", slot.Key, Words(slot.Value) is var w && w.Length > 0 ? w : "as made");
        }
    }
}
