using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's cloth scenario (docs/FAST_LOOP.md): the wizard's robe as cloth
// (RobeCloth) through the poses that clipped it most: walking, running, a dodge
// roll, sitting on a bench, lying on a lounger and swimming, each filmed from
// the side. Checks the cloth is there and stepping, and that it stays whole
// (no particle flung more than a metre and a half from the hips). For a before
// and after, set WizardOutfit.RigidRobe before running it, for the old shell.
public class ClothProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var animator = wizard.GetComponent<Animator>();
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var cloth = wizard.GetComponentInChildren<RobeCloth>();
        Metric("rigid", WizardOutfit.RigidRobe ? 1 : 0);
        if (!WizardOutfit.RigidRobe) Check("robe_is_cloth", cloth != null, cloth != null ? "RobeCloth on the outfit" : "no RobeCloth");
        var input = wizard.GetComponent<StarterAssets.StarterAssetsInputs>();
        int steps0 = cloth != null ? cloth.Steps : 0;

        // Walking, then running, across the yard, the camera alongside.
        Place(6, 10);
        yield return Wait(0.5f);

        yield return Filmed(WalkFor(20, 10, 3f));
        Shot("walk");
        yield return Wait(0.2f);
        Place(6, 10);
        input.SprintInput(true);
        yield return Filmed(WalkFor(20, 10, 2.2f));
        input.SprintInput(false);
        Shot("run");
        yield return Wait(0.2f);

        // A dodge roll.
        Place(8, 10);
        puppet.Face(Yard(20, 10));
        Cut(Yard(12, 13) + V(0f, 1.4f, 0f), Yard(11, 10) + V(0f, 0.6f, 0f));
        yield return Wait(0.6f);
        if (wizard.TryGetComponent<DodgeRoll>(out var roll)) roll.TryRoll();
        yield return Wait(0.25f);
        Shot("roll");
        yield return Wait(0.2f);
        yield return Wait(1f);

        // A bench, then a lounger.
        var sitter = wizard.GetComponent<Sitter>();
        foreach (bool lounge in new[] { false, true })
        {
            var seat = Nearest(lounge);
            if (seat == null || sitter == null) continue;
            Teleport(seat.Foot + seat.Facing * Vector3.forward * 1.1f + Vector3.up * 0.2f);
            yield return Wait(0.5f);
            sitter.Sit(seat);
            Cut(seat.Foot + seat.Facing * V(1.8f, 1.1f, 1.6f), seat.SeatPoint);
            yield return Wait(2.5f);
            Shot(lounge ? "lounger" : "bench");
            yield return Wait(0.2f);
            sitter.Stand();
            yield return Wait(0.5f);
        }

        // Swimming in the lake.
        var deep = SwimProbe.DeepestLake(grid, out float depth);
        if (depth > 2f)
        {
            var at = grid.CellCenter(deep.x, deep.y);
            float top = grid.WaterSurfaceY(deep.x, deep.y);
            Teleport(new Vector3(at.x, top + 0.5f, at.z));
            Cut(new Vector3(at.x, top, at.z) + V(-3f, 1.2f, -3f), new Vector3(at.x, top - 0.3f, at.z));
            yield return Wait(3f);
            puppet.Move(new Vector2(0f, 1f));
            yield return Wait(1.5f);
            puppet.Move(Vector2.zero);
            Shot("swim");
        }

        if (cloth == null) yield break;
        Metric("cloth_steps", cloth.Steps - steps0);
        Check("cloth_steps", cloth.Steps - steps0 > 300, $"{cloth.Steps - steps0} steps");
        float farthest = cloth.Farthest(hips.position);
        Metric("farthest_from_hips_m", farthest);
        Check("cloth_stays_whole", farthest < 1.5f, $"farthest particle {farthest:0.00} m from the hips");
    }

    // Runs a walk with the camera alongside the wizard, 3.5 m off to its left and a little above.
    IEnumerator Filmed(IEnumerator walk)
    {
        while (walk.MoveNext())
        {
            var at = wizard.transform.position;
            var left = -wizard.transform.right;
            Cut(at + left * 3.5f + Vector3.up * 1.3f, at + Vector3.up * 0.8f);
            yield return walk.Current;
        }
    }

    static Seat Nearest(bool lounge) =>
        FindObjectsByType<Seat>(FindObjectsSortMode.None).Where(s => s.lounge == lounge)
            .OrderBy(s => s.transform.position.sqrMagnitude).FirstOrDefault();
}
