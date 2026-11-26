using System.Collections;
using System.Globalization;
using System.Linq;
using Chem;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

// The probe's pants scenario (playtest 2026-11-08 B2: the wizard's pants, the robot's legs under the robe, stop
// being drawn). The wizard walks, casts with its back to the camera, swims, shifts into a deer and walks and swims as
// one, changes back, and the world is reset. After each the camera is put in front of it and the probe checks the legs
// (the body's skinned mesh and its legs material) and the robe's cloth are drawn: on, not hidden by Shapeshift or the
// camera, seen by the camera, bounds round the body, the hips on the body (unseen, the rig once stopped writing its bones
// and the poses piled up, carrying the hips 50 m up: the cause), and the shins the length they were and under the hips.
public class PantsProbe : ProbeScenario
{
    // How far the shins may grow or shrink, and how far the feet may be from under the hips (m).
    const float ShinGive = 0.05f, FeetReach = 0.8f;
    // The most the hips may be over the wizard's feet (they're about 1 m up standing; swimming, the feet hang lower).
    const float HipsMost = 2f;

    Animator animator;
    Transform hips, leftKnee, leftFoot, rightKnee, rightFoot;
    float shin;

    protected override IEnumerator Run()
    {
        animator = wizard.GetComponent<Animator>();
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        leftKnee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightKnee = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        Metric("animator_always_animates", animator.cullingMode == AnimatorCullingMode.AlwaysAnimate ? 1 : 0);
        Place(10, 10);
        yield return Wait(1.5f);
        shin = Vector3.Distance(leftKnee.position, leftFoot.position);
        Metric("shin_m", shin);
        yield return Legs("standing");

        yield return WalkFor(18, 10, 2f);
        yield return Legs("walked");

        // Casting with the camera behind a wall of nothing: the wizard out of view while the cast pose works the legs.
        Place(10, 10);
        Cut(Yard(10, 10) + V(0f, 30f, 0f) + V(40f, 0f, 0f), Yard(10, 10) + V(80f, 30f, 0f));
        for (int i = 0; i < 3; i++) yield return CastFromBar(SpellIds.FireBolt, 18, 10, 1, 1.2f);
        yield return Legs("cast_unseen");

        yield return Swim();
        yield return Legs("swam");

        yield return Shifted();
        yield return Legs("shifted_back");

        grid.ResetWorld();
        yield return Wait(2f);
        Place(10, 10);
        yield return Wait(1f);
        yield return Legs("reset");
    }

    IEnumerator Swim()
    {
        var deep = SwimProbe.DeepestLake(grid, out float depth);
        Check("deep_water_found", depth > 2f, $"{depth:0.0} m");
        if (depth <= 2f) yield break;
        var at = grid.CellCenter(deep.x, deep.y);
        float top = grid.WaterSurfaceY(deep.x, deep.y);
        Teleport(new Vector3(at.x, top + 0.5f, at.z));
        yield return Wait(2f);
        puppet.Move(Vector2.up);
        yield return Wait(2f);
        puppet.Move(Vector2.zero);
        yield return Wait(1f);
        Place(10, 10);
        yield return Wait(1.5f);
    }

    // Into a deer in the meadow (as the shapeshift probe does), a walk and a swim as one, the camera away, then back.
    IEnumerator Shifted()
    {
        var fauna = FindAnyObjectByType<ValleyFauna>();
        var shape = wizard.GetComponent<Shapeshift>();
        int deer = fauna != null ? fauna.SpeciesIndex("deer") : -1;
        bool ready = fauna != null && shape != null && deer >= 0;
        Check("shapeshift_ready", ready, $"fauna {fauna != null}, Shapeshift {shape != null}, deer {deer}");
        if (!ready) yield break;
        var home = V(27f, 0f, 26f);
        Teleport(new Vector3(home.x, grid.GroundAt(home) + 0.05f, home.z));
        yield return Wait(0.5f);
        var w = wizard.transform.position;
        var deerAt = w + Vector3.forward * 8f;
        int target = fauna.Spawn(deer, new float3(deerAt.x, grid.GroundAt(deerAt), deerAt.z), math.PI);
        fauna.Puppet(target, math.PI, 0f);
        puppet.Face(deerAt);
        yield return null;
        var eye = wizard.transform.position + Vector3.up * 1.6f;
        var ray = new Ray(eye, deerAt + Vector3.up * 0.7f - eye);
        string why = shape.Aim(World.Rules.Spell(SpellIds.Shapeshift), ray, out var cell);
        caster.Select(SpellIds.Shapeshift);
        caster.Cast(SpellIds.Shapeshift, cell.x, cell.y, 1);
        yield return Wait(1f);
        Check("shifted", shape.Shifted, why ?? shape.FormName ?? "not shifted");
        Cut(w + V(0f, 40f, 0f), w + V(60f, 40f, 0f));
        puppet.Move(Vector2.up);
        yield return Wait(2f);
        puppet.Move(Vector2.zero);
        var deep = SwimProbe.DeepestLake(grid, out float depth);
        if (depth > 2f)
        {
            var at = grid.CellCenter(deep.x, deep.y);
            Teleport(new Vector3(at.x, grid.WaterSurfaceY(deep.x, deep.y) + 0.5f, at.z));
            yield return Wait(2f);
            puppet.Move(Vector2.up);
            yield return Wait(2f);
            puppet.Move(Vector2.zero);
        }
        Teleport(new Vector3(home.x, grid.GroundAt(home) + 0.05f, home.z));
        yield return Wait(1f);
        if (shape.Shifted) shape.ChangeBackByChoice();
        yield return Wait(1.5f);
        Check("changed_back", !shape.Shifted && !shape.ModelHidden, shape.FormName ?? "the wizard");
    }

    // The camera 4 m in front of the wizard, looking at its knees; a few frames for the culling to see it, then the checks.
    IEnumerator Legs(string at)
    {
        var w = wizard.transform;
        Cut(w.position + w.forward * 4f + Vector3.up * 1.2f, w.position + Vector3.up * 0.5f);
        for (int i = 0; i < 4; i++) yield return null;
        var skin = wizard.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(s => s.sharedMaterials.Any(m => m != null && m.name.Contains("Legs")));
        var robe = wizard.GetComponentInChildren<RobeCloth>();
        var robeLook = robe != null ? robe.GetComponent<MeshRenderer>() : null;
        Check($"{at}_legs_drawn", Drawn(skin, out string legs), legs);
        Check($"{at}_robe_drawn", Drawn(robeLook, out string cloth), cloth);
        float lifted = hips.position.y - wizard.transform.position.y;
        Metric($"{at}_hips_over_feet_m", lifted);
        Check($"{at}_hips_on_the_body", lifted > 0.2f && lifted < HipsMost && Flat(hips.position - wizard.transform.position) < 0.5f,
            string.Format(CultureInfo.InvariantCulture, "hips {0:0.00} m over the wizard's feet, {1:0.00} m to the side", lifted, Flat(hips.position - wizard.transform.position)));
        float l = Vector3.Distance(leftKnee.position, leftFoot.position), r = Vector3.Distance(rightKnee.position, rightFoot.position);
        float feet = Mathf.Max(Flat(leftFoot.position - hips.position), Flat(rightFoot.position - hips.position));
        float under = hips.position.y - Mathf.Max(leftFoot.position.y, rightFoot.position.y);
        Metric($"{at}_feet_from_hips_m", feet);
        Check($"{at}_legs_in_place", Mathf.Abs(l - shin) < ShinGive && Mathf.Abs(r - shin) < ShinGive && feet < FeetReach && under > 0.3f,
            string.Format(CultureInfo.InvariantCulture, "shins {0:0.00} and {1:0.00} m (were {2:0.00}), feet {3:0.00} m out from the hips and {4:0.00} m under them",
                l, r, shin, feet, under));
        if (!string.IsNullOrEmpty(ScreenshotDir)) Shot(at);
    }

    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    // On, not hidden (Shapeshift's forceRenderingOff, the camera's shadows only), seen, and its bounds round the hips.
    bool Drawn(Renderer r, out string why)
    {
        if (r == null)
        {
            why = "no renderer";
            return false;
        }
        float off = Vector3.Distance(r.bounds.ClosestPoint(hips.position), hips.position);
        why = string.Format(CultureInfo.InvariantCulture, "{0}: enabled {1}, forced off {2}, {3}, visible {4}, bounds {5:0.00} m from the hips (size {6:0.0})",
            r.name, r.enabled, r.forceRenderingOff, r.shadowCastingMode, r.isVisible, off, r.bounds.size.magnitude);
        return r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly && r.isVisible && off < 0.3f;
    }
}
