// One sim tick's inputs, in lockstep: chunks waking or sleeping (ChunkStreaming), a rule switch (mods on or off), the
// casts stamped for that tick, and the effects physics props had on cells
// (Effects.cs: a barrel's blast, a lantern's spill), which the host saw and
// stamped like casts, defense commands (the defense field's start,
// Defense.cs) and players' combo input (Combos.cs). Solo play runs one per tick; online the host
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
        public CastCommand(string spell, int x, int y, int dir, int charge = 1, int aimX = 0, int aimY = 0)
        {
            Spell = spell;
            X = x;
            Y = y;
            Dir = dir;
            Charge = charge;
            AimX = aimX;
            AimY = aimY;
        }

        public string Spell { get; }
        public int X { get; }
        public int Y { get; }
        public int Dir { get; }
        /// <summary>How long it was held: 1 (a tap) to World.MaxCharge.</summary>
        public int Charge { get; }
        /// <summary>The aim off the grid's four ways, or (0, 0) for Dir's (World.SpellCells).</summary>
        public int AimX { get; }
        public int AimY { get; }
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
        /// <summary>Defense commands (the start pedestal: World.Defense.cs), applied after the effects, in order.</summary>
        public List<DefenseCommand> Defense { get; } = new List<DefenseCommand>();
        /// <summary>Players' combo input (World.Combos.cs), applied after the defense, in order.</summary>
        public List<ComboCommand> Combos { get; } = new List<ComboCommand>();
        /// <summary>Where players stand on the course (World.Course.cs), applied last, in order.</summary>
        public List<CourseCommand> Course { get; } = new List<CourseCommand>();
        /// <summary>
        /// The world is rebuilt before this tick (the online reset, J-014): the view rebuilds it from
        /// its seed (NetChem, ChemGrid.ResetWorld), then the batch runs on the fresh world. The
        /// reset batch re-stamps the mods in force, so the rules come through a reset.
        /// </summary>
        public bool Reset { get; set; }

        /// <summary>
        /// Runs the batch: wakes or pauses chunks and switches the rules if it says to, applies its casts and then its props'
        /// effects, then steps the world.
        /// Returns the rules now in force. A rule switch that doesn't check out (it can't on a
        /// machine whose own files differ) keeps the old rules, reports why through
        /// <paramref name="errors"/>, and the checksum will then flag that machine.
        /// </summary>
        public RuleSet RunOn(World world, RuleSet rules, out IReadOnlyList<string> errors)
        {
            var now = Prepare(world, rules, out errors);
            world.Step();
            return now;
        }

        /// <summary>
        /// RunOn without the step: the batch's switches, rules, casts, effects, defense and combo commands,
        /// for a caller that then steps the tick in slices (World.StepSome). A tick still part done
        /// is finished first, so the commands land between whole ticks.
        /// </summary>
        public RuleSet Prepare(World world, RuleSet rules, out IReadOnlyList<string> errors)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            world.FinishTick(); // a batch's commands land between whole ticks
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
            foreach (var c in Casts) world.Cast(c.Spell, c.X, c.Y, c.Dir, c.Charge, c.AimX, c.AimY);
            foreach (var e in Effects) world.ApplyEffect(e);
            foreach (var d in Defense) world.ApplyDefense(d);
            foreach (var c in Combos) world.ApplyCombo(c);
            foreach (var c in Course) world.ApplyCourse(c);
            return rules;
        }

        /// <summary>
        /// Every tick's batch from <paramref name="from"/> (1, or the last reset's tick, whose batch is
        /// then a reset) to <paramref name="upToTick"/>, from a recorded history of casts,
        /// rule switches and props' effects (what a late joiner gets). Ticks with nothing on them get
        /// an empty batch.
        /// </summary>
        public static Dictionary<int, TickBatch> Timeline(IEnumerable<(int tick, CastCommand cast)> casts, IEnumerable<RuleChange> changes, int upToTick,
            IEnumerable<(int tick, EffectCommand effect)> effects = null, IEnumerable<(int tick, ChunkSwitch chunk)> chunks = null,
            IEnumerable<(int tick, DefenseCommand command)> defense = null, int from = 1)
        {
            var batches = new Dictionary<int, TickBatch>();
            for (int t = from; t <= upToTick; t++) batches[t] = new TickBatch(t);
            // A history that starts at a reset (the host keeps none from before it) begins with it.
            if (from > 1 && batches.TryGetValue(from, out var first)) first.Reset = true;
            foreach (var (tick, cast) in casts ?? Enumerable.Empty<(int, CastCommand)>())
                if (batches.TryGetValue(tick, out var b)) b.Casts.Add(cast);
            foreach (var change in changes ?? Enumerable.Empty<RuleChange>())
                if (batches.TryGetValue(change.Tick, out var b)) b.Mods = change.Mods;
            foreach (var (tick, effect) in effects ?? Enumerable.Empty<(int, EffectCommand)>())
                if (batches.TryGetValue(tick, out var b)) b.Effects.Add(effect);
            foreach (var (tick, chunk) in chunks ?? Enumerable.Empty<(int, ChunkSwitch)>())
                if (batches.TryGetValue(tick, out var b)) b.Chunks.Add(chunk);
            foreach (var (tick, command) in defense ?? Enumerable.Empty<(int, DefenseCommand)>())
                if (batches.TryGetValue(tick, out var b)) b.Defense.Add(command);
            return batches;
        }

        /// <summary>Adds a recorded history of combo input to a <see cref="Timeline"/>'s batches.</summary>
        public static void AddCombos(Dictionary<int, TickBatch> batches, IEnumerable<(int tick, ComboCommand command)> combos)
        {
            foreach (var (tick, command) in combos)
                if (batches.TryGetValue(tick, out var b)) b.Combos.Add(command);
        }

        /// <summary>Adds a recorded history of players' steps on the course to a <see cref="Timeline"/>'s batches.</summary>
        public static void AddCourse(Dictionary<int, TickBatch> batches, IEnumerable<(int tick, CourseCommand command)> steps)
        {
            foreach (var (tick, command) in steps)
                if (batches.TryGetValue(tick, out var b)) b.Course.Add(command);
        }
    }
}
