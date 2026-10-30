// Machines and the gates they power (machines.json). Port of tickMachines and
// setGates in sandbox/src/sim.ts.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>A machine's state (MachineState in the TS): its pressure or hold, and whether it powers the gates.</summary>
        public sealed class MachineState
        {
            public int Level, Hold;
            public bool Powered;
        }

        // Each machine's state by its cell's index (Idx), made the first tick it's seen.
        readonly Dictionary<int, MachineState> machines = new Dictionary<int, MachineState>();

        /// <summary>Whether the machines have the gates open (gatesOpen in the TS).</summary>
        public bool GatesOpen { get; private set; }

        /// <summary>The machine that last opened the gates, or null.</summary>
        public string OpenedBy { get; private set; }

        /// <summary>The state of the machine at (x, y), or null if it hasn't run.</summary>
        public MachineState MachineAt(int x, int y) => machines.TryGetValue(Idx(x, y), out var m) ? m : null;

        /// <summary>
        /// Every machine on the active cells, in row order (tickMachines in the TS): a boiler builds pressure from steam
        /// and loses it slowly; a plate is pressed by a raider heavy enough; a wheel, a coil and a windmill hold for a
        /// while after water flows, a charge arrives or the wind blows into them. Any one powered opens the gates.
        /// </summary>
        void TickMachines(RowSpan[] rows)
        {
            if (Rules.Machines.Count == 0) return;
            string poweredBy = null;
            foreach (var s in rows)
            {
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++)
                {
                    if (!Rules.Machines.TryGetValue(cells[i].T, out var def)) continue;
                    int x = CellX(s.Chunk, i), y = CellY(s.Chunk, i);
                    if (!machines.TryGetValue(Idx(x, y), out var m)) machines[Idx(x, y)] = m = new MachineState();
                    RunMachine(def, m, s.Chunk.Marks[i], cells[i], x, y);
                    if (m.Powered && poweredBy == null) poweredBy = def.Name;
                }
            }
            SetGates(rows, poweredBy);
        }

        void RunMachine(MachineDef def, MachineState m, byte marks, Cell c, int x, int y)
        {
            switch (def.Needs)
            {
                case "steam":
                    if ((marks & SteamMark) != 0) m.Level = Math.Min(def.Max, m.Level + 1);
                    else if (Tick % def.DecayEvery == 0) m.Level = Math.Max(0, m.Level - 1);
                    m.Powered = m.Powered ? m.Level > 0 : m.Level >= def.Build;
                    break;
                case "mass":
                    var r = RaiderAt(x, y);
                    m.Level = r != null ? Rules.Entity(r.Kind).Mass : 0;
                    m.Powered = m.Level >= def.Mass;
                    break;
                default:
                    bool hit = Hit(def.Needs, marks, c);
                    m.Hold = hit ? def.Hold : Math.Max(0, m.Hold - 1);
                    m.Level = m.Hold;
                    m.Powered = m.Hold > 0;
                    break;
            }
        }

        // Whether a wheel, a windmill or a coil was reached this tick: water flowed, wind blew, a charge arrived.
        static bool Hit(string needs, byte marks, Cell c)
        {
            if (needs == "flow") return (marks & FlowMark) != 0;
            if (needs == "wind") return (marks & WindMark) != 0;
            return c.Charge > 0;
        }

        // Opens every gate when a machine is powered, shuts them when none is (not one a raider is wedged in), and says so.
        void SetGates(RowSpan[] rows, string poweredBy)
        {
            bool open = poweredBy != null;
            if (open == GatesOpen) return;
            bool changed = false;
            foreach (var s in rows)
            {
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++)
                {
                    var c = cells[i];
                    if (open && c.T == "gate")
                    {
                        c.T = "gate_open";
                        changed = true;
                    }
                    else if (!open && c.T == "gate_open" && RaiderAt(CellX(s.Chunk, i), CellY(s.Chunk, i)) == null)
                    {
                        c.T = "gate";
                        changed = true;
                    }
                }
            }
            GatesOpen = open;
            if (!changed) return;
            if (open)
            {
                OpenedBy = poweredBy;
                AddLog($"The gate grinds open, powered by the {poweredBy.ToLowerInvariant()}", LogKind.Power);
            }
            else AddLog("The machines fall quiet and the gate slams shut", LogKind.Power);
        }
    }
}
