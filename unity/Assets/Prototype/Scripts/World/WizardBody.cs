using System.Collections.Generic;
using UnityEngine;

// A wizard's body as the world's shoves see it (docs/design/architecture-review.md step 10), so Forces and the grid
// never name the wizard. Knockback is the one body; it is listed here while enabled, as in its own list, so a shove
// (Forces) finds the wizards a physics overlap misses, and an upward blast tosses a wizard through its feet (Toss)
// rather than pushing it.
public abstract class WizardBody : MonoBehaviour, IPushable
{
    static readonly List<WizardBody> all = new();

    public static IReadOnlyList<WizardBody> All => all;

    protected void Listed() => all.Add(this);

    protected void Unlisted() => all.Remove(this);

    public abstract void Push(Vector3 impulse);

    public abstract void Toss(float speed);
}
