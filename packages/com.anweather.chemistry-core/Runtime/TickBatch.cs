// One sim tick's inputs, in lockstep: chunks waking or sleeping (ChunkStreaming), a rule switch (mods on or off), the
// casts stamped for that tick, and the effects physics props had on cells
// (Effects.cs: a barrel's blast, a lantern's spill), which the host saw and
// stamped like casts, defense commands (the defense field's start,
// Defense.cs), players' combo input (Combos.cs), the dragon's summons (Play/DragonSystem.cs), room commands (World.Room.cs) and unarmed strikes (World.Strike.cs). Solo play runs one per tick; online the host
// stamps them and every machine runs the same batches in the same order
// (NetChem), and a late joiner rebuilds them from the host's history
// (Timeline) and replays from tick 0. Running a batch is the same code in all
// three cases, so a replayed world matches the live one cell for cell.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public readonly struct CastCommand
    {
#pragma warning disable S107
        public CastCommand(string spell, int x, int y, int dir, int charge = 1, int aimX = 0, int aimY = 0, string by = null)
#pragma warning restore S107
        {
            By = by;
            Spell = spell;
            X = x;
            Y = y;
            Dir = dir;
            Charge = charge;
            AimX = aimX;
            AimY = aimY;
        }

        public string Spell { get; }
        public string By { get; }
        public int X { get; }
        public int Y { get; }
        public int Dir { get; }
        public int Charge { get; }
        public int AimX { get; }
        public int AimY { get; }
    }

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

        public int Tick { get; }
        public List<CastCommand> Casts { get; }
        public IReadOnlyList<ActiveMod> Mods { get; set; }
        public List<EffectCommand> Effects { get; }
        public List<ChunkSwitch> Chunks { get; }
        public List<ChunkFill> Fills { get; } = new List<ChunkFill>();
        public List<DefenseCommand> Defense { get; } = new List<DefenseCommand>();
        public List<ComboCommand> Combos { get; } = new List<ComboCommand>();
        public List<DragonCommand> Dragon { get; } = new List<DragonCommand>();
        public List<CourseCommand> Course { get; } = new List<CourseCommand>();
        public List<StrikeCommand> Strikes { get; } = new List<StrikeCommand>();
        public List<RoomCommand> Room { get; } = new List<RoomCommand>();
        public bool Reset { get; set; }

        public RuleSet RunOn(World world, RuleSet rules, out IReadOnlyList<string> errors)
        {
            var now = Prepare(world, rules, out errors);
            world.Step();
            return now;
        }

        public RuleSet Prepare(World world, RuleSet rules, out IReadOnlyList<string> errors)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            world.FinishTick();
            errors = Array.Empty<string>();
            foreach (var f in Fills) world.FillChunk(f.Key, f.Ground);
            ChunkStreaming.Apply(world, Chunks);
            if (Mods != null && world.Room == null)
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
            foreach (var r in Room) world.ApplyRoom(r);
            if (Room.Count > 0) rules = world.RoomRules;
            foreach (var c in Casts) world.Cast(c);
            foreach (var e in Effects) world.ApplyEffect(e);
            foreach (var d in Defense) world.DefenseSystem.Apply(d);
            foreach (var c in Combos) world.ApplyCombo(c);
            foreach (var d in Dragon) world.DragonSystem.Apply(d);
            foreach (var c in Course) world.CourseSystem.Apply(c);
            foreach (var s in Strikes) world.ApplyStrike(s);
            return rules;
        }

        public static Dictionary<int, TickBatch> Timeline(IEnumerable<(int tick, CastCommand cast)> casts, IEnumerable<RuleChange> changes, int upToTick,
            IEnumerable<(int tick, EffectCommand effect)> effects = null, IEnumerable<(int tick, ChunkSwitch chunk)> chunks = null,
            IEnumerable<(int tick, DefenseCommand command)> defense = null, int from = 1)
        {
            var batches = new Dictionary<int, TickBatch>();
            for (int t = from; t <= upToTick; t++) batches[t] = new TickBatch(t);
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

        public static void AddCombos(Dictionary<int, TickBatch> batches, IEnumerable<(int tick, ComboCommand command)> combos)
        {
            foreach (var (tick, command) in combos)
                if (batches.TryGetValue(tick, out var b)) b.Combos.Add(command);
        }

        public static void AddDragon(Dictionary<int, TickBatch> batches, IEnumerable<int> ticks)
        {
            foreach (int tick in ticks)
                if (batches.TryGetValue(tick, out var b)) b.Dragon.Add(new DragonCommand());
        }

        public static void AddRoom(Dictionary<int, TickBatch> batches, IEnumerable<(int tick, RoomCommand command)> commands)
        {
            foreach (var (tick, command) in commands)
                if (batches.TryGetValue(tick, out var b)) b.Room.Add(command);
        }

        public static void AddStrikes(Dictionary<int, TickBatch> batches, IEnumerable<(int tick, StrikeCommand command)> commands)
        {
            foreach (var (tick, command) in commands)
                if (batches.TryGetValue(tick, out var b)) b.Strikes.Add(command);
        }

        public static void AddCourse(Dictionary<int, TickBatch> batches, IEnumerable<(int tick, CourseCommand command)> steps)
        {
            foreach (var (tick, command) in steps)
                if (batches.TryGetValue(tick, out var b)) b.Course.Add(command);
        }
    }
}
