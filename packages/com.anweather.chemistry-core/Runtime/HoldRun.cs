// The Hold's run (docs/design/the-hold.md, phase 2): raiders come from a map's spawners in endless waves, go for the keep's
// heart, batter what's in their way and hurt wizards in reach; the run ends when the keep falls, and the score is the wave
// reached. A wizard standing on the plate starts a run; every peer sees that stand at the same tick (a Stand command).
// Raiders are the defense field's, so spells reach them as they do there; their way is worked out here, not in
// Play/DefenseSystem.cs, since a castle's walls can be battered and a field's can't. A wizard on a blocking cell is on the walls:
// no raider reaches it. All whole numbers and the seeded generator, and "" in the checksum until there's been a run.
// This file is a run's data, which the world keeps (World.Hold); Play/HoldSystem.cs is the Hold's system (Chem.Play).
using System;
using System.Collections.Generic;

namespace Chem
{
    public enum HoldPhase
    {
        Running,
        Lost,
        Won,
    }

    public sealed class HoldState
    {
        public HoldPhase Phase;
        public int Wave, Health, ToSpawn, NextSpawn, Spawned;
        public int Peers = 1, InWave;
        public int NextWave;
        public string Theme;
        public string Moment;
        public SortedDictionary<string, RunWizard> Wizards { get; } = new SortedDictionary<string, RunWizard>(StringComparer.Ordinal);
        public int MomentTick;
    }
}
