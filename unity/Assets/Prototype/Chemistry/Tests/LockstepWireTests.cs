// The lockstep wire (D19, LockstepWire.cs): numbers, strings and hashes come back as they went, a message too long for
// a send splits into frames that fit and joins again whole, and every standard channel's command round-trips.
using System.Collections.Generic;
using System.Linq;
using Chem.Lockstep;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Unit")]
    public class LockstepWireTests
    {
        [Test]
        public void NumbersStringsAndHashesComeBackAsTheyWent()
        {
            var numbers = new[] { 0, 1, -1, 63, -64, 64, 300, -300, int.MaxValue, int.MinValue };
            var texts = new[] { null, "", "fire-bolt", "Ana's “mod” ✓" };
            var w = new LockstepWriter();
            foreach (int n in numbers) w.Number(n);
            foreach (var t in texts) w.Text(t);
            w.Hash(0xcbf29ce484222325);
            w.Bool(true);
            var r = new LockstepReader(w.ToArray());
            Assert.That(numbers.Select(_ => r.Number()), Is.EqualTo(numbers));
            Assert.That(texts.Select(_ => r.Text()), Is.EqualTo(texts), "null stays apart from empty");
            Assert.That(r.Hash(), Is.EqualTo(0xcbf29ce484222325));
            Assert.That(r.Bool(), Is.True);
            Assert.That(r.AtEnd, Is.True);
            Assert.Throws<LockstepWireException>(() => r.Byte(), "past the end");
            Assert.Throws<LockstepWireException>(() => new LockstepReader(new byte[] { 9, (byte)'a' }).Text(), "a length past the end");
        }

        [Test]
        public void ALongMessageSplitsIntoFramesThatFitAndJoinsWhole()
        {
            var sender = new LockstepFrames();
            var receiver = new LockstepFrames();
            byte[] Bytes(int n, int seed) => Enumerable.Range(0, n).Select(i => (byte)((i * 31 + seed) % 251)).ToArray();
            var a = Bytes(5000, 1);
            var b = Bytes(700, 2);
            var small = Bytes(20, 3);
            var framesA = sender.Split(a, 256);
            var framesB = sender.Split(b, 256);
            var framesSmall = sender.Split(small, 256);
            Assert.That(framesA.Concat(framesB).Concat(framesSmall).All(f => f.Length <= 256), Is.True, "every frame fits a send");
            Assert.That(framesA.Count, Is.GreaterThan(20));
            Assert.That(framesSmall.Count, Is.EqualTo(1), "a short message goes whole");

            // Two messages' pieces interleaved, and a whole one between them, from one sender; and another sender's.
            var from = new PeerId(1);
            var got = new List<byte[]>();
            void Feed(PeerId p, byte[] frame)
            {
                if (receiver.TryJoin(p, frame, out var m)) got.Add(m);
            }
            for (int i = 0; i < framesA.Count; i++)
            {
                Feed(from, framesA[i]);
                if (i < framesB.Count) Feed(from, framesB[i]);
                if (i == 1) Feed(from, framesSmall[0]);
            }
            Assert.That(got.Count, Is.EqualTo(3));
            Assert.That(got[0], Is.EqualTo(small));
            Assert.That(got[1], Is.EqualTo(b));
            Assert.That(got[2], Is.EqualTo(a));

            var other = new LockstepFrames().Split(b, 64);
            got.Clear();
            foreach (var f in other) Feed(new PeerId(2), f);
            Assert.That(got.Single(), Is.EqualTo(b), "another sender's message numbers are its own");
            Assert.That(other.All(f => f.Length <= 64), Is.True);
        }

        [Test]
        public void EveryStandardChannelRoundTripsItsCommand()
        {
            string P(PeerId p) => LockstepChannels.PlayerName(p);
            var batch = RoundTrip(LockstepChannels.Casts(P), new CastCommand("fire-bolt", 3, -4, 2, 3, -1, 5, "p1"));
            var c = batch.Casts.Single();
            Assert.That((c.Spell, c.X, c.Y, c.Dir, c.Charge, c.AimX, c.AimY, c.By), Is.EqualTo(("fire-bolt", 3, -4, 2, 3, -1, 5, "p1")));

            var e = RoundTrip(LockstepChannels.Effects(), new EffectCommand(EffectKind.Spill, 7, 8, "lantern", new ThingId(0x1234567890abUL))).Effects.Single();
            Assert.That((e.Kind, e.X, e.Y, e.Entity, e.Thing.Value), Is.EqualTo((EffectKind.Spill, 7, 8, "lantern", 0x1234567890abUL)));

            var s = RoundTrip(LockstepChannels.Chunks(), new ChunkSwitch(2, 3, true)).Chunks.Single();
            Assert.That(s, Is.EqualTo(new ChunkSwitch(2, 3, true)));

            var d = RoundTrip(LockstepChannels.Defense(), new DefenseCommand(DefenseKind.Start, 1, 2, 3, 4)).Defense.Single();
            Assert.That((d.Kind, d.GateX, d.GateY, d.FlagX, d.FlagY), Is.EqualTo((DefenseKind.Start, 1, 2, 3, 4)));

            var k = RoundTrip(LockstepChannels.Combos(P, false), ComboCommand.Load("p1", "fire", true)).Combos.Single();
            Assert.That((k.Player, k.Kind, k.Element, k.Step, k.Hold), Is.EqualTo(("p1", ComboCommandKind.Load, "fire", 0, true)));
            Assert.That(RoundTrip(LockstepChannels.Combos(P, false), ComboCommand.Cancel("p1")).Combos.Single().Element, Is.Null, "no element stays none");

            Assert.That(RoundTrip(LockstepChannels.Dragon(), new DragonCommand()).Dragon.Count, Is.EqualTo(1));

            var g = RoundTrip(LockstepChannels.Course(P), new CourseCommand("p2", 9, 10)).Course.Single();
            Assert.That((g.Player, g.X, g.Y), Is.EqualTo(("p2", 9, 10)));

            var t = RoundTrip(LockstepChannels.Strikes(P), StrikeCommand.Strike("p1", 4, 5, -1, 1)).Strikes.Single();
            Assert.That((t.Player, t.Kind, t.X, t.Y, t.AimX, t.AimY, t.Hp), Is.EqualTo(("p1", StrikeKind.Strike, 4, 5, -1, 1, 0)));

            var m = RoundTrip(LockstepChannels.Room(LockstepChannels.PeerName), RoomCommand.Apply("Player 2", "big", "{ }", "Big")).Room.Single();
            Assert.That((m.Change, m.Peer, m.Mod, m.Json, m.Title), Is.EqualTo((RoomChange.Apply, "Player 2", "big", "{ }", "Big")));
        }

        [Test]
        public void TheWireIsTheSameBytesEveryTime()
        {
            var casts = LockstepChannels.Casts(LockstepChannels.PlayerName);
            byte[] Encode()
            {
                var w = new LockstepWriter();
                casts.Write(w, new CastCommand("fireball", 10, 20, 1, by: "p0"));
                return w.ToArray();
            }
            Assert.That(Encode(), Is.EqualTo(Encode()));
            Assert.That(Fnv1a64.Hash("a"), Is.EqualTo(0xaf63dc4c8601ec8cUL), "FNV-1a 64 of \"a\"");
        }

        // A command written, read back and added to a batch, as a peer's session does.
        static TickBatch RoundTrip<T>(LockstepChannel<T> channel, T command)
        {
            var w = new LockstepWriter();
            channel.Write(w, command);
            var r = new LockstepReader(w.ToArray());
            var batch = new TickBatch(1);
            channel.AddTo(batch, channel.Read(r));
            Assert.That(r.AtEnd, Is.True, channel.Name + " reads all it wrote");
            return batch;
        }
    }
}
