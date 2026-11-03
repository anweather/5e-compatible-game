using System;
using System.Collections.Generic;
using UnityEngine;

// What a row house looks like as its cells go: each stretch of wall follows the cell under it, leaving
// a broken stump, the roof squares it held up go with it (and the whole roof once half its walls are
// down), and a wall on fire is sooted. A door's panel goes with its cell; the head over a gap stands
// while the wall beside it does. It only hides and tints what RowMesher drew, so a change writes one
// index list and one colour list and never builds a mesh, and cells that come back bring it all back.
// It also says which stretches are out, so their windows' lamps go dark (RowHouses, WindowPanes).
// Only the show: the cells are the truth, so every peer's ruin comes out alike, a late joiner's too.
public sealed class RowRuin
{
    const float Soot = 0.16f;

    /// <summary>What a stretch's cell says now.</summary>
    public struct CellNow
    {
        public bool blocking, burning, ashen;
    }

    enum State : byte { Standing, Holed, Down }

    readonly RowMesher.Stretch[] stretches;
    readonly Mesh body, roof;
    // Each mesh's triangles whole, what nothing owns (the floor, the chimney), and what's shown now.
    readonly int[] bodyAll, bodyFixed, bodyShown, roofAll, roofShown;
    readonly Color32[] colorsAll, colorsShown;
    readonly bool[] ours, solid, burnt, sooted, roofGone;
    readonly State[] states;
    readonly Dictionary<RowMesher.Stretch, int> index = new();

    /// <summary>Stretches down now, and whether any of the roof has gone.</summary>
    public int Down { get; private set; }
    public bool RoofHurt { get; private set; }
    public int Stretches => stretches.Length;

    /// <summary>Takes over a house's meshes, all of it standing; <paramref name="onACell"/> says which stretches have a wall's cell under them.</summary>
    public RowRuin(RowMesher.Result made, Func<Vector2Int, bool> onACell)
    {
        stretches = made.stretches;
        body = made.body;
        roof = made.roof;
        bodyAll = body.triangles;
        roofAll = roof.triangles;
        bodyFixed = (int[])bodyAll.Clone();
        bodyShown = new int[bodyAll.Length];
        roofShown = new int[roofAll.Length];
        colorsAll = body.colors32;
        colorsShown = new Color32[colorsAll.Length];
        int n = stretches.Length;
        ours = new bool[n];
        solid = new bool[n];
        burnt = new bool[n];
        sooted = new bool[n];
        roofGone = new bool[n];
        states = new State[n];
        for (int i = 0; i < n; i++)
        {
            var s = stretches[i];
            index[s] = i;
            ours[i] = onACell(s.Place);
            solid[i] = ours[i];
            foreach (var run in s.Whole) Unfix(run);
            foreach (var run in s.Door) Unfix(run);
            foreach (var run in s.Gable) Unfix(run);
            Unfix(s.Left);
        }
        body.MarkDynamic();
        Write(true);
    }

    /// <summary>Whether a window in this stretch has gone dark: its wall fell, fire reached it or its neighbours, or the house fell (half its stretches down).</summary>
    public bool Out(RowMesher.Stretch s) => s != null && index.TryGetValue(s, out int i) && (states[i] == State.Down || sooted[i] || Down * 2 >= stretches.Length);

    void Unfix(RowMesher.Run run) => Array.Clear(bodyFixed, run.Index, run.Indices);

    /// <summary>Forgets the fire on the stretches of these cells: they were put back.</summary>
    public void Forget(Func<Vector2Int, bool> put)
    {
        for (int i = 0; i < stretches.Length; i++)
            if (put(stretches[i].Place)) burnt[i] = false;
    }

    /// <summary>Reads the cells and redraws if anything shows differently. True if it did.</summary>
    public bool Look(Func<Vector2Int, CellNow> cell, bool roofBurns)
    {
        int n = stretches.Length, down = 0;
        bool bodyChanged = false, roofChanged = false;
        for (int i = 0; i < n; i++)
        {
            var c = cell(stretches[i].Place);
            bodyChanged |= solid[i] != c.blocking;
            solid[i] = c.blocking;
            burnt[i] |= c.burning || (c.ashen && !c.blocking);
        }
        for (int i = 0; i < n; i++)
        {
            var now = StateOf(i);
            if (now == State.Down) down++;
            bodyChanged |= now != states[i];
            states[i] = now;
        }
        Down = down;
        bool roofFalls = down * 2 >= n;
        for (int i = 0; i < n; i++)
        {
            bool gone = roofFalls || states[i] == State.Down || (roofBurns && burnt[i]);
            roofChanged |= gone != roofGone[i];
            roofGone[i] = gone;
            bodyChanged |= Sooted(i) != sooted[i];
        }
        if (!bodyChanged && !roofChanged) return false;
        Write(roofChanged);
        return true;
    }

    // Whether a wall's cell, or the head over a gap, still has what it needs.
    State StateOf(int i)
    {
        if (ours[i] && solid[i]) return State.Standing;
        var s = stretches[i];
        if (ours[i] && !s.Opening) return State.Down;
        return Held(s, true) || Held(s, false) ? State.Holed : State.Down;
    }

    // Along the wall one way, over the gaps, to a stretch that stands on its cell, or at the wall's
    // end to a wall that meets it there (an arch can be a whole wall).
    bool Held(RowMesher.Stretch from, bool before)
    {
        var last = from;
        for (var s = before ? from.Before : from.After; s != null; s = before ? s.Before : s.After)
        {
            int i = index[s];
            if (ours[i] && solid[i]) return true;
            if (ours[i] && !s.Opening) return false;
            last = s;
        }
        foreach (var other in last.Meets)
            if (ours[index[other]] && solid[index[other]]) return true;
        return false;
    }

    bool Sooted(int i)
    {
        if (burnt[i]) return true;
        var s = stretches[i];
        return (s.Before != null && burnt[index[s.Before]]) || (s.After != null && burnt[index[s.After]]);
    }

    void Write(bool roofToo)
    {
        Array.Copy(bodyFixed, bodyShown, bodyFixed.Length);
        Array.Copy(colorsAll, colorsShown, colorsAll.Length);
        for (int i = 0; i < stretches.Length; i++) WriteStretch(i);
        body.SetTriangles(bodyShown, 0, false);
        body.colors32 = colorsShown;
        if (!roofToo) return;
        Array.Clear(roofShown, 0, roofShown.Length);
        RoofHurt = false;
        for (int i = 0; i < stretches.Length; i++)
        {
            RoofHurt |= roofGone[i] && stretches[i].Roof.Count > 0;
            if (roofGone[i]) continue;
            foreach (var run in stretches[i].Roof) Array.Copy(roofAll, run.Index, roofShown, run.Index, run.Indices);
        }
        roof.SetTriangles(roofShown, 0, false);
    }

    void WriteStretch(int i)
    {
        var s = stretches[i];
        bool down = states[i] == State.Down;
        sooted[i] = Sooted(i);
        if (down) Show(s.Left);
        else foreach (var run in s.Whole) Show(run);
        if (!ours[i] || solid[i]) foreach (var run in s.Door) Show(run);
        if (!roofGone[i]) foreach (var run in s.Gable) Show(run);
        if (!sooted[i]) return;
        foreach (var run in s.Whole) Darken(run);
        foreach (var run in s.Door) Darken(run);
        foreach (var run in s.Gable) Darken(run);
        Darken(s.Left);
    }

    void Show(RowMesher.Run run) => Array.Copy(bodyAll, run.Index, bodyShown, run.Index, run.Indices);

    void Darken(RowMesher.Run run)
    {
        for (int v = run.Vertex; v < run.Vertex + run.Vertices; v++)
        {
            var c = colorsAll[v];
            colorsShown[v] = new Color32((byte)(c.r * Soot), (byte)(c.g * Soot), (byte)(c.b * Soot), c.a);
        }
    }
}
