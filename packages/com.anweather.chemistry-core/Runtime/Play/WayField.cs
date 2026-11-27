// The cost field's scratch that the raiders' ways are worked out in (docs/design/architecture-review.md step 9): one
// array of costs over a field's cells and a min-heap, reused from tick to tick. The world owns one (World.Ways), and
// both the defense field's march (DefenseSystem) and the Hold's ways (HoldSystem) work in it, as they always did:
// PathCostAt reads what was last worked out there, by either. Data and its heap, no rules.
using System;
using System.Collections.Generic;

namespace Chem
{
    internal sealed class WayField
    {
        public const int Unreached = int.MaxValue;

        public int[] Cost = Array.Empty<int>();

        public readonly List<long> Heap = new List<long>();

        public static bool InField(DefenseState d, int x, int y) => x >= d.X0 && x <= d.X1 && y >= d.Y0 && y <= d.Y1;

        public static int Index(DefenseState d, int x, int y) => (y - d.Y0) * (d.X1 - d.X0 + 1) + (x - d.X0);

        public void Push(int cell, int n)
        {
            Heap.Add((long)Cost[cell] * n + cell);
            int k = Heap.Count - 1;
            while (k > 0)
            {
                int parent = (k - 1) / 2;
                if (Heap[parent] <= Heap[k]) break;
                (Heap[parent], Heap[k]) = (Heap[k], Heap[parent]);
                k = parent;
            }
        }

        public long Pop()
        {
            long top = Heap[0];
            int last = Heap.Count - 1;
            Heap[0] = Heap[last];
            Heap.RemoveAt(last);
            int k = 0;
            while (true)
            {
                int a = 2 * k + 1, b = a + 1, least = k;
                if (a < Heap.Count && Heap[a] < Heap[least]) least = a;
                if (b < Heap.Count && Heap[b] < Heap[least]) least = b;
                if (least == k) break;
                (Heap[least], Heap[k]) = (Heap[k], Heap[least]);
                k = least;
            }
            return top;
        }
    }
}
