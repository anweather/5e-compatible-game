using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's look scenario (docs/FAST_LOOP.md): the wizard's outfit
// (WizardOutfit, WizardLook). Checks the robot is dressed (face, headwear, robe,
// bodice, sleeves), hidden under the clothes but for its hands and feet, the
// headwear sits on the head and moves with it, the same seed always gives the
// same look, "New look" draws a different one and the headwear swaps alone;
// no wizard carries a staff, whatever its seed drew, and the staff's code still
// hangs one if asked; portraits of a few looks from the front. The player's own
// saved look is put back after.
public class LookProbe : ProbeScenario
{
    // The outfit's parts hanging on the wizard, and its hat.
    int OutfitParts(out Transform hat)
    {
        int parts = 0;
        hat = null;
        foreach (var r in wizard.GetComponentsInChildren<Renderer>())
        {
            if (!r.name.StartsWith("Outfit ", System.StringComparison.Ordinal)) continue;
            parts++;
            if (r.name == "Outfit hat") hat = r.transform;
        }
        return parts;
    }

    // Anything of the outfit hanging on either hand (the sleeves hang on the arms).
    static int OnHands(Animator animator)
    {
        int parts = 0;
        foreach (var bone in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
            parts += animator.GetBoneTransform(bone).GetComponentsInChildren<Renderer>().Count(r => r.name.StartsWith("Outfit ", System.StringComparison.Ordinal));
        return parts;
    }

    // One wizard of each staff a seed can draw, at a clear noon with the same cameras: standing, walking,
    // then with an element in each hand: none of them carries it. For the gallery (--shots); hands-*.png.
    IEnumerator HandsFree(WizardOutfit outfit)
    {
        int seed = outfit.Look.Seed;
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        yield return Wait(1f);
        var w = wizard.transform;
        var animator = wizard.GetComponent<Animator>();
        int onHands = 0;
        foreach (WizardLook.StaffKind kind in System.Enum.GetValues(typeof(WizardLook.StaffKind)))
        {
            int s = 1;
            while (WizardLook.From(s).Staff != kind) s++;
            outfit.Wear(s);
            yield return Wait(0.5f);
            onHands += OnHands(animator);
            foreach (var (name, from) in new[] { ("front", w.forward), ("quarter", (w.forward - w.right).normalized), ("side", -w.right) })
            {
                Cut(w.position + from * 2.4f + Vector3.up * 1.4f, w.position + Vector3.up * 1.05f);
                yield return Wait(0.2f);
                Shot($"hands-{kind}-{name}");
                yield return null;
            }
            caster.enabled = true;
            caster.LoadElement(ElementIds.Fire, true);
            caster.LoadElement(ElementIds.Water, true);
            yield return Wait(0.3f);
            Cut(w.position + (w.forward - w.right * 0.4f).normalized * 2.2f + Vector3.up * 1.4f, w.position + Vector3.up * 1.05f);
            yield return Wait(0.2f);
            Shot($"hands-{kind}-elements");
            yield return null;
            caster.DropCast();
            yield return Wait(0.3f);
            caster.enabled = false;
            var walk = StartCoroutine(puppet.WalkTo(w.position + w.forward * 30f));
            yield return Wait(0.8f);
            Cut(w.position + (w.forward - w.right).normalized * 2.6f + Vector3.up * 1.5f, w.position + Vector3.up * 1.05f);
            yield return null;
            Shot($"hands-{kind}-walk");
            yield return null;
            StopCoroutine(walk);
            puppet.Move(Vector2.zero);
            yield return Wait(0.5f);
        }
        Metric("parts_on_hands", onHands);
        Check("hands_free", onHands == 0, $"{onHands} outfit parts on the hands over one wizard of each staff kind");
        // The staff's code, unused until a focus brings it back: it still hangs one, in the left hand.
        outfit.carriesStaff = true;
        int crystal = 1;
        while (WizardLook.From(crystal).Staff != WizardLook.StaffKind.Crystal) crystal++;
        outfit.Wear(crystal);
        Check("staff_kept_for_a_focus", OnHands(animator) == 1, $"{OnHands(animator)} parts on the hands with carriesStaff on");
        outfit.carriesStaff = false;
        outfit.Wear(seed);
        yield return Wait(0.3f);
    }

    // The cowl's opening, about its own upright: how far back of the head bone that stands, and the
    // half angle (radians) that takes in its steps of quads.
    const float HoodAxisBack = 0.06f, HoodOpening = 1.02f;

    // Rays in from outside the head, over all of it above the brim: the headwear must be met before
    // the head is. For a cowl, the opening in front of the face is left out.
    static int HeadThrough(MeshFilter hat, Transform head, bool cowl)
    {
        var shell = hat.gameObject.AddComponent<MeshCollider>();
        shell.sharedMesh = hat.sharedMesh;
        Physics.SyncTransforms();
        int through = 0;
        for (int i = 0; i < 2000; i++)
        {
            // A spiral of even points over the ball, in the head's own space.
            float y = 1f - 2f * (i + 0.5f) / 2000f, round = Mathf.Sqrt(1f - y * y), turn = i * 2.39996f;
            var dir = new Vector3(Mathf.Cos(turn) * round, y, Mathf.Sin(turn) * round);
            var skin = WizardOutfit.FaceAt + Vector3.Scale(dir, WizardOutfit.FaceSize / 2f);
            if (skin.y < (cowl ? 0.12f : WizardOutfit.HatCrown + 0.015f) || (cowl && Mathf.Abs(Mathf.Atan2(skin.x, skin.z + HoodAxisBack)) < HoodOpening)) continue;
            var at = head.TransformPoint(skin);
            var outward = head.TransformDirection(Vector3.Scale(dir, new Vector3(1f / WizardOutfit.FaceSize.x, 1f / WizardOutfit.FaceSize.y, 1f / WizardOutfit.FaceSize.z))).normalized;
            if (!shell.Raycast(new Ray(at + outward, -outward), out _, 1f)) through++;
        }
        Destroy(shell);
        return through;
    }

    // The ball of a head stays inside the hat and the cowl, for many seeds, and a picture of each.
    IEnumerator HeadwearFits(WizardOutfit outfit, Transform head)
    {
        int seed = outfit.Look.Seed, worst = 0, worstSeed = 0;
        var fit = new System.Collections.Generic.Dictionary<WizardLook.HeadwearKind, int>();
        for (int i = 2; i <= 200; i++)
        {
            // What's worn is in the seed's top bits: each drawn wizard in the one and in the other.
            int s = i % 2 == 0 ? i / 2 : WizardLook.NextHeadwear(i / 2);
            outfit.Wear(s);
            OutfitParts(out var hat);
            bool cowl = outfit.Look.Headwear == WizardLook.HeadwearKind.Cowl;
            int through = HeadThrough(hat.GetComponent<MeshFilter>(), head, cowl);
            if (through > worst) (worst, worstSeed) = (through, s);
            if (!fit.TryAdd(outfit.Look.Headwear, s)) continue;
            var w = wizard.transform;
            foreach (var (name, side) in new[] { ("front", 0f), ("side", 90f) })
            {
                var from = Quaternion.AngleAxis(side, Vector3.up) * w.forward;
                Cut(w.position + from * 1.3f + Vector3.up * 1.75f, w.position + Vector3.up * 1.6f);
                yield return Wait(0.2f);
                Shot($"head-{outfit.Look.Headwear}-{name}");
                yield return null;
            }
        }
        Metric("head_through_worst", worst);
        Check("head_inside_headwear", worst == 0, $"worst {worst} points of the head out through its headwear, seed {worstSeed}, over 200 seeds, {string.Join(", ", fit.Keys)}");
        outfit.Wear(seed);
        yield return Wait(0.2f);
    }

    protected override IEnumerator Run()
    {
        var outfit = wizard.GetComponent<WizardOutfit>();
        Check("outfit_part", outfit != null && outfit.material != null, outfit != null ? "WizardOutfit with its material" : "no WizardOutfit");
        if (outfit == null) yield break;
        yield return Wait(0.5f);
        int saved = WizardOutfit.SavedSeed;
        Check("dressed", outfit.Look != null && outfit.Look.Seed == saved, outfit.Look != null ? $"wearing seed {outfit.Look.Seed}" : "not dressed");

        int parts = OutfitParts(out var hat);
        Metric("outfit_parts", parts);
        Check("parts_hung", parts >= 6 && hat != null, $"{parts} outfit parts on the bones");
        var head = wizard.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
        Check("hat_on_head", hat != null && hat.parent == head, hat != null ? $"the hat hangs on {hat.parent.name}" : "no hat");
        var skin = wizard.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => !r.name.StartsWith("Outfit ", System.StringComparison.Ordinal));
        int hidden = skin.bones.Count(b => b.name == "Hidden");
        var animator = wizard.GetComponent<Animator>();
        Metric("bones_hidden", hidden);
        Check("body_hidden", hidden >= 10 && skin.bones.Contains(animator.GetBoneTransform(HumanBodyBones.LeftFoot)) && skin.bones.Contains(animator.GetBoneTransform(HumanBodyBones.RightHand)) && !skin.bones.Contains(head),
            $"{hidden} bones hidden; feet and hands kept, the head gone");

        // The same seed, the same wizard; another seed, another.
        var a = WizardLook.From(12345);
        var b = WizardLook.From(12345);
        var c = WizardLook.From(54321);
        Check("same_seed_same_look", a.Robe == b.Robe && Mathf.Approximately(a.HatHeight, b.HatHeight) && a.Staff == b.Staff, "seed 12345 twice");
        Check("seeds_differ", a.Robe != c.Robe || !Mathf.Approximately(a.HatHeight, c.HatHeight) || a.Staff != c.Staff, "seeds 12345 and 54321");

        yield return HeadwearFits(outfit, head);
        yield return HandsFree(outfit);
        // Portraits: the camera in front of the wizard, a few looks.
        var w = wizard.transform;
        Cut(w.position + w.forward * 2.6f + Vector3.up * 1.5f, w.position + Vector3.up * 1.15f);
        yield return Wait(0.3f);
        Shot("look-1");
        yield return null;
        Cut(w.position + w.forward * 1f + Vector3.up * 1.55f, w.position + Vector3.up * 1.5f);
        yield return Wait(0.2f);
        Shot("face");
        yield return null;
        // The other headwear: the same wizard otherwise.
        var robe = outfit.Look.Robe;
        var worn = outfit.Look.Headwear;
        outfit.NextHeadwear();
        yield return Wait(0.3f);
        Check("headwear_swaps", outfit.Look.Headwear != worn && outfit.Look.Robe == robe, $"{worn} then {outfit.Look.Headwear}");
        Shot("face-other-headwear");
        yield return null;
        Cut(w.position + w.forward * 2.6f + Vector3.up * 1.5f, w.position + Vector3.up * 1.15f);
        yield return Wait(0.2f);
        Shot("other-headwear");
        yield return null;
        OutfitParts(out hat);
        Check("headwear_on_head", hat != null && hat.parent == head, "after the swap");
        int first = outfit.Look.Seed;
        for (int i = 2; i <= 4; i++)
        {
            outfit.Reroll();
            yield return Wait(0.3f);
            Shot($"look-{i}");
        }
        Check("new_look", outfit.Look.Seed != first, $"seed {first} then {outfit.Look.Seed}");
        Cut(w.position + w.right * 2.4f + Vector3.up * 1.4f, w.position + Vector3.up * 1.15f);
        yield return Wait(0.3f);
        Shot("side");
        yield return null;
        Cut(w.position - w.forward * 2.4f + Vector3.up * 1.4f, w.position + Vector3.up * 1.15f);
        yield return Wait(0.2f);
        Shot("back");
        yield return null;

        // Put the player's own look back.
        PlayerPrefs.SetInt("wizard.look", saved);
        outfit.Wear(saved);
    }
}
