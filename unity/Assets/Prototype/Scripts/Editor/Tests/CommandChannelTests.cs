using System.Collections.Generic;
using System.Linq;
using Chem;
using NUnit.Framework;

// CommandChannel, one kind of command on its way through the lockstep network, without a network:
// the host's stamp, the stamped commands joining their tick's batch whether they come before it or
// after, a late joiner's history, a reset forgetting what came before, and the compaction hook;
// then strikes, defense and room commands on it as NetChem has them, with their real intents: the
// stands a strike history keeps (StandKeeper) as before with no step trigger, and with one, a late
// joiner's replay of them firing the same casts on a real world.
public class CommandChannelTests
{
    // An intent of the test's own: a player and what they did.
    struct Note : ITickStamped
    {
        public string Player, What;
        public int Tick { get; set; }

        public void StampFor(int tick) => Tick = tick;

        public ComboCommand ToCommand() => ComboCommand.Load(Player, What);
    }

    static CommandChannel<Note> Channel(Dictionary<int, TickBatch> batches, System.Action<List<Note>, Note> record = null) =>
        new(batches, (batch, n) => batch.Combos.Add(n.ToCommand()), record);

    static Note N(string player, string what) => new() { Player = player, What = what };

    static string[] Elements(TickBatch batch) => batch.Combos.Select(c => c.Player + ":" + c.Element).ToArray();

    [Test]
    public void TheStampTicksWhatWasAskedInOrderAndOnlyOnce()
    {
        var host = Channel(new Dictionary<int, TickBatch>());
        host.Request(N("p0", "water"));
        host.Request(N("p1", "fire"));
        var stamped = host.Stamp(7);
        Assert.That(stamped.Select(n => n.Player + "@" + n.Tick), Is.EqualTo(new[] { "p0@7", "p1@7" }));
        Assert.That(host.Stamp(8), Is.Empty, "nothing asked since: nothing sent");
        Assert.That(host.HistoryForJoiner().Select(n => n.Tick), Is.EqualTo(new[] { 7, 7 }));
    }

    [Test]
    public void CommandsThatArriveBeforeTheirBatchJoinItWhenItComes()
    {
        var batches = new Dictionary<int, TickBatch>();
        var peer = Channel(batches);
        peer.Receive(5, new[] { N("p0", "water"), N("p1", "earth") });
        batches[4] = new TickBatch(4);
        peer.Deliver(4);
        Assert.That(batches[4].Combos, Is.Empty, "another tick's batch takes none of them");
        batches[5] = new TickBatch(5);
        peer.Deliver(5);
        Assert.That(Elements(batches[5]), Is.EqualTo(new[] { "p0:water", "p1:earth" }));
        peer.Deliver(5);
        Assert.That(batches[5].Combos.Count, Is.EqualTo(2), "delivered once");
    }

    [Test]
    public void CommandsThatArriveAfterTheirBatchJoinItAtOnce()
    {
        var batches = new Dictionary<int, TickBatch> { [5] = new TickBatch(5) };
        var peer = Channel(batches);
        peer.Deliver(5);
        peer.Receive(5, new[] { N("p0", "water"), N("p1", "earth") });
        Assert.That(Elements(batches[5]), Is.EqualTo(new[] { "p0:water", "p1:earth" }));
    }

    [Test]
    public void ALateJoinerReplaysTheHistoryOnTheTicksItWasStampedFor()
    {
        var host = Channel(new Dictionary<int, TickBatch>());
        host.Request(N("p0", "water"));
        host.Stamp(3);
        host.Request(N("p1", "fire"));
        host.Request(N("p0", "air"));
        host.Stamp(5);
        var history = host.HistoryForJoiner();

        var joiner = Channel(new Dictionary<int, TickBatch>());
        joiner.Joining(history);
        var timeline = TickBatch.Timeline(null, null, 6);
        joiner.Replay(timeline);
        Assert.That(Elements(timeline[3]), Is.EqualTo(new[] { "p0:water" }));
        Assert.That(Elements(timeline[5]), Is.EqualTo(new[] { "p1:fire", "p0:air" }));

        // The same replay as TickBatch.AddCombos made of the same history before the channel.
        var before = TickBatch.Timeline(null, null, 6);
        TickBatch.AddCombos(before, history.Select(n => (n.Tick, n.ToCommand())));
        for (int t = 1; t <= 6; t++) Assert.That(Elements(timeline[t]), Is.EqualTo(Elements(before[t])), $"tick {t}");
    }

    [Test]
    public void AResetForgetsTheHistoryBeforeIt()
    {
        var host = Channel(new Dictionary<int, TickBatch>());
        host.Request(N("p0", "water"));
        host.Stamp(3);
        host.Request(N("p0", "fire"));
        host.Stamp(9);
        host.ForgetBefore(9);
        Assert.That(host.HistoryForJoiner().Select(n => n.What), Is.EqualTo(new[] { "fire" }));
    }

    [Test]
    public void TheCompactionHookDecidesWhatTheHistoryKeeps()
    {
        // Simpler than strikes' rule: a player's newer stand replaces the older one, a strike is always kept.
        var host = Channel(new Dictionary<int, TickBatch>(), (history, n) =>
        {
            if (n.What == "stand") history.RemoveAll(h => h.What == "stand" && h.Player == n.Player);
            history.Add(n);
        });
        host.Request(N("p0", "stand"));
        host.Request(N("p1", "stand"));
        host.Stamp(1);
        host.Request(N("p0", "stand"));
        host.Request(N("p0", "strike"));
        var stamped = host.Stamp(2);
        Assert.That(stamped.Length, Is.EqualTo(2), "the wire still carries every command");
        Assert.That(host.HistoryForJoiner().Select(n => $"{n.Player}:{n.What}@{n.Tick}"), Is.EqualTo(new[] { "p1:stand@1", "p0:stand@2", "p0:strike@2" }));
    }

    // Strikes as NetChem has them on their channel, with StandKeeper as its hook.
    static CommandChannel<StrikeIntent> StrikeChannel(Dictionary<int, TickBatch> batches, StandKeeper keeper) =>
        new(batches, (batch, s) => batch.Strikes.Add(s.ToCommand()), keeper.Record);

    static StrikeIntent Stand(string player, int x, int y, int hp = 12) => StrikeIntent.From(StrikeCommand.Stand(player, x, y, hp), 0);

    static StrikeIntent Punch(string player, int x, int y) => StrikeIntent.From(StrikeCommand.Strike(player, x, y, 100, 0), 0);

    static string Held(StrikeIntent s) => $"{s.Player}:{(StrikeKind)s.Kind}@{s.Tick}:{s.X},{s.Y},{s.Hp}";

    // NetChem.StampStrikes' rule before StandKeeper, with no step trigger: a stand replaces the player's stands since the last strike.
    static void RecordAsBefore(List<StrikeIntent> history, StrikeIntent s, ref int lastStrikeTick)
    {
        int last = lastStrikeTick;
        if ((StrikeKind)s.Kind == StrikeKind.Stand) history.RemoveAll(h => (StrikeKind)h.Kind == StrikeKind.Stand && h.Player == s.Player && h.Tick > last);
        else lastStrikeTick = s.Tick;
        history.Add(s);
    }

    [Test]
    public void OutsideAStepTriggerTheStandsKeptAreAsBefore()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var random = new System.Random(seed);
            var keeper = new StandKeeper(() => false, (x, y) => true);
            var host = StrikeChannel(new Dictionary<int, TickBatch>(), keeper);
            var before = new List<StrikeIntent>();
            int lastStrikeTick = 0;
            for (int t = 1; t <= 400; t++)
            {
                foreach (var p in new[] { "p0", "p1", "p2" })
                {
                    int roll = random.Next(10);
                    if (roll < 5) host.Request(Stand(p, random.Next(20), random.Next(20), random.Next(13)));
                    else if (roll == 5) host.Request(Punch(p, random.Next(20), random.Next(20)));
                    else if (roll == 6 && random.Next(20) == 0) host.Request(StrikeIntent.From(StrikeCommand.Leave(p), 0));
                }
                foreach (var s in host.Stamp(t)) RecordAsBefore(before, s, ref lastStrikeTick);
                keeper.Ran(t, 0);
                if (t % 97 == 0)
                {
                    host.ForgetBefore(t - 10);
                    before.RemoveAll(s => s.Tick < t - 10);
                }
                Assert.That(host.HistoryForJoiner().Select(Held), Is.EqualTo(before.Select(Held)), $"seed {seed}, tick {t}");
            }
        }
    }

    [Test]
    public void WithAStepTriggerOnTheStandsKeptAreThoseAReplayNeeds()
    {
        const int FireAt = 7;
        var keeper = new StandKeeper(() => true, (x, y) => true);
        var host = StrikeChannel(new Dictionary<int, TickBatch>(), keeper);
        // East along a row, a turn south for two cells, then east again with a step that fired a trigger at tick 7.
        var walk = new[] { (10, 10), (11, 10), (12, 10), (13, 10), (13, 11), (13, 12), (14, 12), (15, 12) };
        for (int t = 1; t <= walk.Length; t++)
        {
            host.Request(Stand("p0", walk[t - 1].Item1, walk[t - 1].Item2));
            host.Stamp(t);
            if (t < walk.Length) keeper.Ran(t, t >= FireAt ? 1 : 0);
        }
        // 1: the first (no stand before it to step from); 4 and 6: the stand after each steps another way from the
        // stand before them (from 1 to 6 is diagonal, 4 to 6 south; from 4 to 7 diagonal, 6 to 7 east); 7: it fired;
        // 8: the newest, not run yet. 2, 3 and 5 fired nothing and leave the step after them as it was.
        Assert.That(host.HistoryForJoiner().Select(s => s.Tick), Is.EqualTo(new[] { 1, 4, 6, 7, 8 }));

        // A checksum's tick keeps the stand standing then: 30's, not 29's, 31's or 32's.
        keeper = new StandKeeper(() => true, (x, y) => true);
        host = StrikeChannel(new Dictionary<int, TickBatch>(), keeper);
        for (int t = 28; t <= 33; t++)
        {
            host.Request(Stand("p0", t, 10));
            host.Stamp(t);
            keeper.Ran(t, 0);
        }
        Assert.That(host.HistoryForJoiner().Select(s => s.Tick), Is.EqualTo(new[] { 28, 30, 33 }));

        // Hurt to the ground and up again: a stand that's no step can't be shown to fire nothing, so it stays.
        keeper = new StandKeeper(() => true, (x, y) => true);
        host = StrikeChannel(new Dictionary<int, TickBatch>(), keeper);
        var hurt = new[] { (1, 12), (2, 12), (2, 0), (3, 0), (4, 12), (5, 12), (6, 12) };
        for (int t = 1; t <= hurt.Length; t++)
        {
            host.Request(Stand("p0", hurt[t - 1].Item1, 10, hurt[t - 1].Item2));
            host.Stamp(t);
            keeper.Ran(t, 0);
        }
        Assert.That(host.HistoryForJoiner().Select(s => s.Tick), Is.EqualTo(new[] { 1, 3, 4, 5, 7 }),
            "2 goes (the fall after it is no step from 1 either); 3 and 4 are down and 5 gets up, none a step, so they stay; 6 goes");
    }

    [Test]
    public void StandsKeptWhileAStepTriggerWasOnStayOnceItsOff()
    {
        bool steps = true;
        var keeper = new StandKeeper(() => steps, (x, y) => true);
        var host = StrikeChannel(new Dictionary<int, TickBatch>(), keeper);
        for (int t = 1; t <= 6; t++)
        {
            host.Request(Stand("p0", 10 + t, 10));
            host.Stamp(t);
            keeper.Ran(t, t >= 4 ? t - 3 : 0);
        }
        var kept = host.HistoryForJoiner().Select(s => s.Tick).ToArray();
        Assert.That(kept, Is.EqualTo(new[] { 1, 4, 5, 6 }), "4 and 5 fired; 6 hasn't run");
        steps = false;
        host.Request(Stand("p0", 20, 10));
        host.Stamp(7);
        host.Request(Stand("p0", 21, 10));
        host.Stamp(8);
        Assert.That(host.HistoryForJoiner().Select(s => s.Tick), Is.EqualTo(kept.Append(8)), "off: the newest replaces only the stands since the trigger went off");
    }

    // A step trigger's world as the host runs it (one tick behind its stamps, as NetChem's), and a late joiner's
    // replay of the kept history: alone, and with every stand from a tick on as the live batches bring it.
    const string BoltsAndMeteors = "{ \"triggers\": { " +
        "\"bolt-steps\": { \"on\": \"step\", \"who\": \"each\", \"every\": 9, \"do\": [ { \"cast\": \"fire-bolt\", \"at\": \"behind\", \"by\": 2 } ] }, " +
        "\"meteor-steps\": { \"on\": \"step\", \"who\": \"each\", \"every\": 15, \"do\": [ { \"cast\": \"meteor\", \"at\": \"ahead\", \"by\": 2, \"delay\": 6 } ] } } }";

    static Rules LoadedRules() => Rules.Load(System.IO.Path.GetFullPath("../rules"));

    static World Meadow(Rules rules) => new(rules, 64, 64, 5, "grass");

    // What a world shows a peer: its checksum's parts that strikes and triggers reach.
    static string Seen(World w) => $"{w.CellHash()}/{w.CastHash()}/{w.TriggerFirings}/{w.TriggeredCasts}";

    // Players walking about: runs, turns, stops, a hurt, a fall and getting up, a stand off the world, punches.
    static List<StrikeIntent>[] Walks(int seed, int ticks)
    {
        var random = new System.Random(seed);
        var asked = new List<StrikeIntent>[ticks + 1];
        var at = new[] { (20, 20), (40, 30) };
        var way = new[] { (1, 0), (0, 1) };
        var hp = new[] { 12, 12 };
        for (int t = 1; t <= ticks; t++)
        {
            asked[t] = new List<StrikeIntent>();
            for (int p = 0; p < 2; p++)
            {
                string player = "p" + p;
                int roll = random.Next(100);
                if (roll < 40) continue;
                if (roll < 50) way[p] = (random.Next(3) - 1, random.Next(3) - 1);
                else if (roll < 54) hp[p] = hp[p] > 0 ? random.Next(0, 12) : 12;
                else if (roll < 56)
                {
                    asked[t].Add(Punch(player, at[p].Item1, at[p].Item2));
                    continue;
                }
                else if (roll == 56)
                {
                    asked[t].Add(Stand(player, -3, at[p].Item2, hp[p]));
                    continue;
                }
                int x = at[p].Item1 + way[p].Item1, y = at[p].Item2 + way[p].Item2;
                if (x < 4 || x > 59 || y < 4 || y > 59) way[p] = (-way[p].Item1, -way[p].Item2);
                else at[p] = (x, y);
                asked[t].Add(Stand(player, at[p].Item1, at[p].Item2, hp[p]));
            }
        }
        return asked;
    }

    [Test]
    public void AStandTheHoldReadsAndTheOneThatLeavesItStayInTheHistory()
    {
        const int Plate = 5;
        var keeper = new StandKeeper(() => false, (x, y) => true, (x, y) => x == Plate);
        var host = StrikeChannel(new Dictionary<int, TickBatch>(), keeper);
        var walk = new[] { 1, 2, Plate, 8, 9, 10 };
        for (int t = 1; t <= walk.Length; t++)
        {
            host.Request(Stand("p0", walk[t - 1], 1));
            host.Stamp(t);
        }
        var kept = host.HistoryForJoiner().Select(s => s.X).ToList();
        Assert.That(kept.Contains(Plate));
        Assert.That(kept.Contains(8), "the stand off it says when it was left");
        Assert.That(kept.Contains(10));
        Assert.That(kept.Contains(9), Is.False, "the rest go, as ever");
    }

    static Dictionary<int, string> Replay(Rules rules, string mod, IEnumerable<StrikeIntent> strikes, int ticks)
    {
        var joiner = StrikeChannel(new Dictionary<int, TickBatch>(), new StandKeeper(() => true, (x, y) => true));
        joiner.Joining(strikes.ToArray());
        var timeline = TickBatch.Timeline(null, new[] { new RuleChange(1, new[] { new ActiveMod("steps", mod) }) }, ticks);
        joiner.Replay(timeline);
        var world = Meadow(rules);
        var set = RuleSet.Plain(rules);
        var seen = new Dictionary<int, string>();
        for (int t = 1; t <= ticks; t++)
        {
            set = timeline[t].RunOn(world, set, out _);
            if (t % RollingHash.Every == 0 || t == ticks) seen[t] = Seen(world);
        }
        return seen;
    }

    [Test]
    public void ALateJoinerReplaysTheSameStepsFromTheStandsKept()
    {
        const int Ticks = 360;
        var rules = LoadedRules();
        var stepped = rules.WithMods(new[] { ModPatch.Parse(BoltsAndMeteors) });
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var asked = Walks(seed, Ticks);
            var live = Meadow(rules);
            var keeper = new StandKeeper(() => true, live.InBounds);
            var host = StrikeChannel(new Dictionary<int, TickBatch>(), keeper);
            var all = new List<StrikeIntent>();
            var seen = new Dictionary<int, string>();
            for (int t = 1; t <= Ticks; t++)
            {
                foreach (var s in asked[t]) host.Request(s);
                var stamped = host.Stamp(t);
                all.AddRange(stamped);
                if (t == 1) live.ApplyRules(stepped);
                foreach (var s in stamped) live.ApplyStrike(s.ToCommand());
                live.Step();
                keeper.Ran(t, live.TriggerFirings);
                if (t % RollingHash.Every == 0 || t == Ticks) seen[t] = Seen(live);
            }
            var kept = host.HistoryForJoiner();
            Assert.That(live.TriggeredCasts, Is.GreaterThan(10), $"seed {seed}: the walks fire triggers");

            // The kept history alone, and the kept history up to a tick with every command from it on (the live batches).
            foreach (int from in new[] { Ticks + 1, 61, 152, 245, 300 })
            {
                var strikes = kept.Where(s => s.Tick < from).Concat(all.Where(s => s.Tick >= from));
                var replayed = Replay(rules, BoltsAndMeteors, strikes, Ticks);
                foreach (var kv in seen)
                    Assert.That(replayed[kv.Key], Is.EqualTo(kv.Value), $"seed {seed}, live from {from}: tick {kv.Key} (cells/casts/firings/triggered casts)");
            }
            Assert.That(kept.Length, Is.LessThan(all.Count * 3 / 4), $"seed {seed}: kept {kept.Length} of {all.Count}");
        }
    }

    // Defense and room commands as NetChem has them on their channels, with the real intents.
    static CommandChannel<DefenseIntent> DefenseChannel(Dictionary<int, TickBatch> batches) => new(batches, (batch, d) => batch.Defense.Add(d.ToCommand()));

    static CommandChannel<RoomIntent> RoomChannel(Dictionary<int, TickBatch> batches) => new(batches, (batch, r) => batch.Room.Add(r.ToCommand()));

    static DefenseIntent Gate(int x) => DefenseIntent.From(new DefenseCommand(DefenseKind.Start, x, 1, x, 9), 0);

    static RoomIntent Mod(string peer, string mod) => RoomIntent.From(RoomCommand.Apply(peer, mod, "{}"), 0);

    static int[] Gates(TickBatch batch) => batch.Defense.Select(d => d.GateX).ToArray();

    static string[] Mods(TickBatch batch) => batch.Room.Select(r => r.Peer + ":" + r.Mod).ToArray();

    [Test]
    public void DefenseHistoryReplaysAsTimelineDidWithIt()
    {
        // The defense history comes in SendHistoryRpc's defense parameter; the joiner's channel adds it
        // to the timeline, as TickBatch.Timeline(defense:) did. After a reset too (from > 1).
        var host = DefenseChannel(new Dictionary<int, TickBatch>());
        host.Request(Gate(2));
        host.Stamp(3);
        host.Request(Gate(4));
        host.Request(Gate(6));
        var stamped = host.Stamp(5);
        Assert.That(stamped.Select(d => d.Tick), Is.EqualTo(new[] { 5, 5 }), "DefenseIntent.Tick is what the stamp sets");
        var history = host.HistoryForJoiner();

        foreach (int from in new[] { 1, 4 })
        {
            var joiner = DefenseChannel(new Dictionary<int, TickBatch>());
            joiner.Joining(history);
            var timeline = TickBatch.Timeline(null, null, 6, null, null, null, from);
            joiner.Replay(timeline);
            var before = TickBatch.Timeline(null, null, 6, null, null, history.Select(d => (d.Tick, d.ToCommand())), from);
            Assert.That(timeline.Keys, Is.EquivalentTo(before.Keys));
            foreach (int t in before.Keys) Assert.That(Gates(timeline[t]), Is.EqualTo(Gates(before[t])), $"from {from}, tick {t}");
        }
    }

    [Test]
    public void AResetKeepsTheRoomCommandsStampedOnItsTick()
    {
        // StampResetRules runs before StampRoom in the host's tick: the reset forgets the history
        // before it, and what was asked meanwhile is stamped on the reset's tick and kept.
        var host = RoomChannel(new Dictionary<int, TickBatch>());
        host.Request(Mod("Player 1", "old"));
        host.Stamp(3);
        host.Request(Mod("Player 2", "asked"));
        host.ForgetBefore(9);
        var stamped = host.Stamp(9);
        Assert.That(stamped.Select(r => r.Tick), Is.EqualTo(new[] { 9 }), "RoomIntent.Tick is what the stamp sets");
        host.Request(Mod("Player 1", "after"));
        host.Stamp(10);
        Assert.That(host.HistoryForJoiner().Select(r => $"{r.Mod}@{r.Tick}"), Is.EqualTo(new[] { "asked@9", "after@10" }));
    }

    [Test]
    public void AResetLeavesHeldRoomCommandsToJoinTheirBatch()
    {
        // A peer's held commands aren't history: a reset on the host never touches them.
        var batches = new Dictionary<int, TickBatch>();
        var peer = RoomChannel(batches);
        peer.Receive(9, new[] { Mod("Player 2", "a"), Mod("Player 1", "b") });
        peer.ForgetBefore(10);
        batches[9] = new TickBatch(9);
        peer.Deliver(9);
        Assert.That(Mods(batches[9]), Is.EqualTo(new[] { "Player 2:a", "Player 1:b" }));
    }

    [Test]
    public void ALateJoinerReplaysTheRoomHistoryAsAddRoomDid()
    {
        var host = RoomChannel(new Dictionary<int, TickBatch>());
        host.Request(Mod("Player 2", "a"));
        host.Request(RoomIntent.From(RoomCommand.Found("Player 1", new ThingId(7)), 0));
        host.Stamp(2);
        host.Request(Mod("Player 1", "b"));
        host.Stamp(4);
        var history = host.HistoryForJoiner();

        var joiner = RoomChannel(new Dictionary<int, TickBatch>());
        joiner.Joining(history);
        var timeline = TickBatch.Timeline(null, null, 5);
        joiner.Replay(timeline);
        var before = TickBatch.Timeline(null, null, 5);
        TickBatch.AddRoom(before, history.Select(r => (r.Tick, r.ToCommand())));
        for (int t = 1; t <= 5; t++) Assert.That(Mods(timeline[t]), Is.EqualTo(Mods(before[t])), $"tick {t}");
        Assert.That(Mods(timeline[2]), Is.EqualTo(new[] { "Player 2:a", "Player 1:7" }));
    }

    [Test]
    public void ClearDropsEverything()
    {
        var batches = new Dictionary<int, TickBatch>();
        var channel = Channel(batches);
        channel.Request(N("p0", "water"));
        channel.Stamp(1);
        channel.Request(N("p0", "fire"));
        channel.Receive(2, new[] { N("p0", "air") });
        channel.Joining(new[] { new Note { Player = "p0", What = "earth", Tick = 1 } });
        channel.Clear();
        Assert.That(channel.Stamp(3), Is.Empty);
        Assert.That(channel.HistoryForJoiner(), Is.Empty);
        batches[2] = new TickBatch(2);
        channel.Deliver(2);
        Assert.That(batches[2].Combos, Is.Empty);
        var timeline = TickBatch.Timeline(null, null, 2);
        channel.Replay(timeline);
        Assert.That(timeline[1].Combos, Is.Empty);
    }
}
