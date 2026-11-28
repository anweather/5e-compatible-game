// The late-join harness (D4, D22, docs/TESTING_LAYERS.md layer 1; #173, #164, #55, #83), for the lockstep tests and any
// mode's own (golf's GolfJoinTests). The host runs a world tick by tick on scripted commands, stamping the stands into
// a strike history through StandKeeper as NetChem does (stamped before the tick runs, Ran after it). A joiner arriving
// at a tick gets that history, keeps every command after it as the live batches bring them, and replays all of it on a
// fresh world from the same build (which installs the modes the host's has). Both are compared at every tick, part by
// part of the checksum (NetChem.Checksum), with the stands apart from the rest of the casts part: the stands may differ
// only at a tick no rule and no checksum reads them, and everything else never. The joiners and snapshots of one host
// run replay side by side, each on its own world (the host's run is only read). core/Chem.Lockstep.Kit in dotnet.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Chem.Tests
{
    internal static class LockstepKit
    {
        internal static World Meadow(Rules rules) => new World(rules, 64, 64, 5, "grass");

        internal sealed class Stamp
        {
            public readonly List<CastCommand> Casts = new List<CastCommand>();
            public readonly List<ComboCommand> Combos = new List<ComboCommand>();
            public readonly List<StrikeCommand> Strikes = new List<StrikeCommand>();
            public readonly List<ChunkSwitch> Chunks = new List<ChunkSwitch>();
            public IReadOnlyList<ActiveMod> Mods;
            public bool Dragon;
        }

        internal static string[] Parts(World w) => new[]
        {
            w.CellHash(), w.Rules.Fingerprint, w.ThingsNow.Fingerprint, w.SkyHash(), w.DefenseHash(), w.CastText(""), w.DragonSystem.Hash(), w.LandHash(),
            w.StrikeText(),
        };

        internal static readonly string[] PartNames = { "cells", "rules", "things", "sky", "defense", "casts but the stands", "dragon", "land", "stands" };
        internal const int StandsPart = 8;

        internal static bool ChecksumTick(World w, int tick) => tick % RollingHash.Every == 0 || tick % RollingHash.WindowFor(w) == 0;

        internal sealed class HostRun
        {
            public readonly Dictionary<int, Stamp> Stamped = new Dictionary<int, Stamp>();
            public readonly Dictionary<int, StampedStrike[]> KeptAt = new Dictionary<int, StampedStrike[]>();
            public readonly Dictionary<int, string[]> Seen = new Dictionary<int, string[]>();
            public readonly List<StampedStrike> AllStrikes = new List<StampedStrike>();
            public readonly Dictionary<int, JoinSnapshot> Snapshots = new Dictionary<int, JoinSnapshot>();
            public World World;
            public int Ticks;
        }

        internal static HostRun Host(Func<World> build, int ticks, Action<int, World, Stamp> script, ICollection<int> snapshotAt = null)
        {
            var run = new HostRun { World = build(), Ticks = ticks };
            var w = run.World;
            var set = RuleSet.Plain(w.Rules);
            var history = new List<StampedStrike>();
            Stamp now = null;
            var keeper = new StandKeeper<StampedStrike>(() => w.TriggersReadStands || now?.Mods != null, w.InBounds, w.HoldReadsStand, w.DeltaX);
            for (int t = 1; t <= ticks; t++)
            {
                now = new Stamp();
                script(t, w, now);
                run.Stamped[t] = now;
                foreach (var s in now.Strikes)
                {
                    var stamped = new StampedStrike(t, s);
                    run.AllStrikes.Add(stamped);
                    keeper.Record(history, stamped);
                }
                set = Batch(t, now, now.Strikes).RunOn(w, set, out _);
                keeper.Ran(t, w.StandReads, RollingHash.WindowFor(w));
                run.KeptAt[t] = history.ToArray();
                run.Seen[t] = Parts(w);
                if (snapshotAt?.Contains(t) == true) run.Snapshots[t] = JoinSnapshot.Parse(JoinSnapshot.Take(w, w.Source, set).Text());
            }
            return run;
        }

        internal static TickBatch Batch(int t, Stamp stamp, IEnumerable<StrikeCommand> strikes)
        {
            var batch = new TickBatch(t, stamp.Casts, stamp.Mods);
            batch.Combos.AddRange(stamp.Combos);
            batch.Chunks.AddRange(stamp.Chunks);
            batch.Strikes.AddRange(strikes);
            if (stamp.Dragon) batch.Dragon.Add(new DragonCommand());
            return batch;
        }

        internal static string FirstDifference(HostRun host, Func<World> build, int joinAt)
        {
            var strikes = host.KeptAt[joinAt].Concat(host.AllStrikes.Where(s => s.Tick > joinAt)).ToLookup(s => s.Tick, s => s.Command);
            var w = build();
            var set = RuleSet.Plain(w.Rules);
            for (int t = 1; t <= host.Ticks; t++)
            {
                set = Batch(t, host.Stamped[t], strikes[t]).RunOn(w, set, out _);
                var seen = Parts(w);
                for (int p = 0; p < seen.Length; p++)
                {
                    if (seen[p] == host.Seen[t][p] || (p == StandsPart && !ChecksumTick(w, t))) continue;
                    return $"joined at {joinAt}: tick {t}, {PartNames[p]}: host {host.Seen[t][p]}, joiner {seen[p]}";
                }
            }
            return null;
        }

        internal static void AssertJoinersMatch(HostRun host, Func<World> build, IEnumerable<int> joinAts)
        {
            foreach (var found in Replayed(joinAts.ToArray(), at => FirstDifference(host, build, at))) Assert.That(found, Is.Null);
        }

        internal static string[] Replayed<T>(T[] each, Func<T, string> difference)
        {
            var found = new string[each.Length];
            Parallel.For(0, each.Length, i => found[i] = difference(each[i]));
            return found;
        }

        internal const string BoltsAndMeteors = "{ 'triggers': { " +
            "'bolt-steps': { 'on': 'step', 'who': 'each', 'every': 9, 'do': [ { 'cast': 'fire-bolt', 'at': 'behind', 'by': 2 } ] }, " +
            "'meteor-steps': { 'on': 'step', 'who': 'each', 'every': 15, 'do': [ { 'cast': 'meteor', 'at': 'ahead', 'by': 2, 'delay': 6 } ] } } }";

        internal const string Showers = "{ 'triggers': { 'shower': { 'on': 'tick', 'who': 'each', 'every': 12, 'chance': 0.5, 'do': [ { 'cast': 'fire-bolt', 'at': 'around', 'within': 3 } ] } } }";

        internal static ActiveMod[] Mod(string id, string json) => new[] { new ActiveMod(id, json.Replace('\'', '"')) };

        internal static string SnapshotDifference(HostRun host, JoinSnapshot snapshot, IChunkSource source, int[] land = null, World modesOf = null)
        {
            var (w, set) = snapshot.Join(ChemistryTests.R, source, land, null, modesOf);
            var strikes = host.AllStrikes.Where(s => s.Tick > snapshot.Tick).ToLookup(s => s.Tick, s => s.Command);
            for (int t = snapshot.Tick + 1; t <= host.Ticks; t++)
            {
                set = Batch(t, host.Stamped[t], strikes[t]).RunOn(w, set, out _);
                var seen = Parts(w);
                for (int p = 0; p < seen.Length; p++)
                    if (seen[p] != host.Seen[t][p]) return $"joined at {snapshot.Tick}: tick {t}, {PartNames[p]}: host {host.Seen[t][p]}, joiner {seen[p]}";
            }
            return null;
        }
    }
}
