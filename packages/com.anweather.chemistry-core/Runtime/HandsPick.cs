// The pad's hands menu (pad-hands-menu, docs/playtests/2026-11-08-human-results.md M1): hold the d-pad's down, and each stick
// points its hand at an element, the rules' elements round it clockwise from up; letting go attunes the hands that changed.
// Engine-free so the core's tests can hold it; the view (HandsMenu) feeds it the sticks and sends what it confirms through
// SpellCaster.AttuneTo, one hand at a time as the core attunes.
using System;
using System.Collections.Generic;

namespace Chem
{
    public static class HandsPick
    {
        public const float DeadZone = 0.5f;

        public static int Pointed(float x, float y, int count, float deadZone = DeadZone)
        {
            if (count <= 0 || x * x + y * y < deadZone * deadZone) return -1;
            double across = x, up = y;
            double turn = Math.Atan2(across, up) / (2 * Math.PI);
            if (turn < 0) turn += 1;
            return (int)Math.Round(turn * count) % count;
        }

        public static (float x, float y) Direction(int index, int count)
        {
            double a = 2 * Math.PI * index / Math.Max(1, count);
            return ((float)Math.Sin(a), (float)Math.Cos(a));
        }

        public static List<(int hand, string element)> Confirmed(IReadOnlyList<string> holds, IReadOnlyList<string> picks)
        {
            var changes = new List<(int, string)>();
            for (int hand = 0; hand < picks.Count && hand < holds.Count; hand++)
                if (picks[hand] != null && picks[hand] != holds[hand]) changes.Add((hand, picks[hand]));
            return changes;
        }
    }
}
