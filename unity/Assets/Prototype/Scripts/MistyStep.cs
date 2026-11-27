using Chem;
using UnityEngine;

// Misty Step: blink up to the spell's range (rules/spells.json) to a spot you
// can see, onto the ground or up onto anything you can stand on: a big rock,
// the cabin, the top of a Wall of Stone. The web sandbox's rules (range in
// squares, walls block sight, gate bars don't) plus the 3D ones: the spot
// must be a walkable surface (not too steep, not an invisible cell blocker)
// with room for the wizard, and in sight physically as well as on the grid
// (Fog Cloud's thick fog hides a spot behind it: World.FogBetween).
//
// Cast from the crosshair, the wizard lands where it points (SpellCaster
// keeps that spot as BlinkPoint); pointed past the range, or at somewhere it
// can't stand (a tree, a crate), it lands on the farthest spot it can reach
// along the same line (Reach). Cast by square (demos, scripts), it lands on
// the top surface over that square. Held (charged), it reaches half as far
// again, or twice as far at full charge (Range). The chemistry has nothing to do with
// it (World.Cast only logs it); this moves the caster's own wizard, so online
// only the caster's machine runs it and the wizard's owner-authoritative
// NetworkTransform carries the jump to everyone else (NetWizard.Teleport).
public static class MistyStep
{
    const float MinUpward = 0.57f;
    const float Eye = 1.6f;
    static readonly RaycastHit[] Hits = new RaycastHit[32];
    static readonly Collider[] Overlaps = new Collider[16];

    public static string Check(ChemGrid grid, SpellDef spell, Transform wizard, RaycastHit hit, out Vector3 landing, int charge = 1)
    {
        landing = hit.point;
        if (hit.normal.y < MinUpward && !CellBlockers.IsBlocker(hit.collider) && TopAbove(hit, out var top))
        {
            landing = top.point;
            return CheckSpot(grid, spell, wizard, top, charge, seen: true);
        }
        return CheckSpot(grid, spell, wizard, hit, charge, seen: true);
    }

    public static string Check(ChemGrid grid, SpellDef spell, Transform wizard, int x, int y, int charge = 1)
    {
        if (!grid.World.InBounds(x, y)) return $"{spell.Name} needs a square to land on.";
        return TopOf(grid, wizard, x, y, out var top) ? CheckSpot(grid, spell, wizard, top, charge) : $"{spell.Name} needs ground to land on.";
    }

    public static int Range(SpellDef spell, int charge = 1) => (spell.Range > 0 ? spell.Range : 6) * (1 + World.ClampCharge(charge)) / 2;

    public static string Reach(ChemGrid grid, SpellDef spell, Transform wizard, RaycastHit hit, out Vector3 landing, int charge = 1)
    {
        string why = Check(grid, spell, wizard, hit, out landing, charge);
        if (why == null) return null;
        var from = wizard.position;
        var flat = new Vector3(hit.point.x - from.x, 0f, hit.point.z - from.z);
        int range = Range(spell, charge);
        float far = Mathf.Min(flat.magnitude, (range - 0.5f) * grid.cellSize * Mathf.Sqrt(2f));
        if (far < MinReach) return why;
        var dir = flat / flat.magnitude;
        for (float d = far; d >= MinReach; d -= ReachStep)
        {
            var at = from + dir * d;
            if (!GroundAt(at, wizard, out var top)) continue;
            if (CheckSpot(grid, spell, wizard, top, charge) != null) continue;
            landing = top.point;
            return null;
        }
        return why;
    }

    const float MinReach = 2f, ReachStep = 0.5f;

    public static bool Blink(ChemGrid grid, SpellDef spell, Transform wizard, int x, int y, int charge = 1)
    {
        wizard.TryGetComponent<SpellCaster>(out var caster);
        Vector3 to;
        string why;
        var aimed = caster != null ? caster.BlinkPoint : null;
        if (aimed.HasValue && grid.CellAt(aimed.Value) == new Vector2Int(x, y) && Physics.Raycast(aimed.Value + Vector3.up * 0.5f, Vector3.down, out var spot, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            to = spot.point;
            why = CheckSpot(grid, spell, wizard, spot, charge, seen: true);
        }
        else if (TopOf(grid, wizard, x, y, out var top))
        {
            to = top.point;
            why = CheckSpot(grid, spell, wizard, top, charge);
        }
        else
        {
            to = default;
            why = "there's no ground there";
        }
        if (caster != null) caster.BlinkPoint = null;
        if (why != null)
        {
            EventLog.Add($"{spell.Name} fizzles: {why}");
            return false;
        }
        bool hasBody = wizard.TryGetComponent<CharacterController>(out var body);
        if (hasBody) body.enabled = false;
        if (wizard.TryGetComponent<NetWizard>(out var net) && net.IsSpawned) net.Teleport(to);
        else wizard.position = to;
        if (hasBody) body.enabled = true;
        return true;
    }

    static string CheckSpot(ChemGrid grid, SpellDef spell, Transform wizard, RaycastHit spot, int charge, bool seen = false)
    {
        int range = Range(spell, charge);
        var point = spot.point;
        var from = grid.CellAt(wizard.position);
        var to = grid.CellAt(point);
        if (Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y)) > range)
            return $"Too far. {spell.Name} reaches {range * 5} ft ({range} squares).";
        if (grid.World.FogBetween(from.x, from.y, to.x, to.y))
            return "You can't see that spot through the fog.";
        if (CellBlockers.IsBlocker(spot.collider) || spot.normal.y < MinUpward)
            return $"{spell.Name} needs somewhere to stand: ground, a rock, a roof.";
        var blocker = InTheWay(wizard, point, spot.normal);
        if (blocker != null)
            return $"There's no room to stand there ({blocker.name} is in the way).";
        if (!seen && !InSight(grid, wizard, point))
            return "You can't see that spot. Walls block sight; gate bars don't.";
        return null;
    }

    static readonly float[] Inward = { 0.6f, 1.2f, 2f, 3f };

    static bool TopAbove(RaycastHit hit, out RaycastHit top)
    {
        top = default;
        var inward = new Vector3(-hit.normal.x, 0f, -hit.normal.z);
        if (inward.sqrMagnitude < 1e-4f) return false;
        inward.Normalize();
        var bounds = hit.collider.bounds;
        foreach (float step in Inward)
        {
            var probe = hit.point + inward * step;
            var from = new Vector3(probe.x, bounds.max.y + 1f, probe.z);
            if (hit.collider.Raycast(new Ray(from, Vector3.down), out top, bounds.size.y + 2f) && top.normal.y >= MinUpward) return true;
        }
        return false;
    }

    static bool TopOf(ChemGrid grid, Transform wizard, int x, int y, out RaycastHit top) =>
        GroundAt(grid.CellCenter(x, y) + Vector3.up * grid.SurfaceY(x, y), wizard, out top);

    static bool GroundAt(Vector3 at, Transform wizard, out RaycastHit top)
    {
        top = default;
        var above = new Vector3(at.x, at.y + 40f, at.z);
        int n = Physics.RaycastNonAlloc(above, Vector3.down, Hits, 80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var h = Hits[i];
            if (h.distance >= best || h.collider.transform.IsChildOf(wizard) || CellBlockers.IsBlocker(h.collider)) continue;
            best = h.distance;
            top = h;
        }
        return best < float.MaxValue;
    }

    public static Collider InTheWay(Transform wizard, Vector3 point, Vector3 normal)
    {
        float radius = 0.3f, height = 1.8f;
        if (wizard.TryGetComponent<CharacterController>(out var body))
        {
            radius = body.radius * wizard.lossyScale.y;
            height = body.height * wizard.lossyScale.y;
        }
        float r = radius * 0.9f;
        float lift = r / Mathf.Max(normal.y, MinUpward) + 0.08f;
        var bottom = point + Vector3.up * lift;
        var top = point + Vector3.up * Mathf.Max(lift + 0.02f, height - radius);
        int n = Physics.OverlapCapsuleNonAlloc(bottom, top, r, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!Overlaps[i].transform.IsChildOf(wizard)) return Overlaps[i];
        return null;
    }

    static bool InSight(ChemGrid grid, Transform wizard, Vector3 point)
    {
        var eye = wizard.position + Vector3.up * Eye;
        var target = point + Vector3.up * 0.5f;
        var dir = target - eye;
        int n = Physics.RaycastNonAlloc(eye, dir.normalized, Hits, dir.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = Hits[i].collider;
            if (c.transform.IsChildOf(wizard)) continue;
            if (CellBlockers.IsBlocker(c) && SeeThrough(grid, c.bounds.center)) continue;
            return false;
        }
        return true;
    }

    static bool SeeThrough(ChemGrid grid, Vector3 at)
    {
        var cell = grid.CellAt(at);
        var world = grid.World;
        return world.InBounds(cell.x, cell.y) && world.HasTag(world.At(cell.x, cell.y), "seeThrough");
    }
}
