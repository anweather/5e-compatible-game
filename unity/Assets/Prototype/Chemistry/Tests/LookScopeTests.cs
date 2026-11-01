using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Scoped and mixed looks (LookScopes.cs; docs/design/world-tools.md "Scoped and mixed looks"): the
    /// colour the view draws at a cell from the looks in force and where the players stand.
    /// </summary>
    public class LookScopeTests
    {
        static Rules Base => ChemistryTests.R;

        static readonly LookColour Green = new LookColour(0.2f, 0.9f, 0.3f), Blue = new LookColour(0.25f, 0.45f, 1f), Red = new LookColour(1f, 0.1f, 0.1f);

        // A box of a place and a village far off, in a world of their own.
        static readonly WorldPlaces Places = WorldPlaces.FromJson(@"{ ""places"": [
            { ""id"": ""box"", ""name"": ""the box"", ""area"": [20, 20, 24, 24] },
            { ""id"": ""far"", ""area"": [100, 100, 110, 110] } ] }");

        static readonly IReadOnlyList<PlayerSpot> Nobody = Array.Empty<PlayerSpot>();

        static ModPatch Mod(string json) => ModPatch.Parse(json.Replace('\'', '"'));

        // A mod giving fire's look these scoped looks (a JSON list) and maybe a mix.
        static ModPatch FireLook(string scoped, string mix = null) =>
            Mod("{ 'elements': { 'fire': { 'look': { 'color': [0.95, 0.42, 0.12]" + (mix == null ? "" : ", 'mix': " + mix) + (scoped == null ? "" : ", 'scoped': " + scoped) + " } } } }");

        static CellLook Fire(params ModPatch[] mods) => LookScopes.Compile(Base.WithMods(mods), Places).Of("elements", "fire");

        static LookColour Plain => LookColour.Of(Base.ElementLook("fire"));

        static void Same(LookColour got, LookColour want, string why = null) =>
            Assert.That(got.Near(want), Is.True, $"{why}: got {got}, want {want}");

        static LookColour Lerp(LookColour a, LookColour b, float t) =>
            new LookColour(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

        [Test]
        public void AScopeHoldsInsideAndBlendsOverItsEdge()
        {
            var fire = Fire(FireLook("[{ 'where': { 'at': [50, 50], 'within': 4 }, 'color': [0.2, 0.9, 0.3] }]"));
            Assert.That(fire.Plain, Is.False);
            Same(fire.At(50, 50, Nobody), Green, "the middle");
            Same(fire.At(54, 50, Nobody), Green, "at within");
            Same(fire.At(55, 50, Nobody), Lerp(Plain, Green, 2f / 3), "one cell past");
            Same(fire.At(56, 50, Nobody), Lerp(Plain, Green, 1f / 3), "two cells past");
            Same(fire.At(57, 50, Nobody), Plain, "the edge's width past: the plain look");
            Same(fire.At(0, 0, Nobody), Plain, "far off");

            var box = Fire(FireLook("[{ 'where': { 'place': 'box', 'within': 2 }, 'color': [0.2, 0.9, 0.3] }]"));
            Same(box.At(22, 22, Nobody), Green, "in the place");
            Same(box.At(26, 22, Nobody), Green, "within 2 of it");
            Same(box.At(27, 22, Nobody), Lerp(Plain, Green, 2f / 3), "past within");
            Same(box.At(30, 22, Nobody), Plain);
            Same(Fire(FireLook("[{ 'where': { 'place': 'the box' }, 'color': [0.2, 0.9, 0.3] }]")).At(24, 24, Nobody), Green, "named, with no within: the place's own cells");
        }

        [Test]
        public void ThePlaceAWhereNamesMustExistOrItNeverHolds()
        {
            var fire = Fire(FireLook("[{ 'where': { 'place': 'atlantis' }, 'color': [0.2, 0.9, 0.3] }]"));
            Assert.That(fire.Scopes, Is.EqualTo(0));
            Same(fire.At(22, 22, Nobody), Plain);
        }

        [Test]
        public void WhereScopesOverlapTheLatestWins()
        {
            var fire = Fire(FireLook("[{ 'where': { 'at': [50, 50], 'within': 6 }, 'color': [0.2, 0.9, 0.3] }, { 'where': { 'at': [56, 50], 'within': 6 }, 'color': [0.25, 0.45, 1] }]"));
            Same(fire.At(53, 50, Nobody), Blue, "in both: the later");
            Same(fire.At(46, 50, Nobody), Green, "in the first only");

            // Two mods, each one scoped look: both stay on, and the one switched on later wins.
            var green = FireLook("[{ 'where': { 'at': [50, 50], 'within': 6 }, 'color': [0.2, 0.9, 0.3] }]");
            var blue = FireLook("[{ 'where': { 'at': [56, 50], 'within': 6 }, 'color': [0.25, 0.45, 1] }]");
            Same(Fire(green, blue).At(53, 50, Nobody), Blue);
            Same(Fire(green, blue).At(46, 50, Nobody), Green, "the earlier mod's look is kept");
            Same(Fire(blue, green).At(53, 50, Nobody), Green, "the other order");
        }

        [Test]
        public void ASpellsOwnColourWins()
        {
            var fire = Fire(FireLook("[{ 'where': { 'at': [50, 50], 'within': 6 }, 'color': [0.2, 0.9, 0.3] }]"));
            Same(fire.At(50, 50, Nobody, Red), Red);
            Same(fire.At(0, 0, Nobody, Red), Red);
        }

        [Test]
        public void AroundAPlayerFollowsThemAndLapsesWithoutThem()
        {
            var fire = Fire(FireLook("[{ 'where': { 'around': 'host', 'within': 5 }, 'color': [0.2, 0.9, 0.3] }]"));
            var here = new List<PlayerSpot> { new PlayerSpot("guest", 10, 10), new PlayerSpot("host", 30, 30) };
            Same(fire.At(32, 30, here), Green, "near the host");
            Same(fire.At(10, 10, here), Plain, "near someone else");
            here[1] = new PlayerSpot("host", 10, 12);
            Same(fire.At(32, 30, here), Plain, "the host walked off: a fire left behind changes back");
            Same(fire.At(10, 10, here), Green, "and the fire where they are now takes it");
            Same(fire.At(10, 12, Nobody), Plain, "the host isn't in the game: the look lapses");
            Same(fire.At(10, 12, null), Plain);
        }

        [Test]
        public void RevertingTheModPutsThePlainLookBack()
        {
            var mod = FireLook("[{ 'where': { 'place': 'box', 'within': 3 }, 'color': [0.2, 0.9, 0.3] }]", "[[1, 0, 0], [0, 0, 1]]");
            Same(Fire(mod).At(22, 22, Nobody), Green);
            var reverted = Fire();
            Assert.That(reverted.Plain, Is.True);
            Same(reverted.At(22, 22, Nobody), Plain);
        }

        [Test]
        public void AMixSharesTheCellsByItsWeights()
        {
            var fire = Fire(FireLook(null, "[{ 'color': [1, 0, 0], 'weight': 3 }, { 'color': [0, 0, 1], 'weight': 1 }]"));
            int red = 0, n = 0;
            for (int y = 0; y < 200; y++)
                for (int x = 0; x < 200; x++, n++)
                {
                    var c = fire.At(x, y, Nobody);
                    Assert.That(c.R + c.B, Is.EqualTo(1f), "every cell is one of the mix's colours");
                    if (c.R > 0.5f) red++;
                }
            Assert.That((double)red / n, Is.EqualTo(0.75).Within(0.01), "three red in four");

            // A scoped mix picks its own patchwork; outside it the plain look's mix holds.
            var scoped = Fire(FireLook("[{ 'where': { 'place': 'far' }, 'mix': [[0, 1, 0], [0, 1, 1], [1, 1, 0]] }]", "[[1, 0, 0], [0, 0, 1]]"));
            var seen = new HashSet<string>();
            for (int y = 100; y <= 110; y++)
                for (int x = 100; x <= 110; x++) seen.Add(scoped.At(x, y, Nobody).ToString());
            Assert.That(seen.Count, Is.EqualTo(3), "all three colours show in the place");
            var c0 = scoped.At(0, 0, Nobody);
            Assert.That(c0.G, Is.EqualTo(0f), "outside it, the plain mix");
        }

        [Test]
        public void ThePatchworkIsTheSameOnEveryRunAndPeer()
        {
            var mod = FireLook("[{ 'where': { 'place': 'box', 'within': 8 }, 'mix': [[0, 1, 0], [0, 1, 1], [1, 1, 0]] }]", "[[1, 0, 0], [0, 0, 1], [1, 1, 1]]");
            var one = Fire(mod);
            var two = Fire(mod);
            uint a = Digest(one), b = Digest(two);
            Assert.That(a, Is.EqualTo(b), "two compiles, the same colours");
            Assert.That(a, Is.EqualTo(0x7285A64Eu), "golden: the hash a cell picks by must not move, or peers on different versions see different patchworks");
        }

        // FNV-1a over the colours of a block of cells, each channel in 1/1000ths.
        static uint Digest(CellLook look)
        {
            unchecked
            {
                uint h = 0x811c9dc5;
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        var c = look.At(x, y, Nobody);
                        foreach (float v in new[] { c.R, c.G, c.B }) h = (h ^ (uint)Math.Round(v * 1000)) * 0x01000193u;
                    }
                return h;
            }
        }

        [Test]
        public void AtAllocatesNothing()
        {
            var fire = Fire(FireLook("[{ 'where': { 'place': 'box', 'within': 3 }, 'mix': [[0, 1, 0], [0, 1, 1]] }, { 'where': { 'around': 'host', 'within': 5 }, 'color': [0.2, 0.9, 0.3] }]", "[[1, 0, 0], [0, 0, 1]]"));
            var players = new List<PlayerSpot> { new PlayerSpot("host", 30, 30) };
            float Sweep()
            {
                float sum = 0;
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++) sum += fire.At(x, y, players, y == 0 ? Red : (LookColour?)null).G;
                return sum;
            }
            for (int warm = 0; warm < 3; warm++) Sweep();
            // The runtime may allocate a little once around a measured loop (its JIT), so what's checked is
            // that the bytes don't grow with the calls: 4096 calls and 12288 allocate the same.
            long Measure(int sweeps)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < sweeps; i++) Sweep();
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
            long one = Measure(1), three = Measure(3);
            Assert.That(three, Is.EqualTo(one), "nothing per call");
            Assert.That(one, Is.LessThan(64));
            float sum = Sweep();
            Assert.That(sum, Is.GreaterThan(0));
        }

        [Test]
        public void LooksStayOutOfTheFingerprint()
        {
            var mod = FireLook("[{ 'where': { 'around': 'host', 'within': 5 }, 'color': [0.2, 0.9, 0.3] }]", "[[1, 0, 0], [0, 0, 1]]");
            Assert.That(Base.WithMods(new[] { mod }).Fingerprint, Is.EqualTo(Base.Fingerprint));
            var cover = Mod("{ 'covers': { 'oil': { 'name': 'Oil', 'tags': [], 'look': { 'color': [0.1, 0.1, 0.1], 'scoped': [{ 'where': { 'place': 'box' }, 'color': [1, 1, 0] }] } } } }");
            var oil = LookScopes.Compile(Base.WithMods(new[] { cover }), Places).Of("covers", "oil");
            Same(oil.At(22, 22, Nobody), new LookColour(1, 1, 0), "a cover's look scopes too");
        }

        [Test]
        public void ABadMixOrWhereIsRefused()
        {
            string Errors(string look) => string.Join(" | ",
                Assert.Throws<ModException>(() => Base.WithMods(new[] { Mod("{ 'elements': { 'fire': { 'look': " + look + " } } }") })).Errors);
            StringAssert.Contains("look.mix must be a list of colours with weights", Errors("{ 'mix': [{ 'color': [1, 0, 0], 'weight': 0 }] }"));
            StringAssert.Contains("look.scoped[0] needs a color or a mix", Errors("{ 'scoped': [{ 'where': { 'place': 'box' } }] }"));
            StringAssert.Contains("look.scoped[0].where names one of place, at or around", Errors("{ 'scoped': [{ 'where': { 'place': 'box', 'at': [1, 2] }, 'color': [1, 0, 0] }] }"));
            StringAssert.Contains("needs within (cells) round a cell or a player", Errors("{ 'scoped': [{ 'where': { 'around': 'host' }, 'color': [1, 0, 0] }] }"));
            StringAssert.Contains("look.scoped[0].where must be an object", Errors("{ 'scoped': [{ 'where': 'box', 'color': [1, 0, 0] }] }"));
        }
    }
}
