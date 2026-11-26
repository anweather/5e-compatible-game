using System.Collections.Generic;
using UnityEngine;

// Shoves from spells and explosions, as physics impulses. One point of
// "push" in the rules (Thunderwave 3, Gust of Wind 2, a barrel's blast 2) is
// ImpulsePerPush newton-seconds, and one point of entity mass is KgPerMass
// kilograms, so a crate (mass 1) flies off three times as fast as a boulder
// (mass 3), much as the sandbox's "distance = force - mass + 1" has it.

public interface IPushable
{
    void Push(Vector3 impulse);
}

public static class Forces
{
    public const float KgPerMass = 40f;
    public const float ImpulsePerPush = 100f;
    const float Loft = 0.35f;
    const float ColumnHeight = 3f;

    static readonly Collider[] Hits = new Collider[64];

    public static int Radial(Vector3 center, float reach, float impulse, Transform except, System.Predicate<IPushable> only = null)
    {
        var targets = Around(center, reach, except, only);
        foreach (var target in targets)
        {
            var away = Position(target) - center;
            away.y = 0f;
            float distance = away.magnitude;
            var dir = distance > 1e-3f ? away / distance : Vector3.forward;
            target.Push(Thrown(dir) * (impulse * (1f - 0.5f * Mathf.Clamp01(distance / reach))));
        }
        return targets.Count;
    }

    public static int Up(Vector3 center, float reach, float impulse, float wizardSpeed, Transform except, System.Predicate<IPushable> only = null)
    {
        var targets = Around(center, reach, except, only);
        foreach (var target in targets)
        {
            if (target is Knockback wizard)
            {
                if (wizard.TryGetComponent<SurfaceEffects>(out var feet)) feet.Toss(wizardSpeed);
            }
            else target.Push(Vector3.up * impulse);
        }
        return targets.Count;
    }

    static List<IPushable> Around(Vector3 center, float reach, Transform except, System.Predicate<IPushable> only)
    {
        var targets = InSphere<IPushable>(center, reach, except);
        foreach (var wizard in Knockback.All)
        {
            if (targets.Contains(wizard) || (except != null && wizard.transform.IsChildOf(except))) continue;
            if ((wizard.transform.position - center).sqrMagnitude <= reach * reach) targets.Add(wizard);
        }
        if (only != null) targets.RemoveAll(t => !only(t));
        return targets;
    }

    public static int Along(ChemGrid grid, List<(int x, int y)> cells, Vector3 direction, float impulse, Transform except, System.Predicate<IPushable> only = null)
    {
        var pushed = OnCells(grid, cells, except, only);
        foreach (var target in pushed) target.Push(Thrown(direction.normalized) * impulse);
        return pushed.Count;
    }

    public static HashSet<IPushable> OnCells(ChemGrid grid, List<(int x, int y)> cells, Transform except, System.Predicate<IPushable> only = null)
    {
        var found = new HashSet<IPushable>();
        var half = new Vector3(grid.cellSize / 2f, ColumnHeight / 2f, grid.cellSize / 2f);
        foreach (var (x, y) in cells)
        {
            var column = grid.CellCenter(x, y) + Vector3.up * (grid.SurfaceY(x, y) + half.y);
            int count = Physics.OverlapBoxNonAlloc(column, half, Hits, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var target = Find<IPushable>(Hits[i], except);
                if (target != null && (only == null || only(target))) found.Add(target);
            }
        }
        return found;
    }

    public static List<T> InSphere<T>(Vector3 center, float reach, Transform except) where T : class
    {
        var found = new List<T>();
        int count = Physics.OverlapSphereNonAlloc(center, reach, Hits, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var target = Find<T>(Hits[i], except);
            if (target != null && !found.Contains(target)) found.Add(target);
        }
        return found;
    }

    public static T Nearest<T>(Vector3 point, float reach) where T : Component
    {
        T best = null;
        float bestDistance = float.MaxValue;
        foreach (var candidate in InSphere<T>(point, reach, null))
        {
            float d = (candidate.transform.position - point).sqrMagnitude;
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = candidate;
        }
        return best;
    }

    public static Vector3 Thrown(Vector3 flatDirection) => (flatDirection + Vector3.up * Loft).normalized;

    static Vector3 Position(IPushable target) => target is Component c ? c.transform.position : Vector3.zero;

    static T Find<T>(Collider hit, Transform except) where T : class
    {
        var target = hit.GetComponentInParent<T>();
        if (target is not Component c || (except != null && c.transform.IsChildOf(except))) return null;
        return target;
    }
}
