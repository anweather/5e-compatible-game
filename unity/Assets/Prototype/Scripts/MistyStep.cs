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
    // Steepest ground to land on: 55 degrees, a little past the controller's own slope limit.
    const float MinUpward = 0.57f;
    const float Eye = 1.6f;
    static readonly RaycastHit[] Hits = new RaycastHit[32];
    static readonly Collider[] Overlaps = new Collider[16];

    /// <summary>
    /// Why <paramref name="wizard"/> can't blink to what the crosshair <paramref name="hit"/>, or null
    /// if it can, with where it would land. Pointing at the side of something you can stand on (the
    /// cabin's wall, a rock's face) lands on its top, straight above where you pointed.
    /// </summary>
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

    /// <summary>Why <paramref name="wizard"/> can't blink onto square (x, y), or null if it can.</summary>
    public static string Check(ChemGrid grid, SpellDef spell, Transform wizard, int x, int y, int charge = 1)
    {
        if (!grid.World.InBounds(x, y)) return $"{spell.Name} needs a square to land on.";
        return TopOf(grid, wizard, x, y, out var top) ? CheckSpot(grid, spell, wizard, top, charge) : $"{spell.Name} needs ground to land on.";
    }

    /// <summary>How many squares a blink reaches at a charge: the spell's range, half again held, twice at full charge.</summary>
    public static int Range(SpellDef spell, int charge = 1) => (spell.Range > 0 ? spell.Range : 6) * (1 + World.ClampCharge(charge)) / 2;

    /// <summary>
    /// Where the crosshair's <paramref name="hit"/> takes <paramref name="wizard"/>: the spot itself if
    /// it can land there (Check), else the farthest spot on the ground towards it that it can, so aiming
    /// past the range or at a tree still blinks. Returns why not (the spot's own reason) if nowhere on
    /// the way will do.
    /// </summary>
    public static string Reach(ChemGrid grid, SpellDef spell, Transform wizard, RaycastHit hit, out Vector3 landing, int charge = 1)
    {
        string why = Check(grid, spell, wizard, hit, out landing, charge);
        if (why == null) return null;
        var from = wizard.position;
        var flat = new Vector3(hit.point.x - from.x, 0f, hit.point.z - from.z);
        int range = Range(spell, charge);
        // Chebyshev squares: along a diagonal the range reaches further; a square's slack for walking on.
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

    // Nearer than this isn't worth a blink; the steps back along the line towards the caster.
    const float MinReach = 2f, ReachStep = 0.5f;

    /// <summary>
    /// Moves <paramref name="wizard"/> to square (x, y): to its BlinkPoint when it aimed at a spot
    /// in that square, else onto the square's top surface. It checks again first (the spot may have
    /// filled up since the cast was aimed). The mist at both ends is the cast's show, on every
    /// machine (SpellCaster.PlayCastEffect). Returns whether it moved.
    /// </summary>
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
        if (hasBody) body.enabled = false; // a controller would undo a direct move
        if (wizard.TryGetComponent<NetWizard>(out var net) && net.IsSpawned) net.Teleport(to);
        else wizard.position = to;
        if (hasBody) body.enabled = true;
        EventLog.Add("You reappear in a swirl of silver mist");
        return true;
    }

    // The rules for a landing spot, in order of what a player most likely got wrong. A spot the
    // crosshair hit is already seen: the camera's ray reached it first, which is how a roof is
    // seen from below. A square's top is checked from the wizard's eyes.
    static string CheckSpot(ChemGrid grid, SpellDef spell, Transform wizard, RaycastHit spot, int charge, bool seen = false)
    {
        int range = Range(spell, charge);
        var point = spot.point;
        var from = grid.CellAt(wizard.position);
        var to = grid.CellAt(point);
        if (Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y)) > range)
            return $"Too far. {spell.Name} reaches {range * 5} ft ({range} squares).";
        // Fog Cloud's fog hides what's behind it, from the crosshair too (the core's line of sight).
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

    // How far in from the face the crosshair hit to look for a top: a box's roof is just inside its
    // wall; a rock's rounded shoulder can be steep for a metre or two before its top flattens out.
    static readonly float[] Inward = { 0.6f, 1.2f, 2f, 3f };

    // The top of the thing the crosshair hit, inside the face it hit: rays down onto the same collider
    // from above it, stepping inward until one finds somewhere you could stand.
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

    // The highest thing to stand on over a square's centre (not the wizard, not a cell blocker).
    static bool TopOf(ChemGrid grid, Transform wizard, int x, int y, out RaycastHit top) =>
        GroundAt(grid.CellCenter(x, y) + Vector3.up * (grid.SurfaceY(x, y) + 40f), wizard, out top);

    // The highest thing to stand on under a point 40 m up from it (not the wizard, not a cell blocker).
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

    /// <summary>
    /// What would be in the wizard's capsule standing at the spot (a prop, a wall, an overhang), or
    /// null if there's room. The capsule's foot sits high enough over the ground it stands on that
    /// a slope it may land on doesn't count, and the wizard's own parts (and what it holds) don't either.
    /// </summary>
    public static Collider InTheWay(Transform wizard, Vector3 point, Vector3 normal)
    {
        float radius = 0.3f, height = 1.8f;
        if (wizard.TryGetComponent<CharacterController>(out var body))
        {
            radius = body.radius;
            height = body.height;
        }
        float r = radius * 0.9f;
        // A sphere touches a slope of this normal when its centre is less than r / normal.y over the spot.
        float lift = r / Mathf.Max(normal.y, MinUpward) + 0.08f;
        var bottom = point + Vector3.up * lift;
        var top = point + Vector3.up * Mathf.Max(lift + 0.02f, height - radius);
        int n = Physics.OverlapCapsuleNonAlloc(bottom, top, r, Overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!Overlaps[i].transform.IsChildOf(wizard)) return Overlaps[i];
        return null;
    }

    // Sight in 3D, from the wizard's eyes to a little above the spot, so a roof can be seen over
    // the cabin's own squares: rocks, the cabin and the ground block it, and so do the cell
    // blockers at their real heights (trees, stone, raised walls), except a see-through cell's
    // (gate bars), as in the sandbox's lineOfSight.
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
