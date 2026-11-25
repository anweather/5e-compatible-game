// Late join from a snapshot (docs/design/player-saves.md, step 6): a joiner loads the host's world as it stood between two
// ticks, then replays only the batches after it, rather than the whole history from tick 0 (NetChem's Timeline). The
// snapshot is the host's world save (World.Save, its land's marks included), the mods in force outside a room (a room's
// are in its save), and the host's checksum at that tick, the same parts peers compare (NetChem.Checksum). A joiner
// loads it over its own map, rules and land, and is refused, saying why, unless they're the host's and the load comes
// back to that checksum: a part of the world the save misses shows there, not ticks later as a desync.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>A world between two ticks, for a late joiner: its save, the mods in force and the checksum it comes back to.</summary>
    public sealed class JoinSnapshot
    {
        public const int Format = 1;

        /// <summary>The checksum's parts, in NetChem.Checksum's order, named for a refusal.</summary>
        public static readonly IReadOnlyList<string> PartNames = new[] { "cells", "rules", "things", "sky", "defense", "casts and stands", "dragon", "land" };

        JoinSnapshot(int tick, string save, IReadOnlyList<ActiveMod> mods, IReadOnlyList<string> checksum)
        {
            Tick = tick;
            Save = save;
            Mods = mods;
            Checksum = checksum;
        }

        /// <summary>The last tick the world had run when it was taken: a joiner replays the batches after it.</summary>
        public int Tick { get; }
        /// <summary>The world save (World.Save).</summary>
        public string Save { get; }
        /// <summary>The mods in force, outside a room (a room's are in its save); none in a room.</summary>
        public IReadOnlyList<ActiveMod> Mods { get; }
        /// <summary>The host's checksum at Tick, part by part (PartNames).</summary>
        public IReadOnlyList<string> Checksum { get; }

        /// <summary>A world's checksum part by part: what peers compare (NetChem.Checksum's parts, in its order).</summary>
        public static string[] ChecksumOf(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            return new[]
            {
                world.CellHash(), world.Rules.Fingerprint, world.ThingsNow.Fingerprint, world.SkyHash(), world.DefenseHash(),
                world.CastHash(), world.DragonHash(), world.LandHash(),
            };
        }

        /// <summary>
        /// The host's world now, between ticks (a tick in progress is finished first, as a save does), with the rules it's
        /// running under: what a joiner arriving now loads.
        /// </summary>
        public static JoinSnapshot Take(World world, IChunkSource source, RuleSet rules)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            string save = world.Save(source);
            var mods = world.Room == null ? rules.Mods.ToArray() : Array.Empty<ActiveMod>();
            return new JoinSnapshot(world.Tick, save, mods, ChecksumOf(world));
        }

        /// <summary>As it goes over the wire: one line of JSON, the same text on every machine.</summary>
        public string Text() => new JObject
        {
            ["format"] = Format,
            ["tick"] = Tick,
            ["mods"] = new JArray(Mods.Select(m => new JArray(m.Id, m.Json))),
            ["checksum"] = new JArray(Checksum.Cast<object>().ToArray()),
            ["world"] = Save,
        }.ToString(Formatting.None);

        /// <summary>A snapshot from its text; SaveException, saying why, if it isn't one.</summary>
        public static JoinSnapshot Parse(string text)
        {
            JObject o;
            try
            {
                o = JObject.Parse(text ?? "");
            }
            catch (JsonReaderException e)
            {
                throw new SaveException($"That isn't a snapshot: {e.Message}");
            }
            if ((int?)o["format"] != Format) throw new SaveException($"This snapshot doesn't fit: format {o["format"]}, not {Format}");
            if (!(o["checksum"] is JArray sum) || sum.Count != PartNames.Count || !(o["world"] is JValue world) || o["tick"] == null)
                throw new SaveException("That isn't a snapshot: its tick, checksum or world is missing");
            var mods = (o["mods"] as JArray ?? new JArray()).Select(m => new ActiveMod((string)m[0], (string)m[1])).ToArray();
            return new JoinSnapshot((int)o["tick"], (string)world, mods, sum.Select(p => (string)p).ToArray());
        }

        /// <summary>
        /// The world the joiner plays on, and the rules to run its batches with (TickBatch.RunOn): the save loaded over the
        /// joiner's own map (<paramref name="source"/>), base rules and land, with the snapshot's mods. Refused
        /// (SaveException), saying why, if those aren't the host's, or if the world loaded isn't the host's checksum.
        /// </summary>
        public (World world, RuleSet rules) Join(Rules baseRules, IChunkSource source, int[] land = null, ModBudgets budgets = null)
        {
            if (baseRules == null) throw new ArgumentNullException(nameof(baseRules));
            RuleSet rules;
            try
            {
                rules = Mods.Count == 0 ? RuleSet.Plain(baseRules) : RuleSet.Plain(baseRules).WithMods(Mods);
            }
            catch (ModException e)
            {
                throw new SaveException($"This snapshot doesn't fit: its mods don't go on these rules ({e.Message})");
            }
            // A room's save composes its own mods over the base rules; any other world's is saved under the mods in force.
            var world = World.Load(Save, IsRoom ? baseRules : rules.Effective, source, budgets, land);
            var now = ChecksumOf(world);
            for (int p = 0; p < now.Length; p++)
                if (now[p] != Checksum[p])
                    throw new SaveException($"This snapshot doesn't come back to the host's world at tick {Tick}: its {PartNames[p]} differ (host {Checksum[p]}, here {now[p]})");
            return (world, rules);
        }

        bool IsRoom => JObject.Parse(Save)["room"] != null;
    }
}
