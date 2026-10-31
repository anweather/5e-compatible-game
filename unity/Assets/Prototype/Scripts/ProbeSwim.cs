using System.Collections;
using UnityEngine;

// The probe's swim scenario (docs/FAST_LOOP.md): the wizard drops into the
// deepest part of the lake (SurfaceEffects). Checks it floats at the surface
// rather than sinking to the bed, stays afloat, swims along when pushed, and
// that jump kicks it up out of the water; then that the dive takes it under,
// its breath runs down there, and letting go brings it back up to breathe.
public class SwimProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var surface = wizard.GetComponent<SurfaceEffects>();
        var deep = Deepest(out float depth);
        Metric("water_depth_m", depth);
        Check("deep_water_found", depth > 2.5f, $"{depth:0.0} m of water at cell {deep}");
        if (depth <= 2.5f) yield break;

        var at = grid.CellCenter(deep.x, deep.y);
        float top = grid.WaterSurfaceY(deep.x, deep.y);
        Teleport(new Vector3(at.x, top + 0.5f, at.z));
        Cut(at + V(-7f, 4f, -7f), at + V(0f, top - at.y, 0f));
        yield return Wait(3f);
        float y = wizard.transform.position.y;
        Metric("feet_below_surface_m", top - y);
        Check("afloat", surface.Swimming, "SurfaceEffects.Swimming");
        Check("at_the_surface", top - y > 1f && top - y < 1.9f, $"feet {top - y:0.00} m under the surface (the bed is {depth:0.0} m down)");
        Shot("afloat");

        yield return Wait(3f);
        Check("stays_afloat", surface.Swimming && Mathf.Abs(wizard.transform.position.y - y) < 0.3f, $"y {y:0.00} then {wizard.transform.position.y:0.00}");

        var pose = wizard.GetComponent<SwimPose>();
        var animator = wizard.GetComponent<Animator>();
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Check("swim_pose", pose != null && pose.Weight > 0.9f, pose != null ? $"pose weight {pose.Weight:0.00}" : "no SwimPose");

        var from = wizard.transform.position;
        puppet.Move(Vector2.up);
        yield return Wait(1.2f);
        // While stroking: the body leans forward into it, the head stays out, the hands move.
        float lean = Vector3.Angle(hips.up, Vector3.up);
        float handTravel = 0f;
        var handWas = hand.position - wizard.transform.position;
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            var now = hand.position - wizard.transform.position;
            handTravel += (now - handWas).magnitude;
            handWas = now;
            yield return null;
        }
        Metric("stroke_lean_deg", lean);
        Metric("head_above_water_m", head.position.y - top);
        Metric("hand_travel_m", handTravel);
        Check("leans_into_stroke", lean > 30f, $"hips {lean:0} degrees from upright");
        Check("head_out_of_water", head.position.y > top, $"head {head.position.y - top:0.00} m above the surface");
        Check("arms_stroke", handTravel > 0.5f, $"the right hand moved {handTravel:0.00} m in 0.8 s");
        Shot("stroke");
        puppet.Move(Vector2.zero);
        var swum = wizard.transform.position - from;
        swum.y = 0f;
        Metric("swum_m", swum.magnitude);
        Check("swims_along", swum.magnitude > 1f && surface.Swimming, $"{swum.magnitude:0.0} m in 2 s");

        float before = wizard.transform.position.y, highest = before;
        surface.Kick(); // what jump does afloat (SurfaceEffects.OnJump)
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            highest = Mathf.Max(highest, wizard.transform.position.y);
            yield return null;
        }
        Metric("kick_m", highest - before);
        Check("jump_kicks_up", highest - before > 1.5f, $"rose {highest - before:0.00} m");
        yield return Wait(2f);
        Check("back_afloat", surface.Swimming, "after the kick it falls back in and floats");

        // The dive held (Left Ctrl, SurfaceEffects.HoldDive): under it goes, holding its breath, and let go it comes back up.
        var breath = wizard.GetComponent<Vitals>().Breath;
        float afloatY = wizard.transform.position.y;
        surface.HoldDive = true;
        yield return Wait(1.5f);
        float dived = afloatY - wizard.transform.position.y;
        double held = breath.Left;
        Metric("dive_m", dived);
        Check("dives_under", surface.Diving && surface.HeadUnder && dived > 0.8f, $"down {dived:0.00} m, head under {surface.HeadUnder}");
        Shot("diving");
        yield return Wait(1f);
        Metric("breath_left_s", (float)breath.Left);
        Check("holds_breath", breath.Under && breath.Left < held - 0.5, $"breath {held:0.0} s then {breath.Left:0.0} s");
        surface.HoldDive = false;
        yield return Wait(3f);
        Check("comes_back_up", surface.Swimming && !surface.HeadUnder, $"head under {surface.HeadUnder}, afloat {surface.Swimming}");
        Check("breath_refills", breath.Full, $"breath {breath.Left:0.0} of {breath.Max:0} s");
    }

    // The lake square with the most water over the ground. The lake is the lowest water (the
    // pond at the foot of the falls, high up, can be deeper).
    Vector2Int Deepest(out float depth) => DeepestLake(grid, out depth);

    /// <summary>The lake square with the most water over the ground, and how deep (also for the cloth probe).</summary>
    internal static Vector2Int DeepestLake(ChemGrid grid, out float depth)
    {
        float lake = float.MaxValue;
        var world = grid.World;
        for (int y = 0; y < world.H; y++)
            for (int x = 0; x < world.W; x++)
                if (Open(grid, x, y)) lake = Mathf.Min(lake, grid.WaterSurfaceY(x, y));
        depth = 0f;
        var best = Vector2Int.zero;
        for (int y = 0; y < world.H; y++)
            for (int x = 0; x < world.W; x++)
            {
                float d = LakeDepth(grid, x, y, lake);
                if (d <= depth) continue;
                depth = d;
                best = new Vector2Int(x, y);
            }
        return best;
    }

    // Water over the ground in a lake square (one whose surface is the lake's), else 0.
    static float LakeDepth(ChemGrid grid, int x, int y, float lake) =>
        Open(grid, x, y) && grid.WaterSurfaceY(x, y) <= lake + 1f ? grid.WaterSurfaceY(x, y) - TerrainGround(grid.CellCenter(x, y)) : 0f;

    static bool Open(ChemGrid grid, int x, int y)
    {
        var c = grid.World.At(x, y);
        if (c.Water <= 0 || c.Frozen || c.T != TerrainIds.Pit) return false;
        // The lake, not the sea (whose deepest water is out by the world's edge).
        var currents = WaterCurrents.Instance;
        return currents == null || !currents.IsSea(grid.CellCenter(x, y));
    }
}
