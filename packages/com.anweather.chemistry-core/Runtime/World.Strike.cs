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
    public sealed class StrikeParams
    {
        public int Reach = 1, KnockBack = 1;
        public int CooldownTicks = 6;
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
        Stand,
        Strike,
        Leave,
    }

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
        public int X { get; }
        public int Y { get; }
        public int AimX { get; }
        public int AimY { get; }
        public int Hp { get; }

        public static StrikeCommand Stand(string player, int x, int y, int hp) => new StrikeCommand(player, StrikeKind.Stand, x, y, hp: hp);
        public static StrikeCommand Strike(string player, int x, int y, int aimX, int aimY) => new StrikeCommand(player, StrikeKind.Strike, x, y, aimX, aimY);
        public static StrikeCommand Leave(string player) => new StrikeCommand(player, StrikeKind.Leave, 0, 0);
    }

    public sealed class WizardStand
    {
        public string Player;
        public int X, Y, Hp, NextStrike;
        public bool Down => Hp <= 0;
    }

    public sealed class StrikeHit
    {
        public int Tick;
        public string Attacker, TargetPlayer;
        public int TargetRaider = -1;
        public int Roll, Total, Damage, ToX, ToY;
        public bool Hit, Downed;
    }

    public sealed partial class World
    {
        public const string WizardEntity = "wizard";

        readonly SortedDictionary<string, WizardStand> stands = new SortedDictionary<string, WizardStand>(StringComparer.Ordinal);
        readonly List<StrikeHit> strikeHits = new List<StrikeHit>();

        public IReadOnlyDictionary<string, WizardStand> Stands => stands;

        int standReads;

        public int StandReads => standReads + triggerFirings;

        public bool TriggersReadStands => Rules.TriggersOn(TriggerEvent.Step).Count > 0 || Rules.TriggersOn(TriggerEvent.Tick).Count > 0;

        void ReadStands() => standReads++;

        public IReadOnlyList<StrikeHit> TakeStrikeHits()
        {
            var taken = strikeHits.ToList();
            strikeHits.Clear();
            return taken;
        }

        public static int Modifier(int score) => (int)Math.Floor((score - 10) / 2.0);

        public void ApplyStrike(StrikeCommand command)
        {
            FinishTick();
            if (command.Player == null) return;
            if (command.Kind == StrikeKind.Leave)
            {
                stands.Remove(command.Player);
                return;
            }
            if (!InBounds(command.X, command.Y)) return;
            bool known = stands.ContainsKey(command.Player);
            var me = StandOf(command.Player);
            int fromX = me.X, fromY = me.Y;
            bool wasDown = me.Down;
            me.X = FoldX(command.X);
            me.Y = FoldY(command.Y);
            if (command.Kind != StrikeKind.Stand) Strike(me, command.AimX, command.AimY);
            else
            {
                me.Hp = Math.Max(0, command.Hp);
                if (known && !wasDown && !me.Down && (fromX != me.X || fromY != me.Y)) Stepped(me, fromX, fromY);
            }
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
            int dx = Math.Sign(DeltaX(me.X, raider != null ? raider.X : foe.X)), dy = Math.Sign(DeltaY(me.Y, raider != null ? raider.Y : foe.Y));
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

        bool Target(WizardStand me, int aimX, int aimY, int reach, out WizardStand foe, out Raider raider)
        {
            foe = null;
            raider = null;
            long best = long.MaxValue;
            foreach (var s in stands.Values)
            {
                if (s == me || s.Down) continue;
                long key = TargetKey(DeltaX(me.X, s.X), DeltaY(me.Y, s.Y), aimX, aimY, reach);
                if (key >= best) continue;
                best = key;
                foe = s;
            }
            foreach (var r in raiders)
            {
                if (!r.Alive) continue;
                long key = TargetKey(DeltaX(me.X, r.X), DeltaY(me.Y, r.Y), aimX, aimY, reach);
                if (key >= best) continue;
                best = key;
                foe = null;
                raider = r;
            }
            return foe != null || raider != null;
        }

        static long TargetKey(int ox, int oy, int aimX, int aimY, int reach)
        {
            int far = Math.Max(Math.Abs(ox), Math.Abs(oy));
            if (far < 1 || far > reach) return long.MaxValue;
            long dot = (long)ox * aimX + (long)oy * aimY, cross = Math.Abs((long)ox * aimY - (long)oy * aimX);
            if ((aimX != 0 || aimY != 0) && dot <= 0) return long.MaxValue;
            return ((long)ox * ox + (long)oy * oy) * 1000000L + Math.Min(cross, 999999L);
        }

        (int, int) KnockBack(int x, int y, int dx, int dy, int n)
        {
            for (int i = 0; i < n; i++)
            {
                int nx = FoldX(x + dx), ny = FoldY(y + dy);
                var c = At(nx, ny);
                if (c == null || IsBlocking(c) || RaiderAt(nx, ny) != null || stands.Values.Any(s => s.X == nx && s.Y == ny)) break;
                (x, y) = (nx, ny);
            }
            return (x, y);
        }

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

        void SaveStands(JObject save)
        {
            if (stands.Count == 0) return;
            save["stands"] = new JArray(stands.Values.Select(s => new JArray(s.Player, s.X, s.Y, s.Hp, s.NextStrike)));
        }

        void LoadStands(JObject s)
        {
            foreach (var a in s["stands"] as JArray ?? new JArray())
                stands[(string)a[0]] = new WizardStand { Player = (string)a[0], X = (int)a[1], Y = (int)a[2], Hp = (int)a[3], NextStrike = (int)a[4] };
        }
    }
}
