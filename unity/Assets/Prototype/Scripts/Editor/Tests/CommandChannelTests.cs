using System.Collections.Generic;
using System.Linq;
using Chem;
using NUnit.Framework;

// CommandChannel, one kind of command on its way through the lockstep network, without a network:
// the host's stamp, the stamped commands joining their tick's batch whether they come before it or
// after, a late joiner's history, a reset forgetting what came before, and the compaction hook.
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
        // As strikes will: a player's newer stand replaces the older one, a strike is always kept.
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
