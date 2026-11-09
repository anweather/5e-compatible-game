using System.Collections.Generic;
using UnityEngine;

// A walk with no one at the keys, for test runs and clips (NetCommandLine's -walk-to, a clip's walk from the valley into
// the next square): a path over the cells from where a wizard stands to a cell, found once (open ground, no water, no
// step higher than a wizard climbs), then followed by pushing the wizard's stick toward a point a few cells ahead, with a
// jump when it hasn't moved for a moment. It only moves a wizard the way its controls would.
public sealed class TestWalk
{
    const int Margin = 48, Ahead = 6;
    const float MostStep = 0.9f, StuckSeconds = 1.5f;

    readonly List<Vector2Int> path;
    int next;
    float stillFor;
    Vector3 lastAt;

    TestWalk(List<Vector2Int> path) => this.path = path;

    /// <summary>The cells of the path, start to end; none when there's no way.</summary>
    public int Length => path.Count;

    /// <summary>Whether the wizard has come to the end of the path.</summary>
    public bool Done => next >= path.Count - 1;

    /// <summary>A walk from one cell to another, or null when no way is found inside the box round them.</summary>
    public static TestWalk To(ChemGrid grid, Vector2Int from, Vector2Int to)
    {
        var world = grid.World;
        int x0 = Mathf.Max(0, Mathf.Min(from.x, to.x) - Margin), y0 = Mathf.Max(0, Mathf.Min(from.y, to.y) - Margin);
        int x1 = Mathf.Min(world.W - 1, Mathf.Max(from.x, to.x) + Margin), y1 = Mathf.Min(world.H - 1, Mathf.Max(from.y, to.y) + Margin);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        var came = new int[w * h];
        System.Array.Fill(came, -1);
        bool Open(int x, int y)
        {
            var c = world.At(x, y);
            return c != null && !world.IsBlocking(c) && c.Water <= 0;
        }
        int Index(Vector2Int p) => (p.y - y0) * w + p.x - x0;
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        came[Index(from)] = Index(from);
        var steps = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (p == to) break;
            float here = grid.SurfaceY(p.x, p.y);
            foreach (var d in steps)
            {
                var q = p + d;
                if (q.x < x0 || q.y < y0 || q.x > x1 || q.y > y1 || came[Index(q)] >= 0 || !Open(q.x, q.y)) continue;
                if (Mathf.Abs(grid.SurfaceY(q.x, q.y) - here) > MostStep) continue;
                came[Index(q)] = Index(p);
                queue.Enqueue(q);
            }
        }
        if (came[Index(to)] < 0) return null;
        var cells = new List<Vector2Int>();
        for (int k = Index(to); ; k = came[k])
        {
            cells.Add(new Vector2Int(x0 + k % w, y0 + k / w));
            if (came[k] == k) break;
        }
        cells.Reverse();
        return new TestWalk(cells);
    }

    /// <summary>The stick for this frame (the camera's way, as a controller's), and whether to jump.</summary>
    public Vector2 Stick(ChemGrid grid, Transform wizard, Transform cam, float dt, out bool jump)
    {
        jump = false;
        if (Done) return Vector2.zero;
        var at = grid.CellAt(wizard.position);
        // On along the path past the cells it has reached.
        for (int k = next; k < Mathf.Min(path.Count, next + 24); k++)
            if ((path[k] - at).sqrMagnitude <= 2) next = k;
        var goal = path[Mathf.Min(path.Count - 1, next + Ahead)];
        var toward = grid.CellCenter(goal.x, goal.y) - wizard.position;
        toward.y = 0f;
        if ((wizard.position - lastAt).sqrMagnitude < 0.04f) stillFor += dt;
        else
        {
            stillFor = 0f;
            lastAt = wizard.position;
        }
        if (stillFor > StuckSeconds)
        {
            jump = true;
            stillFor = 0f;
        }
        var way = toward.normalized;
        var right = new Vector3(cam.right.x, 0f, cam.right.z).normalized;
        var forward = new Vector3(cam.forward.x, 0f, cam.forward.z).normalized;
        return new Vector2(Vector3.Dot(way, right), Vector3.Dot(way, forward));
    }
}
