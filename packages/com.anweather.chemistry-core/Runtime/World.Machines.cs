// Machines and the gates they power (machines.json). Port of tickMachines,
// setGates and setLinkedGates in sandbox/src/sim.ts. A gate linked to a
// machine (LinkGate: a puzzle's gate to its own coil) opens only for that
// machine, and that machine opens no other; the gates no one linked open for
// any machine no one linked.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public sealed partial class World
    {
        public sealed class MachineState
        {
            public int Level, Hold;
            public bool Powered;
        }

        public sealed class GateLink
        {
            public int Gate, Machine;
            public bool Open;
        }

        readonly Dictionary<int, MachineState> machines = new Dictionary<int, MachineState>();

        readonly List<GateLink> gateLinks = new List<GateLink>();
        readonly HashSet<int> linkedGates = new HashSet<int>(), linkedMachines = new HashSet<int>();

        public bool GatesOpen { get; private set; }

        public string OpenedBy { get; private set; }

        public IReadOnlyList<GateLink> GateLinks => gateLinks;

        public MachineState MachineAt(int x, int y) => machines.TryGetValue(Idx(x, y), out var m) ? m : null;

        public void Swing(int x, int y)
        {
            var c = At(x, y);
            string to = c == null ? null : TerrainOf(c).SwingsTo;
            if (to == null || !Rules.Terrain.TryGetValue(to, out var into)) return;
            var leaves = LeavesFrom(x, y, c.T);
            if (into.Has("blocking") && leaves.Any(l => RaiderAt(l.x, l.y) != null)) return;
            foreach (var (lx, ly) in leaves)
            {
                var leaf = At(lx, ly);
                leaf.T = to;
                leaf.ForgetDef();
            }
        }

        List<(int x, int y)> LeavesFrom(int x, int y, string terrain)
        {
            var leaves = new List<(int x, int y)> { (x, y) };
            var seen = new HashSet<(int, int)> { (x, y) };
            for (int i = 0; i < leaves.Count; i++)
                foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
                {
                    var n = (FoldX(leaves[i].x + dx), FoldY(leaves[i].y + dy));
                    if (InBounds(n.Item1, n.Item2) && At(n.Item1, n.Item2).T == terrain && seen.Add(n)) leaves.Add(n);
                }
            return leaves;
        }

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

        void TickMachines(RowSpan[] rows)
        {
            if (Rules.Machines.Count == 0) return;
            string poweredBy = null;
            string lastT = null;
            MachineDef def = null;
            foreach (var s in rows)
            {
                var cells = s.Chunk.ActiveCells;
                for (int i = s.Start; i < s.End; i++)
                {
                    var t = cells[i].T;
                    if (!ReferenceEquals(t, lastT))
                    {
                        lastT = t;
                        def = Rules.Machines.TryGetValue(t, out var d) ? d : null;
                    }
                    if (def == null) continue;
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

        static bool Hit(string needs, byte marks, Cell c)
        {
            if (needs == "flow") return (marks & FlowMark) != 0;
            if (needs == "wind") return (marks & WindMark) != 0;
            return c.Charge > 0;
        }

        void SetGates(RowSpan[] rows, string poweredBy)
        {
            bool open = poweredBy != null;
            if (open == GatesOpen) return;
            bool changed = false;
            foreach (var s in rows)
            {
                var cells = s.Chunk.ActiveCells;
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
