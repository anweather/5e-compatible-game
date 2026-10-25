using UnityEngine;

// An item that breaks when it lands hard (items.json "breaks"): a bullseye
// lantern dropped from a height or hurled at the ground smashes and splashes
// its burning oil over the square, through ChemEffects like a barrel's blast.
// A drop from the hand (about 4.5 m/s) doesn't break it; a hard throw does.
[RequireComponent(typeof(Carryable))]
public class Fragile : MonoBehaviour
{
    Carryable item;
    bool broken;

    /// <summary>The speed of its hardest landing so far, in m/s.</summary>
    public float HardestImpact { get; private set; }

    void Awake() => item = GetComponent<Carryable>();

    void OnCollisionEnter(Collision hit)
    {
        if (broken || item.Holder != null || item.Def == null) return;
        float speed = hit.relativeVelocity.magnitude;
        HardestImpact = Mathf.Max(HardestImpact, speed);
        if (!item.Def.BreaksAt(speed)) return;
        broken = true;
        var grid = item.Grid;
        if (item.Def.Breaks.Spill != null) ChemEffects.Request(grid, new ChemEffect(ChemEffectKind.Spill, grid.CellAt(transform.position), item.Def.Id));
        else EventLog.Add($"The {item.Name.ToLowerInvariant()} shatters");
        Destroy(gameObject);
    }
}
