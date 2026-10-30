using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's look scenario (docs/FAST_LOOP.md): the wizard's outfit
// (WizardOutfit, WizardLook). Checks the robot is dressed (face, headwear, robe,
// bodice, sleeves), hidden under the clothes but for its hands and feet, the
// headwear sits on the head and moves with it, the same seed always gives the
// same look, "New look" draws a different one and the headwear swaps alone;
// portraits of a few looks from the front. The player's own saved look is put back after.
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
