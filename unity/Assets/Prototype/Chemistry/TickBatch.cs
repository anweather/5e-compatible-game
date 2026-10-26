// One sim tick's inputs, in lockstep: chunks waking or sleeping (ChunkStreaming), a rule switch (mods on or off), the
// casts stamped for that tick, and the effects physics props had on cells
// (Effects.cs: a barrel's blast, a lantern's spill), which the host saw and
// stamped like casts. Solo play runs one per tick; online the host
// stamps them and every machine runs the same batches in the same order
// (NetChem), and a late joiner rebuilds them from the host's history
// (Timeline) and replays from tick 0. Running a batch is the same code in all
// three cases, so a replayed world matches the live one cell for cell.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    /// <summary>A spell cast: what NetChem's CastIntent carries, minus who cast it.</summary>
    public readonly struct CastCommand
    {
        public CastCommand(string spell, int x, int y, int dir, int charge = 1)
        {
            Spell = spell;
            X = x;
            Y = y;
            Dir = dir;
            Charge = charge;
        }

        public string Spell { get; }
        public int X { get; }
        public int Y { get; }
        public int Dir { get; }
        /// <summary>How long it was held: 1 (a tap) to World.MaxCharge.</summary>
        public int Charge { get; }
    }

    /// <summary>A rule switch stamped with the tick it takes effect on: the full mod list from then on.</summary>
    public readonly struct RuleChange
    {
        public RuleChange(int tick, IReadOnlyList<ActiveMod> mods)
        {
            Tick = tick;
            Mods = mods ?? Array.Empty<ActiveMod>();
        }

        public int Tick { get; }
        public IReadOnlyList<ActiveMod> Mods { get; }
    }

    public sealed class TickBatch
    {
        public TickBatch(int tick, IEnumerable<CastCommand> casts = null, IReadOnlyList<ActiveMod> mods = null, IEnumerable<EffectCommand> effects = null,
            IEnumerable<ChunkSwitch> chunks = null)
        {
            Tick = tick;
            Casts = (casts ?? Enumerable.Empty<CastCommand>()).ToList();
            Mods = mods;
            Effects = (effects ?? Enumerable.Empty<EffectCommand>()).ToList();
            Chunks = (chunks ?? Enumerable.Empty<ChunkSwitch>()).ToList();
        }

        /// <summary>The tick this batch brings the world to: it runs while the world is at Tick - 1.</summary>
        public int Tick { get; }
        public List<CastCommand> Casts { get; }
        /// <summary>The mod list from this tick on, or null when the rules don't change.</summary>
        public IReadOnlyList<ActiveMod> Mods { get; set; }
        /// <summary>What physics props did to cells, applied after the casts, in order.</summary>
        public List<EffectCommand> Effects { get; }
        /// <summary>Chunks waking or going to sleep (ChunkStreaming), applied first.</summary>
        public List<ChunkSwitch> Chunks { get; }

        /// <summary>
        /// Runs the batch: wakes or pauses chunks and switches the rules if it says to, applies its casts and then its props'
        /// effects, then steps the world.
        /// Returns the rules now in force. A rule switch that doesn't check out (it can't on a
        /// machine whose own files differ) keeps the old rules, reports why through
        /// <paramref name="errors"/>, and the checksum will then flag that machine.
        /// </summary>
        public RuleSet RunOn(World world, RuleSet rules, out IReadOnlyList<string> errors)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            errors = Array.Empty<string>();
            ChunkStreaming.Apply(world, Chunks);
            if (Mods != null)
            {
                try
                {
                    rules = rules.WithMods(Mods);
                    world.ApplyRules(rules.Effective);
                }
                catch (ModException e)
                {
                    errors = e.Errors;
                }
            }
            foreach (var c in Casts) world.Cast(c.Spell, c.X, c.Y, c.Dir, c.Charge);
            foreach (var e in Effects) world.ApplyEffect(e);
            world.Step();
            return rules;
        }

        /// <summary>
        /// Every tick's batch from 1 to <paramref name="upToTick"/>, from a recorded history of casts,
        /// rule switches and props' effects (what a late joiner gets). Ticks with nothing on them get
        /// an empty batch.
        /// </summary>
        public static Dictionary<int, TickBatch> Timeline(IEnumerable<(int tick, CastCommand cast)> casts, IEnumerable<RuleChange> changes, int upToTick,
            IEnumerable<(int tick, EffectCommand effect)> effects = null, IEnumerable<(int tick, ChunkSwitch chunk)> chunks = null)
        {
            var batches = new Dictionary<int, TickBatch>();
            for (int t = 1; t <= upToTick; t++) batches[t] = new TickBatch(t);
            foreach (var (tick, cast) in casts ?? Enumerable.Empty<(int, CastCommand)>())
                if (batches.TryGetValue(tick, out var b)) b.Casts.Add(cast);
            foreach (var change in changes ?? Enumerable.Empty<RuleChange>())
                if (batches.TryGetValue(change.Tick, out var b)) b.Mods = change.Mods;
            foreach (var (tick, effect) in effects ?? Enumerable.Empty<(int, EffectCommand)>())
                if (batches.TryGetValue(tick, out var b)) b.Effects.Add(effect);
            foreach (var (tick, chunk) in chunks ?? Enumerable.Empty<(int, ChunkSwitch)>())
                if (batches.TryGetValue(tick, out var b)) b.Chunks.Add(chunk);
            return batches;
        }
    }
}
