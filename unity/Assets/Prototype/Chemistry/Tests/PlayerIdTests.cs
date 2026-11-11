using System;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The player's stable id (PlayerId.cs; docs/design/player-saves.md, step 2): made from random bytes, read back as
    /// written, and never a session's name for a player ("p0", "solo").
    /// </summary>
    [Category("Unit")]
    public class PlayerIdTests
    {
        static byte[] Bytes(byte from) => Enumerable.Range(from, PlayerId.Bytes).Select(b => (byte)b).ToArray();

        [Test]
        public void ANewIdIsItsBytesInHexAndReadsBack()
        {
            var id = PlayerId.New(Bytes(1));
            Assert.That(id.Value, Is.EqualTo("0102030405060708090a0b0c0d0e0f10"));
            Assert.That(PlayerId.Parse(id.Value), Is.EqualTo(id));
            Assert.That(PlayerId.New(Bytes(2)), Is.Not.EqualTo(id));
        }

        [TestCase("ann")]
        [TestCase("a")]
        [TestCase("player-2_b")]
        [TestCase("p")]
        [TestCase("p0x")]
        [TestCase("solo2")]
        public void AnIdIsReadAsWritten(string text)
        {
            Assert.That(PlayerId.TryParse(text, out var id), Is.True, PlayerId.Problem(text));
            Assert.That(id.Value, Is.EqualTo(text));
            Assert.That(id.ToString(), Is.EqualTo(text));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Ann")]
        [TestCase("an n")]
        [TestCase("ann!")]
        [TestCase("p0")]
        [TestCase("p17")]
        [TestCase("solo")]
        public void WhatIsntAnIdIsRefusedSayingWhy(string text)
        {
            Assert.That(PlayerId.TryParse(text, out var id), Is.False);
            Assert.That(id.IsNone, Is.True);
            Assert.That(PlayerId.Problem(text), Is.Not.Null);
            Assert.Throws<FormatException>(() => PlayerId.Parse(text));
        }

        [Test]
        public void ASessionsNameIsNeverAnId()
        {
            // What the game calls a player today (NetChem.PlayerName, TriggerDef.SoloPlayer) can't be taken for an id.
            foreach (ulong client in new ulong[] { 0, 1, 9, 42, ulong.MaxValue - 1 })
                Assert.That(PlayerId.TryParse("p" + client.ToString(System.Globalization.CultureInfo.InvariantCulture), out _), Is.False, $"client {client}");
            Assert.That(PlayerId.TryParse(TriggerDef.SoloPlayer, out _), Is.False);
        }

        [Test]
        public void TooLongOrNotRandomIsRefused()
        {
            Assert.That(PlayerId.TryParse(new string('a', PlayerId.MostLength), out _), Is.True);
            Assert.That(PlayerId.TryParse(new string('a', PlayerId.MostLength + 1), out _), Is.False);
            Assert.Throws<ArgumentException>(() => PlayerId.New(new byte[PlayerId.Bytes]));
            Assert.Throws<ArgumentException>(() => PlayerId.New(new byte[3]));
            Assert.Throws<ArgumentException>(() => PlayerId.New(null));
        }

        [Test]
        public void IdsCompareAsWritten()
        {
            var a = PlayerId.Parse("ann");
            var b = PlayerId.Parse("bo");
            Assert.That(a.CompareTo(b), Is.LessThan(0));
            Assert.That(a == PlayerId.Parse("ann"), Is.True);
            Assert.That(a != b, Is.True);
            Assert.That(default(PlayerId).IsNone, Is.True);
            Assert.That(new[] { b, a }.OrderBy(x => x).Select(x => x.Value), Is.EqualTo(new[] { "ann", "bo" }));
        }
    }
}
