using System.Collections.Generic;
using System.Linq;
using Chem;
using NUnit.Framework;

// CommandChannel, one kind of command on its way through the lockstep network, without a network:
// the host's stamp, the stamped commands joining their tick's batch whether they come before it or
// after, a late joiner's history, a reset forgetting what came before, and the compaction hook;
// then defense and room commands on it as NetChem has them, with their real intents.
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

    [Test]
    public void TheHookCanKeepEveryStandWhileAStepTriggerIsOn()
    {
        // NetChem.StampStrikes' rule as a hook: a stand replaces the player's stands since the last
        // strike, unless a step trigger is in force, when every stand is a step and is kept.
        bool steps = false;
        int lastStrikeTick = 0;
        var host = Channel(new Dictionary<int, TickBatch>(), (history, n) =>
        {
            if (n.What == "stand" && !steps) history.RemoveAll(h => h.What == "stand" && h.Player == n.Player && h.Tick > lastStrikeTick);
            else lastStrikeTick = n.Tick;
            history.Add(n);
        });
        host.Request(N("p0", "stand"));
        host.Stamp(1);
        host.Request(N("p0", "stand"));
        host.Stamp(2);
        Assert.That(host.HistoryForJoiner().Select(n => n.Tick), Is.EqualTo(new[] { 2 }), "no trigger: the newer stand replaces the older");

        steps = true;
        host.Request(N("p0", "stand"));
        host.Stamp(3);
        host.Request(N("p0", "stand"));
        host.Stamp(4);
        Assert.That(host.HistoryForJoiner().Select(n => $"{n.What}@{n.Tick}"), Is.EqualTo(new[] { "stand@2", "stand@3", "stand@4" }),
            "a step trigger on: every stand kept for the joiner's replay");

        steps = false;
        host.Request(N("p0", "stand"));
        host.Stamp(5);
        Assert.That(host.HistoryForJoiner().Select(n => n.Tick), Is.EqualTo(new[] { 2, 3, 4, 5 }), "the stands kept as steps stay");
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
