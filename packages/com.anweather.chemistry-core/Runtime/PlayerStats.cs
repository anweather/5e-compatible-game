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
    /// <summary>One player's stats: times the wizard's own speed, size and jump height, and the jumps before landing (2 is a double jump).</summary>
    public readonly struct PlayerStats
    {
        public PlayerStats(double speed, double size, double jump, int jumps)
        {
            Speed = speed;
            Size = size;
            Jump = jump;
            Jumps = jumps;
        }

        /// <summary>The wizard as it's made: its own speeds, size and jump, and a double jump.</summary>
        public static readonly PlayerStats Normal = new PlayerStats(1, 1, 1, 2);

        /// <summary>Times the wizard's walk and sprint speeds.</summary>
        public double Speed { get; }
        /// <summary>Times the wizard's height and width (its capsule, its model and the camera's height).</summary>
        public double Size { get; }
        /// <summary>Times the height of each jump, the first and every one in the air.</summary>
        public double Jump { get; }
        /// <summary>Jumps before landing: 1 is the ground's alone, 2 adds the double jump, 4 is a quadruple jump.</summary>
        public int Jumps { get; }

        public bool IsNormal => Same(Normal);

        /// <summary>Whether two players' stats are the same, stat by stat.</summary>
        public bool Same(PlayerStats o) => Speed.CompareTo(o.Speed) == 0 && Size.CompareTo(o.Size) == 0 && Jump.CompareTo(o.Jump) == 0 && Jumps == o.Jumps;
    }

    /// <summary>sim.json "player": everyone's stats, and a slot's own over them ("by").</summary>
    public sealed class PlayerParams
    {
        /// <summary>The stats a player has, in rules/sim.json player and budgets.json sim (player.speed...).</summary>
        public static readonly string[] Stats = { "speed", "size", "jump", "jumps" };

        /// <summary>Where a slot's own stats are kept under player: {"by": {"1": {"jumps": 4}}}.</summary>
        public const string ByKey = "by";

        readonly SortedDictionary<int, PlayerStats> bySlot = new SortedDictionary<int, PlayerStats>();

        /// <summary>Everyone's stats, where a slot has none of its own.</summary>
        public PlayerStats All { get; private set; } = PlayerStats.Normal;

        /// <summary>The slots with stats of their own, lowest first.</summary>
        public IEnumerable<int> Slots => bySlot.Keys;

        /// <summary>The stats of the player in a slot (0 the host, then each in join order): their own, else everyone's.</summary>
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

        // A row's stats, each it leaves out (or nulls) taken from under. Nothing at or below 0: the view divides by none of them.
        static PlayerStats Read(JObject o, PlayerStats under) => new PlayerStats(
            Positive(o["speed"], under.Speed),
            Positive(o["size"], under.Size),
            Positive(o["jump"], under.Jump),
            Math.Max(1, o["jumps"]?.Type == JTokenType.Integer ? (int)o["jumps"] : under.Jumps));

        static double Positive(JToken t, double under) =>
            t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) && (double)t > 0 ? (double)t : under;

        /// <summary>The stats in words, for list and whats_on: "speed 2x, jumps 4" (only what isn't normal); "" when nothing is.</summary>
        public static string Words(PlayerStats s)
        {
            var said = new List<string>();
            if (s.Speed.CompareTo(1.0) != 0) said.Add(string.Format(CultureInfo.InvariantCulture, "speed {0:0.##}x", s.Speed));
            if (s.Size.CompareTo(1.0) != 0) said.Add(string.Format(CultureInfo.InvariantCulture, "size {0:0.##}x", s.Size));
            if (s.Jump.CompareTo(1.0) != 0) said.Add(string.Format(CultureInfo.InvariantCulture, "jump {0:0.##}x", s.Jump));
            if (s.Jumps != 2) said.Add(string.Format(CultureInfo.InvariantCulture, "jumps {0}", s.Jumps));
            return string.Join(", ", said);
        }

        /// <summary>Everyone's and each slot's that isn't normal, in words; empty when every player is as made.</summary>
        public IEnumerable<string> Lines()
        {
            if (!All.IsNormal) yield return "Every player: " + Words(All);
            foreach (var slot in bySlot.Where(s => !s.Value.Same(All)))
                yield return string.Format(CultureInfo.InvariantCulture, "The player in slot {0}: {1}", slot.Key, Words(slot.Value) is var w && w.Length > 0 ? w : "as made");
        }
    }
}
