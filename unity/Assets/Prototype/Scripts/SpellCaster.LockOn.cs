using Chem;
using UnityEngine;

// Lock-on and pitch for the aimed spells (D16, docs/design/unified-casting.md; the 2026-11-06 playtest). Only a view helper:
// it picks the target cell, and the core still gets an ordinary cast, so lockstep is unaffected.
//  - Pitch: aimed above everything (at a flyer, at the sky), the crosshair lands on the ground under where the camera looks,
//    SkyReach away along its heading, instead of nowhere. So every aimed spell points where the camera does.
//  - Lock-on: Lightning takes the raider nearest the crosshair inside a wide cone (LightningLock); the other aimed spells that
//    hurt or shove (Fire Bolt, Ray of Frost, Gust of Wind, ...) only within a narrow one (SoftLock). A flyer is aimed at
//    where it's drawn, high in the air. Brackets on the screen show who's locked while a spell is under way.
public partial class SpellCaster
{
    const float LightningLock = 14f, SoftLock = 5f, SkyReach = 30f, NoLineReach = 45f, StandingAim = 1f;

    Ray aimRay;
    bool hasAimRay, skyAim;
    Vector3 lockedPoint;
    GUIStyle lockStyle;

    public static bool AimHelp { get; set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetAimHelp()
    {
        AimHelp = true;
    }

    public Ray? AimRayOverride { get; set; }

    public Raider LockedOn { get; private set; }

    public CastCommand LastHandCast { get; private set; }

    public Raider LastLocked { get; private set; }

    bool RayCrosshair()
    {
        if (controls == null || Camera.main == null) return false;
        aimRay = AimRayOverride ?? Aiming.Ray(controls);
        hasAimRay = true;
        if (Aiming.PastSelf(aimRay, transform, out crosshair)) return true;
        if (!AimHelp) return false;
        var heading = aimRay.direction;
        heading.y = 0f;
        heading = heading.sqrMagnitude > 1e-4f ? heading.normalized : transform.forward;
        var far = transform.position + heading * SkyReach;
        crosshair = default;
        crosshair.point = new Vector3(far.x, grid.GroundAt(far), far.z);
        crosshair.normal = Vector3.up;
        skyAim = true;
        return true;
    }

    public static float LockCone(SpellDef s)
    {
        if (s == null || s.Needs != null || s.Effect != null) return 0f;
        if (s.Shape == SpellShapes.Line && s.Element == "lightning") return LightningLock;
        bool aimed = s.Shape == SpellShapes.Line || s.Shape == "target" || s.Shape == "point";
        bool strikes = (s.Damage != null && s.Damage.Length == 2) || s.ForcePush > 0 || s.ForceThrow > 0;
        bool fromHere = s.Shape == SpellShapes.Line && s.Origin == "self";
        return aimed && strikes && (fromHere || s.Origin != "self") ? SoftLock : 0f;
    }

    SpellDef AimingFor(SpellDef selected)
    {
        var held = HeldCast().spell;
        if (held != null) return grid.World.Rules.Spell(held);
        bool handsOn = controls.ElementPad && (controls.HandPad || controls.UsingGamepad);
        return handsOn ? null : selected;
    }

    void LockOnto(SpellDef spell)
    {
        LockedOn = null;
        float cone = LockCone(spell);
        if (!AimHelp || !hasAimRay || AimOverride.HasValue || cone <= 0f || grid.World.Raiders.Count == 0) return;
        float reach = spell.Shape == SpellShapes.Line && spell.Origin == "self" ? (spell.Length + 0.5f) * grid.cellSize : NoLineReach;
        float best = cone;
        foreach (var r in grid.World.Raiders)
        {
            if (!r.Alive) continue;
            var at = RaiderAim(r);
            var flat = at - transform.position;
            flat.y = 0f;
            if (flat.magnitude > reach) continue;
            float angle = Vector3.Angle(aimRay.direction, at - aimRay.origin);
            if (angle >= best) continue;
            best = angle;
            LockedOn = r;
            lockedPoint = at;
        }
        if (LockedOn == null) return;
        var lead = Lead(LockedOn, FlightSeconds(new CastCommand(spell.Id, LockedOn.X, LockedOn.Y, 0)));
        var cell = grid.CellCenter(lead.x, lead.y);
        crosshair = default;
        crosshair.point = new Vector3(cell.x, grid.GroundAt(cell), cell.z);
        crosshair.normal = Vector3.up;
        strikePoint = RaiderAim(LockedOn, lead);
    }

    const int MostLead = 3;

    Vector2Int Lead(Raider r, float seconds)
    {
        var world = grid.World;
        var at = new Vector2Int(r.X, r.Y);
        var def = world.Rules.Entity(r.Kind);
        if (!world.HoldRunning || world.HoldSite == null || def?.Raider == null) return at;
        if (r.StatusOf(World.ThrownStatus) > 0 || r.StatusOf("frozen") > 0 || r.StatusOf("snagged") > 0) return at;
        int ticks = Mathf.CeilToInt(seconds * world.Rules.Sim.TicksPerSecond), first = r.Wait + 1, pace = Mathf.Max(1, def.Raider.TicksPerSquare);
        int steps = ticks < first ? 0 : Mathf.Min(MostLead, 1 + (ticks - first) / pace);
        bool flies = world.Flying(r), road = def.Raider.Route != World.ClimbRoute && def.Raider.Route != World.FlyRoute;
        var heart = world.HoldSite.Heart;
        for (int i = 0; i < steps; i++)
        {
            if (flies) at += new Vector2Int(System.Math.Sign(heart.x - at.x), System.Math.Sign(heart.y - at.y));
            else if (world.HoldNext(at.x, at.y, road) is { } next) at = new Vector2Int(next.x, next.y);
            else break;
        }
        return at;
    }

    Vector3? strikeAt;
    Vector3 strikePoint;

    void StrikeAtLocked() => strikeAt = LockedOn != null ? strikePoint : null;

    Vector3 RaiderAim(Raider r) => RaiderAim(r, new Vector2Int(r.X, r.Y));

    Vector3 RaiderAim(Raider r, Vector2Int at)
    {
        var cell = grid.CellCenter(at.x, at.y);
        float up = grid.World.Flying(r) ? Raiders.FlyHeight : StandingAim;
        return new Vector3(cell.x, grid.GroundAt(cell) + up, cell.z);
    }

    void DrawLock()
    {
        if (LockedOn == null || !LockedOn.Alive || !EventLog.HudVisible || Camera.main == null) return;
        var screen = Camera.main.WorldToScreenPoint(lockedPoint);
        if (screen.z <= 0f) return;
        HudScale.Apply();
        lockStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, richText = true };
        float x = screen.x / HudScale.Factor, y = (Screen.height - screen.y) / HudScale.Factor;
        GUI.Label(new Rect(x - 40f, y - 22f, 80f, 44f), "<color=#ffe27a>[    ]</color>", lockStyle);
    }
}
