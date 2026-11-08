using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Scoped and mixed looks (LookScopes.cs; docs/design/world-tools.md "Scoped and mixed looks"): the
    /// colour the view draws at a cell from the looks in force and where the players stand.
    /// </summary>
    public class LookScopeTests
    {
        [Test]
        public void ALookRoundAPlayerSaysWhoItFollowsAndHowFar()
        {
            var patch = ModPatch.Parse("{ \"elements\": { \"fire\": { \"look\": { \"color\": [0.95, 0.42, 0.12], \"scoped\": [ { \"where\": { \"around\": \"Player 2\", \"within\": 9 }, \"color\": [0.2, 0.9, 0.3] } ] } } } }");
            var scopes = LookScopes.Compile(ChemistryTests.R.WithMods(new[] { patch }), WorldPlaces.Empty);
            Assert.That(scopes.Followers, Is.EqualTo(new[] { ("Player 2", 9) }));
            Assert.That(scopes.Of("elements", "fire").Colour.Near(new LookColour(0.95f, 0.42f, 0.12f)), Is.True, "what a scope's edge fades into");
            Assert.That(LookScopes.Compile(ChemistryTests.R, WorldPlaces.Empty).Followers, Is.Empty);
        }

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

        // A scope's edge mixes into the plain look like paint (Colors), t of the way to the scope's colour.
        static LookColour Lerp(LookColour a, LookColour b, float t) => Colors.Mix(a, b, t);

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
        public void WhereScopesOverlapTheyMixLikePaint()
        {
            var yellow = new LookColour(1f, 0.9f, 0.1f);
            var fire = Fire(FireLook("[{ 'where': { 'at': [50, 50], 'within': 6 }, 'color': [0.25, 0.45, 1] }, { 'where': { 'at': [56, 50], 'within': 6 }, 'color': [1, 0.9, 0.1] }]"));
            var middle = fire.At(53, 50, Nobody);
            Same(middle, Colors.Mix(Blue, yellow), "halfway between, as deep in each: blue and yellow mixed evenly");
            Assert.That(middle.G, Is.GreaterThan(middle.R).And.GreaterThan(middle.B), "and that's green");
            var nearBlue = fire.At(51, 50, Nobody);
            Assert.That(nearBlue.B, Is.GreaterThan(middle.B), "deeper in the blue one: bluer");
            Same(fire.At(44, 50, Nobody), Blue, "in the first only");

            // Two mods, each one scoped look: both stay on, and the order they went on in doesn't matter.
            var blue = FireLook("[{ 'where': { 'at': [50, 50], 'within': 6 }, 'color': [0.25, 0.45, 1] }]");
            var gold = FireLook("[{ 'where': { 'at': [56, 50], 'within': 6 }, 'color': [1, 0.9, 0.1] }]");
            Same(Fire(blue, gold).At(53, 50, Nobody), middle);
            Same(Fire(gold, blue).At(53, 50, Nobody), middle, "the other order");
            Same(Fire(gold, blue).At(52, 50, Nobody), Fire(blue, gold).At(52, 50, Nobody));
        }

        // ---------- colour theory ----------

        static float Hue(LookColour c)
        {
            float top = Math.Max(c.R, Math.Max(c.G, c.B)), spread = top - Math.Min(c.R, Math.Min(c.G, c.B));
            float h = 4 + (c.R - c.G) / spread;
            if (c.R >= c.G && c.R >= c.B) h = (c.G - c.B) / spread;
            else if (c.G >= c.B) h = 2 + (c.B - c.R) / spread;
            return (h * 60 + 360) % 360;
        }

        static readonly LookColour PureRed = new LookColour(1, 0, 0), PureBlue = new LookColour(0, 0, 1), PureYellow = new LookColour(1, 1, 0);

        [Test]
        public void ColoursMixLikePaint()
        {
            Assert.That(Hue(Colors.Mix(PureBlue, PureYellow)), Is.InRange(90f, 150f), "blue and yellow: green");
            Assert.That(Hue(Colors.Mix(Blue, new LookColour(1f, 0.9f, 0.1f))), Is.InRange(90f, 150f), "a game blue and a gold: green too");
            Assert.That(Hue(Colors.Mix(PureRed, PureBlue)), Is.InRange(260f, 310f), "red and blue: purple");
            Assert.That(Hue(Colors.Mix(PureRed, PureYellow)), Is.InRange(20f, 45f), "red and yellow: orange");
            var rgbAverage = new LookColour(0.5f, 0.5f, 0.5f);
            Assert.That(Colors.Mix(PureBlue, PureYellow).Near(rgbAverage), Is.False, "not the grey an RGB average gives");
            foreach (var c in new[] { Red, Green, Blue, LookColour.Of(Base.ElementLook("fire")), new LookColour(0.5f, 0.5f, 0.5f), new LookColour(0.3f, 0.1f, 0.7f), new LookColour(0, 0, 0) })
            {
                Assert.That(Colors.Mix(c, c).Near(c), Is.True, $"{c} with itself is {c}, got {Colors.Mix(c, c)}");
                Assert.That(Colors.Mix(c, PureBlue, 0).Near(c), Is.True, "none of the other: unchanged");
            }
        }

        [Test]
        public void AMixDoesntCareWhichCameFirstAndHonoursItsWeights()
        {
            Assert.That(Colors.Mix(PureBlue, PureYellow).Near(Colors.Mix(PureYellow, PureBlue)), Is.True);
            ColourMixer one = default, two = default;
            one.Add(Red, 2);
            one.Add(Blue, 1);
            one.Add(Green, 3);
            two.Add(Green, 3);
            two.Add(Red, 2);
            two.Add(Blue, 1);
            Assert.That(one.Result(Red).Near(two.Result(Red)), Is.True, "three colours in either order");
            var bluer = Colors.Mix(PureBlue, PureYellow, 0.25f);
            var yellower = Colors.Mix(PureBlue, PureYellow, 0.75f);
            Assert.That(bluer.B, Is.GreaterThan(Colors.Mix(PureBlue, PureYellow).B), "more blue, bluer");
            Assert.That(yellower.R, Is.GreaterThan(Colors.Mix(PureBlue, PureYellow).R), "more yellow, yellower");
            ColourMixer twice = default;
            twice.Add(PureBlue, 2);
            twice.Add(PureYellow, 6);
            Assert.That(twice.Result(Red).Near(yellower), Is.True, "weights 2 and 6 are a quarter of the way");
            Assert.That(default(ColourMixer).Result(Red).Near(Red), Is.True, "nothing added: the fallback");
            Assert.That(Colors.Mix(new LookColour(0.2f, 0.4f, 0.9f), new LookColour(0.9f, 0.6f, 0.1f), 0.3f).ToString(),
                Is.EqualTo(Colors.Mix(new LookColour(0.2f, 0.4f, 0.9f), new LookColour(0.9f, 0.6f, 0.1f), 0.3f).ToString()), "the same on every run");
        }

        [Test]
        public void ABlendIsOneColourAndAFireCaughtFromTwoTakesTheirMix()
        {
            var fire = Fire(FireLook(null, "[[0, 0, 1], [1, 1, 0]]"));
            var blend = Fire(Mod("{ 'elements': { 'fire': { 'look': { 'color': [0.95, 0.42, 0.12], 'mix': [[0, 0, 1], [1, 1, 0]], 'blend': true } } } }"));
            var green = Colors.Mix(PureBlue, PureYellow);
            for (int x = 0; x < 20; x++) Same(blend.At(x, 3, Nobody), green, "every cell the one blended colour");
            var seen = new HashSet<string>();
            for (int x = 0; x < 20; x++) seen.Add(fire.At(x, 3, Nobody).ToString());
            Assert.That(seen.Count, Is.EqualTo(2), "without blend, a patchwork of the two");
            ColourMixer neighbours = default;
            neighbours.Add(PureBlue);
            neighbours.Add(PureYellow);
            Same(fire.Caught(neighbours), green, "a blue player's fire meets a yellow one's: a green front");
            Same(fire.Caught(default), LookColour.Of(Base.ElementLook("fire")), "caught from nothing coloured: the plain look");
        }

        [Test]
        public void TwoLooksByTheSamePlayerMixAndOneNamingThemBeatsEach()
        {
            var mine = Fire(FireLook("[{ 'by': 'each', 'mix': [[1, 0.1, 0.1], [0.25, 0.45, 1], [0.2, 0.9, 0.3]] }, { 'by': 1, 'color': [0.2, 0.9, 0.3] }]"));
            var both = Fire(FireLook("[{ 'by': 1, 'color': [0, 0, 1] }]"), FireLook("[{ 'by': 1, 'color': [1, 1, 0] }]"));
            var players = new List<PlayerSpot> { new PlayerSpot("host", 0, 0, 0), new PlayerSpot("guest", 5, 5, 1) };
            Same(mine.At(7, 7, players, null, 1), Green, "a look naming the slot is stronger than each");
            Same(both.At(7, 7, players, null, 1), Colors.Mix(PureBlue, PureYellow), "two of the same strength mix");
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
            Assert.That(a, Is.EqualTo(0x70396988u), "golden: the hash a cell picks by must not move, or peers on different versions see different patchworks");
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
        [NonParallelizable] // with fixtures beside it, the runtime's own work on this thread (its JIT) once landed in a measured loop: 6560 bytes
        public void AtAllocatesNothing()
        {
            var fire = Fire(FireLook("[{ 'where': { 'place': 'box', 'within': 3 }, 'mix': [[0, 1, 0], [0, 1, 1]] }, { 'where': { 'around': 'host', 'within': 5 }, 'color': [0.2, 0.9, 0.3] }, { 'by': 'each', 'mix': [[1, 0, 0], [0, 0, 1]] }, { 'by': 1, 'color': [0, 1, 0] }]", "[[1, 0, 0], [0, 0, 1]]"));
            var players = new List<PlayerSpot> { new PlayerSpot("host", 30, 30, 0), new PlayerSpot("guest", 3, 3, 1) };
            float Sweep()
            {
                float sum = 0;
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++) sum += fire.At(x, y, players, y == 0 ? Red : (LookColour?)null, x % 3 == 0 ? (int?)null : x % 3 - 1).G;
                return sum;
            }
            // Past the runtime's 30 calls before it recompiles a method (tiered JIT), so that work is done before the measuring.
            for (int warm = 0; warm < 32; warm++) Sweep();
            // The runtime may still allocate a little once around a measured loop, so what's checked is
            // that the bytes on this thread don't grow with the calls: 4096 calls and 12288 allocate the same.
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

        // ---------- by player ----------

        static readonly List<PlayerSpot> ThreePlayers = new List<PlayerSpot>
        {
            new PlayerSpot("host", 0, 0, 0), new PlayerSpot("guest", 5, 5, 1), new PlayerSpot("third", 9, 9, 2),
        };

        [Test]
        public void ByWinsThenASpellsOwnColourThenWhereThenTheElement()
        {
            var fire = Fire(FireLook("[{ 'by': 1, 'color': [0.25, 0.45, 1] }, { 'where': { 'at': [50, 50], 'within': 6 }, 'color': [0.2, 0.9, 0.3] }]"));
            Assert.That(fire.ByPlayers, Is.True);
            Same(fire.At(50, 50, ThreePlayers, Red, 1), Blue, "by: the guest's fire is blue, over a spell's own red and the where");
            Same(fire.At(0, 0, ThreePlayers, null, 1), Blue, "and anywhere");
            Same(fire.At(50, 50, ThreePlayers, Red, 0), Red, "the host has no by: a spell's own colour");
            Same(fire.At(50, 50, ThreePlayers, null, 0), Green, "no spell colour: the where");
            Same(fire.At(50, 50, ThreePlayers), Green, "no caster known: the where");
            Same(fire.At(0, 0, ThreePlayers, null, 0), Plain, "none of them: the element's colour");
        }

        [Test]
        public void EachPlayerGetsTheirOwnColourAndASlotThatLeavesLapses()
        {
            var fire = Fire(FireLook("[{ 'by': 'each', 'mix': [[1, 0.1, 0.1], [0.25, 0.45, 1], [0.2, 0.9, 0.3]] }]"));
            Same(fire.At(7, 7, ThreePlayers, null, 0), Red, "slot 0, the first colour");
            Same(fire.At(7, 7, ThreePlayers, null, 1), Blue, "slot 1, the second");
            Same(fire.At(7, 7, ThreePlayers, Red, 2), Green, "slot 2, the third, over a spell's own colour");
            var left = new List<PlayerSpot> { ThreePlayers[0], ThreePlayers[2] };
            Same(fire.At(7, 7, left, null, 1), Plain, "the guest left: their fire lapses to the element's colour");
            Same(fire.At(7, 7, left, null, 2), Green, "and the others keep theirs: slots don't close up");
            var fourth = new List<PlayerSpot>(ThreePlayers) { new PlayerSpot("fourth", 1, 1, 3) };
            Same(fire.At(7, 7, fourth, null, 3), Red, "more players than colours: round again");
            Same(fire.At(7, 7, null, null, 1), Plain, "nobody known in the game: no by");

            // One player's own colour, latest wins over each; it lapses when they leave, and each takes over again only while they're in.
            var mine = Fire(FireLook("[{ 'by': 'each', 'mix': [[1, 0.1, 0.1], [0.25, 0.45, 1], [0.2, 0.9, 0.3]] }, { 'by': 1, 'color': [0.2, 0.9, 0.3] }]"));
            Same(mine.At(7, 7, ThreePlayers, null, 1), Green, "a look naming slot 1 is stronger than each");
            Same(mine.At(7, 7, left, null, 1), Plain, "gone: lapsed");
        }

        [Test]
        public void ASpellsOwnShowTakesALookByPlayer()
        {
            var doc = Newtonsoft.Json.Linq.JObject.Parse(Base.ToJson());
            var fireball = (Newtonsoft.Json.Linq.JObject)((Newtonsoft.Json.Linq.JArray)doc["spells"]).First(t => (string)t["id"] == "fireball").DeepClone();
            fireball["look"]["scoped"] = Newtonsoft.Json.Linq.JArray.Parse("[{ \"by\": 0, \"color\": [0.25, 0.45, 1] }]");
            var rules = Base.WithMods(new[] { ModPatch.Parse(new Newtonsoft.Json.Linq.JObject { ["spells"] = new Newtonsoft.Json.Linq.JArray(fireball) }.ToString()) });
            var show = LookScopes.Compile(rules, Places).Of("spells", "fireball");
            Same(show.At(0, 0, ThreePlayers, null, 0), Blue, "the host's Fireball is blue");
            Same(show.At(0, 0, ThreePlayers, null, 1), LookColour.Of(Base.Spell("fireball").Look), "everyone else's as it was");
            Assert.That(rules.Fingerprint, Is.EqualTo(Base.Fingerprint));
        }

        [Test]
        public void LooksStayOutOfTheFingerprint()
        {
            var mod = FireLook("[{ 'where': { 'around': 'host', 'within': 5 }, 'color': [0.2, 0.9, 0.3] }, { 'by': 'each', 'mix': [[1, 0, 0], [0, 0, 1]] }]", "[[1, 0, 0], [0, 0, 1]]");
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
            StringAssert.Contains("look.scoped[0].by must be a player's slot", Errors("{ 'scoped': [{ 'by': 'me', 'color': [1, 0, 0] }] }"));
            StringAssert.Contains("by each needs a mix", Errors("{ 'scoped': [{ 'by': 'each', 'color': [1, 0, 0] }] }"));
            StringAssert.Contains("gives one of where", Errors("{ 'scoped': [{ 'by': 0, 'where': { 'place': 'box' }, 'color': [1, 0, 0] }] }"));
        }
    }
}
