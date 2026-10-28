using System.Collections.Generic;
using UnityEngine;

// Wizards for measuring what the robes' cloth costs (FpsProbe's -cloth-wizards):
// copies of the local wizard that only look the part. Each is cloned while
// inactive (so none of its scripts wake), its outfit parts are dropped so its
// own WizardOutfit dresses it afresh (cloth, beard and all), every other script
// and its CharacterController stay off, and its animator walks on the spot, so
// the legs move through the robe as they would on the road. View only.
public static class ClothCrowd
{
    /// <summary>
    /// Makes <paramref name="count"/> copies of <paramref name="wizard"/> standing in a row at these
    /// points, facing <paramref name="facing"/>. Destroy them with <see cref="Clear"/>.
    /// </summary>
    public static List<GameObject> Make(GameObject wizard, IReadOnlyList<Vector3> at, Quaternion facing)
    {
        var crowd = new List<GameObject>();
        var holder = new GameObject("Cloth crowd");
        holder.SetActive(false);
        for (int i = 0; i < at.Count; i++)
        {
            var copy = Object.Instantiate(wizard, at[i], facing, holder.transform);
            // Not found as the local wizard.
            copy.name = $"Cloth crowd wizard {i + 1}";
            copy.tag = "Untagged";
            Strip(copy);
            crowd.Add(copy);
        }
        foreach (var copy in crowd)
        {
            copy.transform.SetParent(null, true);
            WalkOnTheSpot(copy);
        }
        Object.Destroy(holder);
        return crowd;
    }

    public static void Clear(List<GameObject> crowd)
    {
        foreach (var copy in crowd)
            if (copy != null) Object.Destroy(copy);
        crowd.Clear();
    }

    // Everything but the look off; the outfit's built parts dropped (the copy's outfit builds its own).
    static void Strip(GameObject copy)
    {
        foreach (var t in copy.GetComponentsInChildren<Transform>(true))
            if (t != null && t != copy.transform && (t.name.StartsWith("Outfit ", System.StringComparison.Ordinal) || t.name == "Sleeve hang"))
                Object.DestroyImmediate(t.gameObject);
        foreach (var script in copy.GetComponentsInChildren<MonoBehaviour>(true))
            if (script is not WizardOutfit) script.enabled = false;
        if (copy.TryGetComponent<CharacterController>(out var body)) body.enabled = false;
    }

    // The walk's animation without the walking (its parameters set once it's awake).
    static void WalkOnTheSpot(GameObject copy)
    {
        if (copy.TryGetComponent<Animator>(out var animator))
        {
            animator.applyRootMotion = false;
            foreach (var p in animator.parameters)
            {
                if (p.name == "Speed") animator.SetFloat(p.nameHash, 2f);
                else if (p.name == "MotionSpeed") animator.SetFloat(p.nameHash, 1f);
                else if (p.name == "Grounded") animator.SetBool(p.nameHash, true);
            }
        }
    }
}
