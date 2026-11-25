// The open world's plan (D20, docs/design/core-package/open-world.md): where the characters are, and so which chunks are
// filled and which are active. The host game answers two things through its port (IOpenGround): where each character is
// (its layer and cell) and what the ground is in a chunk never filled. Both are asked on the host (and in solo) only; the
// planner turns them into commands stamped on a tick (fills on the "fills" channel, switches on "chunks"), so every peer
// fills and switches the same chunks on the same tick and no peer but the host ever samples.
//
// Each tick it plans for (LockstepSession.Stamping):
// - every chunk within a character's reach, plus one ring, is filled (the ring so an active chunk's neighbours exist): the
//   nearest first, at most FillsPerTick a tick, so a teleport into new ground fills over a few ticks;
// - a filled chunk within a character's reach whose ring is filled too activates;
// - an active chunk further than the keep distance from every character of its layer deactivates (and packs, D17).
// Reach and keep are sim.json's streaming, as for a bounded world's ChunkStreaming, but by layer: a character in a cave
// keeps the cave's chunks active, not the meadow's above it.
//
// The host's world runs a tick or so behind what it has stamped, so the planner keeps what it stamped until its tick
// has run, and plans against the world as it will be.
using System;
using System.Collections.Generic;
using System.Linq;
using Chem.Lockstep;

namespace Chem
{
    /// <summary>
    /// The host game's half of an open world (the port): where the characters are and what the ground is. Asked on the
    /// host and in solo only, never on a client.
    /// </summary>
    public interface IOpenGround
    {
        /// <summary>Where each character is: who (for the host game's own use), its layer and its cell in that layer.</summary>
        IEnumerable<(string who, int layer, int x, int y)> Characters();

        /// <summary>The ground of a chunk never filled before; the planner stamps it as a fill.</summary>
        ChunkGround Sample(ChunkKey key);
    }

    /// <summary>One tick's plan for an open world: the chunks to fill, then the switches, each in key order.</summary>
    public sealed class OpenPlan
    {
        public List<ChunkFill> Fills { get; } = new List<ChunkFill>();
        public List<ChunkSwitch> Switches { get; } = new List<ChunkSwitch>();
    }

    /// <summary>Plans an open world's fills and switches from its port (IOpenGround), tick by tick, on the host.</summary>
    public sealed class OpenWorldPlanner
    {
        readonly IOpenGround ground;
        // What's been stamped and not yet run, by the tick it lands on: a fill, a switch (on or off).
        readonly Dictionary<ChunkKey, int> fillsComing = new Dictionary<ChunkKey, int>();
        readonly Dictionary<ChunkKey, (bool active, int tick)> switchesComing = new Dictionary<ChunkKey, (bool, int)>();

        public OpenWorldPlanner(IOpenGround ground)
        {
            this.ground = ground ?? throw new ArgumentNullException(nameof(ground));
        }

        /// <summary>The most chunks filled a tick (the nearest first): each is a sample on the host and ~1-4 KB on the wire.</summary>
        public int FillsPerTick { get; set; } = 16;

        /// <summary>
        /// The host's plan as a session's (LockstepSession.Stamping): at each tick it stamps, the fills on
        /// <paramref name="fills"/> and the switches on <paramref name="chunks"/>, landing on that tick. Every peer registers
        /// both channels; only the host plans.
        /// </summary>
        public static OpenWorldPlanner Attach(LockstepSession session, IOpenGround ground, LockstepChannel<ChunkFill> fills, LockstepChannel<ChunkSwitch> chunks)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (fills == null) throw new ArgumentNullException(nameof(fills));
            if (chunks == null) throw new ArgumentNullException(nameof(chunks));
            var planner = new OpenWorldPlanner(ground);
            session.Stamping += tick =>
            {
                var plan = planner.Plan(session.World.World, session.NetTick, tick);
                foreach (var f in plan.Fills) session.Stamp(fills, f);
                foreach (var s in plan.Switches) session.Stamp(chunks, s);
            };
            return planner;
        }

        /// <summary>
        /// The plan for <paramref name="tick"/>, against an open world that has run to <paramref name="ran"/> (what was
        /// stamped for later ticks counts as done). The plan is kept as coming until its tick has run.
        /// </summary>
        public OpenPlan Plan(World world, int ran, int tick)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!world.IsOpen) throw new ArgumentException("the open world's plan is for an open world (World.OpenWorld)", nameof(world));
            Forget(ran);
            var plan = new OpenPlan();
            var characters = ground.Characters().Select(c => ChunkKey.OfCell(c.layer, c.x, c.y)).Distinct().ToList();
            if (characters.Count == 0) return plan;
            var s = world.Rules.Sim.Streaming;
            int reach = Math.Max(0, s.Reach), keep = Math.Max(reach, s.Keep);

            // The chunks to fill: within reach and a ring of a character, not filled or coming, nearest first.
            var wanted = Around(characters, reach + 1);
            foreach (var (key, _) in wanted.Where(w => !Filled(world, w.Key)).OrderBy(w => w.Value).ThenBy(w => w.Key).Take(Math.Max(0, FillsPerTick)))
            {
                var g = ground.Sample(key) ?? throw new InvalidOperationException($"the host game gave no ground for chunk {key}");
                plan.Fills.Add(new ChunkFill(key, g));
                fillsComing[key] = tick;
            }

            // Switches: on within reach once the chunk and its ring are filled; off past the keep distance.
            var switches = new SortedDictionary<ChunkKey, bool>();
            foreach (var w in wanted)
                if (w.Value <= reach && !Active(world, w.Key) && Filled(world, w.Key) && RingFilled(world, w.Key)) switches[w.Key] = true;
            foreach (var key in ActiveNow(world))
                if (Nearest(characters, key) > keep) switches[key] = false;
            foreach (var sw in switches)
            {
                var (cx, cy) = sw.Key.Chunk;
                plan.Switches.Add(new ChunkSwitch(cx, cy, sw.Value));
                switchesComing[sw.Key] = (sw.Value, tick);
            }
            return plan;
        }

        // What's been stamped for ticks that have run is in the world now.
        void Forget(int ran)
        {
            foreach (var k in fillsComing.Where(f => f.Value <= ran).Select(f => f.Key).ToList()) fillsComing.Remove(k);
            foreach (var k in switchesComing.Where(f => f.Value.tick <= ran).Select(f => f.Key).ToList()) switchesComing.Remove(k);
        }

        bool Filled(World world, ChunkKey key) => fillsComing.ContainsKey(key) || world.IsFilled(key);

        bool Active(World world, ChunkKey key) => switchesComing.TryGetValue(key, out var s) ? s.active : world.IsActive(key);

        bool RingFilled(World world, ChunkKey key)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var n = new ChunkKey(key.Layer, key.X + dx, key.Y + dy);
                    if (n.Fits && !Filled(world, n)) return false;
                }
            return true;
        }

        // The chunks active once what's coming has landed.
        IEnumerable<ChunkKey> ActiveNow(World world)
        {
            var now = new SortedSet<ChunkKey>(world.ActiveKeys);
            foreach (var s in switchesComing)
                if (s.Value.active) now.Add(s.Key);
                else now.Remove(s.Key);
            return now;
        }

        // Every chunk within distance chunks of a character (one a world holds), with how far the nearest character is.
        static Dictionary<ChunkKey, int> Around(List<ChunkKey> characters, int distance)
        {
            var around = new Dictionary<ChunkKey, int>();
            foreach (var c in characters)
                for (int dy = -distance; dy <= distance; dy++)
                    for (int dx = -distance; dx <= distance; dx++)
                    {
                        var key = new ChunkKey(c.Layer, c.X + dx, c.Y + dy);
                        if (!key.Fits) continue;
                        int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        if (!around.TryGetValue(key, out int was) || d < was) around[key] = d;
                    }
            return around;
        }

        static int Nearest(List<ChunkKey> characters, ChunkKey key)
        {
            int near = int.MaxValue;
            foreach (var c in characters) near = Math.Min(near, c.DistanceTo(key));
            return near;
        }
    }
}
