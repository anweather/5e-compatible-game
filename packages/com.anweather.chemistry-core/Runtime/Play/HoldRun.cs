// The Hold's run (docs/design/the-hold.md, phase 2): raiders come from a map's spawners in endless waves, go for the keep's
// heart, batter what's in their way and hurt wizards in reach; the run ends when the keep falls, and the score is the wave
// reached. A wizard standing on the plate starts a run; every peer sees that stand at the same tick (a Stand command).
// Raiders are the defense field's, so spells reach them as they do there; their way is worked out here, not in
// Play/DefenseSystem.cs, since a castle's walls can be battered and a field's can't. A wizard on a blocking cell is on the walls:
// no raider reaches it. All whole numbers and the seeded generator, and "" in the checksum until there's been a run.
// This file is a run's data; HoldSystem.cs is the Hold's system, which the world owns.
using System;
using System.Collections.Generic;

namespace Chem
{
    public enum HoldPhase
    {
        Running,
        Lost,
        /// <summary>The last wave beaten (HoldParams.Waves).</summary>
        Won,
    }

    /// <summary>A run: how it stands.</summary>
    public sealed class HoldState
    {
        public HoldPhase Phase;
        /// <summary>The wave under way (the score, once it's lost), the keep's health, raiders of this wave still to come, the tick the next comes on, and how many have come in all.</summary>
        public int Wave, Health, ToSpawn, NextSpawn, Spawned;
        /// <summary>The wizards at the run's start, which scale its waves; raiders of this wave out so far.</summary>
        public int Peers = 1, InWave;
        /// <summary>With timed waves (HoldParams.WaveTicks), the tick the next wave starts on, whoever is still standing; 0 without.</summary>
        public int NextWave;
        /// <summary>This wave's theme, or null.</summary>
        public string Theme;
        /// <summary>The moment this run came from, or null; and the tick it was gone into.</summary>
        public string Moment;
        /// <summary>Each wizard's offer and picks, by name.</summary>
        public SortedDictionary<string, RunWizard> Wizards { get; } = new SortedDictionary<string, RunWizard>(StringComparer.Ordinal);
        public int MomentTick;
    }
}
