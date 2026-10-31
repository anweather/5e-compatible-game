// Machines and the gates they power (machines.json). Port of tickMachines,
// setGates and setLinkedGates in sandbox/src/sim.ts. A gate linked to a
// machine (LinkGate: a puzzle's gate to its own coil) opens only for that
// machine, and that machine opens no other; the gates no one linked open for
// any machine no one linked.
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

        /// <summary>A gate that opens only for one machine (GateLink in the TS): both by their cell's index (Idx), and whether it has it open.</summary>
        public sealed class GateLink
        {
            public int Gate, Machine;
            public bool Open;
        }

        // Each machine's state by its cell's index (Idx), made the first tick it's seen.
        readonly Dictionary<int, MachineState> machines = new Dictionary<int, MachineState>();

        // The linked gates in the order they were linked, and the cells of their gates and machines.
        readonly List<GateLink> gateLinks = new List<GateLink>();
        readonly HashSet<int> linkedGates = new HashSet<int>(), linkedMachines = new HashSet<int>();

        /// <summary>Whether the machines no one linked have the gates no one linked open (gatesOpen in the TS).</summary>
        public bool GatesOpen { get; private set; }

        /// <summary>The machine that last opened a gate, or null.</summary>
        public string OpenedBy { get; private set; }

        /// <summary>The linked gates, in the order they were linked.</summary>
        public IReadOnlyList<GateLink> GateLinks => gateLinks;

        /// <summary>The state of the machine at (x, y), or null if it hasn't run.</summary>
        public MachineState MachineAt(int x, int y) => machines.TryGetValue(Idx(x, y), out var m) ? m : null;

        /// <summary>
        /// Links the gate at (gx, gy) to the machine at (mx, my) (linkGate in the TS): from then on that gate opens
        /// only while that machine is powered, and that machine opens no other gate. A gate has one machine; linking
        /// it again moves its link. Every peer links the same gates before the first tick (WorldPuzzles.LayInto).
        /// </summary>
        public void LinkGate(int gx, int gy, int mx, int my)
        {
            if (!InBounds(gx, gy) || !InBounds(mx, my)) return;
            int gate = Idx(gx, gy);
            gateLinks.RemoveAll(l => l.Gate == gate);
            gateLinks.Add(new GateLink { Gate = gate, Machine = Idx(mx, my), Open = At(gx, gy).T == "gate_open" });
            RelinkSets();
        }

        void RelinkSets()
        {
            linkedGates.Clear();
            linkedMachines.Clear();
            foreach (var l in gateLinks)
            {
                linkedGates.Add(l.Gate);
                linkedMachines.Add(l.Machine);
            }
        }

        /// <summary>
        /// Every machine on the active cells, in row order (tickMachines in the TS): a boiler builds pressure from steam
        /// and loses it slowly; a plate is pressed by a raider heavy enough; a wheel, a coil and a windmill hold for a
        /// while after water flows, a charge arrives or the wind blows into them. A linked gate opens while its own
        /// machine is powered; any other machine powered opens the gates no one linked.
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
                    if (m.Powered && poweredBy == null && !linkedMachines.Contains(Idx(x, y))) poweredBy = def.Name;
                }
            }
            SetGates(rows, poweredBy);
            SetLinkedGates();
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

        // Opens every gate no one linked when a machine no one linked is powered, shuts them when none is (not one
        // a raider is wedged in), and says so.
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
                    if (open && c.T == "gate" && !Linked(s.Chunk, i))
                    {
                        c.T = "gate_open";
                        changed = true;
                    }
                    else if (!open && c.T == "gate_open" && !Linked(s.Chunk, i) && RaiderAt(CellX(s.Chunk, i), CellY(s.Chunk, i)) == null)
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

        bool Linked(Chunk ch, int i) => linkedGates.Count > 0 && linkedGates.Contains(Idx(CellX(ch, i), CellY(ch, i)));

        // Each linked gate on an active cell follows its own machine, in the order they were linked: open while it's
        // powered, shut once it isn't (not while a raider is wedged in it: then it tries again next tick), and says so.
        void SetLinkedGates()
        {
            foreach (var l in gateLinks)
            {
                bool powered = machines.TryGetValue(l.Machine, out var m) && m.Powered;
                int x = l.Gate % W, y = l.Gate / W;
                if (powered == l.Open || !IsActiveAt(x, y)) continue;
                var c = At(x, y);
                if (!powered && c.T == "gate_open" && RaiderAt(x, y) != null) continue;
                l.Open = powered;
                if (powered && c.T == "gate")
                {
                    c.T = "gate_open";
                    OpenedBy = MachineName(l.Machine);
                    AddLog($"The gate grinds open, powered by the {OpenedBy.ToLowerInvariant()}", LogKind.Power);
                }
                else if (!powered && c.T == "gate_open")
                {
                    c.T = "gate";
                    AddLog($"The {MachineName(l.Machine).ToLowerInvariant()} falls quiet and its gate slams shut", LogKind.Power);
                }
            }
        }

        string MachineName(int idx) => Rules.Machines.TryGetValue(At(idx % W, idx / W).T, out var def) ? def.Name : "machine";
    }
}
