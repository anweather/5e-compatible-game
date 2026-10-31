// The unarmed strike (SRD 5.2.1, GitHub #18, J-045): a punch that counts. A
// wizard's health is its own (Vitals), so the core keeps only where each wizard
// stands and its health as its peer last said (a Stand command, sent when the
// wizard steps onto a new cell, is hurt, goes down or gets up), the same on
// every peer. A Strike command (who, their cell and the aim) is resolved here:
// the target is the nearest wizard or raider within sim.json strike.reach cells
// in front, by the core's own cells, never physics. The attack roll is a d20
// from the world's seeded generator + the Strength modifier + the proficiency
// bonus against the target's AC (entities.json "ac"); a natural 1 misses and a
// natural 20 hits. A hit does 1 + the Strength modifier bludgeoning damage and
// knocks the target strike.knockBack cells on, stopping at blocking ground or
// anyone standing there. A wizard at 0 is downed (the view's Downed takes it
// from there); a raider takes it like any damage (DamageRaider). Props are
// physics in the view, so the view pushes those itself. The stands are in the
// checksum (CastHash, through StrikeText).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>sim.json's "strike": the unarmed strike's numbers.</summary>
    public sealed class StrikeParams
    {
        /// <summary>Cells away (in any of the eight ways) a strike reaches, and how far a hit knocks the target on.</summary>
        public int Reach = 1, KnockBack = 1;
        /// <summary>Ticks after a strike before the same wizard may strike again.</summary>
        public int CooldownTicks = 6;
        /// <summary>The proficiency bonus added to the attack roll (SRD: +2 at level 1).</summary>
        public int Proficiency = 2;

        internal static StrikeParams Parse(JToken t)
        {
            var s = new StrikeParams();
            if (!(t is JObject o)) return s;
            s.Reach = Math.Max(1, (int?)o["reach"] ?? s.Reach);
            s.KnockBack = Math.Max(0, (int?)o["knockBack"] ?? s.KnockBack);
            s.CooldownTicks = Math.Max(0, (int?)o["cooldownTicks"] ?? s.CooldownTicks);
            s.Proficiency = (int?)o["proficiency"] ?? s.Proficiency;
            return s;
        }
    }

    public enum StrikeKind
    {
        /// <summary>A wizard stands on a cell with this much health (0: downed).</summary>
        Stand,
        /// <summary>A wizard punches from its cell along the aim.</summary>
        Strike,
        /// <summary>A wizard left the room: the core forgets it.</summary>
        Leave,
    }

    /// <summary>
    /// A wizard's stand, strike or leaving, stamped like a cast (TickBatch.Strikes). Player is the name the
    /// casts use (SpellCaster.CastPlayer); the log shows it as it is.
    /// </summary>
    public readonly struct StrikeCommand
    {
        public StrikeCommand(string player, StrikeKind kind, int x, int y, int aimX = 0, int aimY = 0, int hp = 0)
        {
            Player = player;
            Kind = kind;
            X = x;
            Y = y;
            AimX = aimX;
            AimY = aimY;
            Hp = hp;
        }

        public string Player { get; }
        public StrikeKind Kind { get; }
        /// <summary>The wizard's cell.</summary>
        public int X { get; }
        public int Y { get; }
        /// <summary>A strike's aim, any whole vector along the ground (east +X, south +Y); (0, 0) is every way.</summary>
        public int AimX { get; }
        public int AimY { get; }
        /// <summary>A stand's health, rounded up (Vitals.Health).</summary>
        public int Hp { get; }

        public static StrikeCommand Stand(string player, int x, int y, int hp) => new StrikeCommand(player, StrikeKind.Stand, x, y, hp: hp);
        public static StrikeCommand Strike(string player, int x, int y, int aimX, int aimY) => new StrikeCommand(player, StrikeKind.Strike, x, y, aimX, aimY);
        public static StrikeCommand Leave(string player) => new StrikeCommand(player, StrikeKind.Leave, 0, 0);
    }

    /// <summary>Where the core knows a wizard stands, its health as last said, and the tick it may strike again.</summary>
    public sealed class WizardStand
    {
        public string Player;
        public int X, Y, Hp, NextStrike;
        public bool Down => Hp <= 0;
    }

    /// <summary>
    /// What a strike did, for the view: who struck whom (a wizard's player, or a raider's id), the roll, the
    /// damage, and where the target ended up. The view hurts its own wizard and moves it there.
    /// </summary>
    public sealed class StrikeHit
    {
        public int Tick;
        public string Attacker, TargetPlayer;
        /// <summary>The raider struck, or -1 for a wizard.</summary>
        public int TargetRaider = -1;
        public int Roll, Total, Damage, ToX, ToY;
        public bool Hit, Downed;
    }

    public sealed partial class World
    {
        /// <summary>The wizard's row in entities.json: its health, Strength and AC.</summary>
        public const string WizardEntity = "wizard";

        readonly SortedDictionary<string, WizardStand> stands = new SortedDictionary<string, WizardStand>(StringComparer.Ordinal);
        readonly List<StrikeHit> strikeHits = new List<StrikeHit>();

        /// <summary>The wizards the core knows of, by player.</summary>
        public IReadOnlyDictionary<string, WizardStand> Stands => stands;

        /// <summary>The strikes since the view last took them, oldest first; taking them empties the list.</summary>
        public IReadOnlyList<StrikeHit> TakeStrikeHits()
        {
            var taken = strikeHits.ToList();
            strikeHits.Clear();
            return taken;
        }

        /// <summary>The Strength modifier of a score: (score - 10) / 2, rounded down.</summary>
        public static int Modifier(int score) => (int)Math.Floor((score - 10) / 2.0);

        /// <summary>Runs a stand, strike or leaving. Between ticks, like a cast.</summary>
        public void ApplyStrike(StrikeCommand command)
        {
            FinishTick(); // commands land between whole ticks (StepSome)
            if (command.Player == null) return;
            if (command.Kind == StrikeKind.Leave)
            {
                stands.Remove(command.Player);
                return;
            }
            if (!InBounds(command.X, command.Y)) return;
            var me = StandOf(command.Player);
            me.X = command.X;
            me.Y = command.Y;
            if (command.Kind == StrikeKind.Stand) me.Hp = Math.Max(0, command.Hp);
            else Strike(me, command.AimX, command.AimY);
        }

        WizardStand StandOf(string player)
        {
            if (stands.TryGetValue(player, out var s)) return s;
            var wizard = Rules.Entity(WizardEntity);
            return stands[player] = new WizardStand { Player = player, Hp = wizard != null ? wizard.Hp : 1 };
        }

        void Strike(WizardStand me, int aimX, int aimY)
        {
            var p = Rules.Sim.Strike;
            if (me.Down || Tick < me.NextStrike) return;
            me.NextStrike = Tick + p.CooldownTicks;
            var wizard = Rules.Entity(WizardEntity);
            int str = Modifier(wizard != null ? wizard.Str : EntityDef.DefaultScore);
            if (!Target(me, aimX, aimY, p.Reach, out var foe, out var raider)) return;
            var def = raider != null ? Rules.Entity(raider.Kind) : wizard;
            string name = raider != null ? "the " + def.Name.ToLowerInvariant() : foe.Player;
            int roll = 1 + (int)(Rand() * 20), total = roll + str + p.Proficiency;
            bool hit = roll == 20 || (roll != 1 && total >= (def != null ? def.Ac : EntityDef.DefaultScore));
            var result = new StrikeHit { Tick = Tick, Attacker = me.Player, TargetPlayer = foe?.Player, TargetRaider = raider != null ? raider.Id : -1, Roll = roll, Total = total, Hit = hit };
            strikeHits.Add(result);
            if (!hit)
            {
                AddLog($"{me.Player}'s punch misses {name}");
                (result.ToX, result.ToY) = raider != null ? (raider.X, raider.Y) : (foe.X, foe.Y);
                return;
            }
            int damage = Math.Max(0, 1 + str);
            result.Damage = damage;
            AddLog($"{me.Player} punches {name}: {damage} bludgeoning damage", LogKind.Hurt);
            int dx = Math.Sign(raider != null ? raider.X - me.X : foe.X - me.X), dy = Math.Sign(raider != null ? raider.Y - me.Y : foe.Y - me.Y);
            if (raider != null)
            {
                (raider.X, raider.Y) = KnockBack(raider.X, raider.Y, dx, dy, p.KnockBack);
                (result.ToX, result.ToY) = (raider.X, raider.Y);
                DamageRaider(raider, damage, "bludgeoning", null);
                return;
            }
            (foe.X, foe.Y) = KnockBack(foe.X, foe.Y, dx, dy, p.KnockBack);
            (result.ToX, result.ToY) = (foe.X, foe.Y);
            foe.Hp = Math.Max(0, foe.Hp - damage);
            if (!foe.Down) return;
            result.Downed = true;
            AddLog($"{foe.Player} is downed", LogKind.Hurt);
        }

        // The nearest wizard or raider within reach in front of me: closest first, then the most in line with the aim,
        // then wizards by player before raiders by id. False with no one there.
        bool Target(WizardStand me, int aimX, int aimY, int reach, out WizardStand foe, out Raider raider)
        {
            foe = null;
            raider = null;
            long best = long.MaxValue;
            foreach (var s in stands.Values)
            {
                if (s == me || s.Down) continue;
                long key = TargetKey(s.X - me.X, s.Y - me.Y, aimX, aimY, reach);
                if (key >= best) continue;
                best = key;
                foe = s;
            }
            foreach (var r in raiders)
            {
                if (!r.Alive) continue;
                long key = TargetKey(r.X - me.X, r.Y - me.Y, aimX, aimY, reach);
                if (key >= best) continue;
                best = key;
                foe = null;
                raider = r;
            }
            return foe != null || raider != null;
        }

        // How near and how in line an offset is, as one number to sort by (smaller first); long.MaxValue out of reach or behind.
        static long TargetKey(int ox, int oy, int aimX, int aimY, int reach)
        {
            int far = Math.Max(Math.Abs(ox), Math.Abs(oy));
            if (far < 1 || far > reach) return long.MaxValue;
            long dot = (long)ox * aimX + (long)oy * aimY, cross = Math.Abs((long)ox * aimY - (long)oy * aimX);
            if ((aimX != 0 || aimY != 0) && dot <= 0) return long.MaxValue;
            return ((long)ox * ox + (long)oy * oy) * 1000000L + Math.Min(cross, 999999L);
        }

        // Steps (x, y) on by (dx, dy) up to n times, stopping before blocking ground, the map's edge or anyone standing there.
        (int, int) KnockBack(int x, int y, int dx, int dy, int n)
        {
            for (int i = 0; i < n; i++)
            {
                int nx = x + dx, ny = y + dy;
                var c = At(nx, ny);
                if (c == null || IsBlocking(c) || RaiderAt(nx, ny) != null || stands.Values.Any(s => s.X == nx && s.Y == ny)) break;
                (x, y) = (nx, ny);
            }
            return (x, y);
        }

        /// <summary>
        /// The wizards' stands as text, "|w" player:x:y:hp:nextStrike each; empty with none, so worlds without
        /// wizards hash as before. The hits aren't in it: each view takes them when it likes.
        /// </summary>
        public string StrikeText()
        {
            if (stands.Count == 0) return "";
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            foreach (var s in stands.Values)
                sb.Append("|w").Append(s.Player).Append(':').Append(s.X.ToString(inv)).Append(':').Append(s.Y.ToString(inv)).Append(':')
                  .Append(s.Hp.ToString(inv)).Append(':').Append(s.NextStrike.ToString(inv));
            return sb.ToString();
        }
    }
}
