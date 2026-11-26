using Chem;
using UnityEngine;

// An item that breaks when it lands hard (items.json "breaks"): a bullseye
// lantern dropped from a height or hurled at the ground smashes and splashes
// its burning oil over the square, through ChemEffects like a barrel's blast;
// a glow ball thrown hard, or struck by something fast, shatters into a spray
// of glass shards (PhysicsProp.ShowBreak, ShardBurst) with the break sound.
// A drop from the hand (about 4.5 m/s) doesn't break it; a hard throw does.
// A material that cracks (shatter.cracks: a glow ball's glass) takes two hard
// hits: the first cracks it (EffectKind.Crack, so it is saved and every peer
// has it; a thing with no stable id keeps it here) and dims it, the next breaks it.
// Online only the host's copy lands for real (a client's is a puppet); the
// spill is stamped for every machine and the item is gone from all of them.
[RequireComponent(typeof(Carryable))]
public class Fragile : MonoBehaviour
{
    Carryable item;
    const float BounceSeconds = 0.4f, CrackedDim = 0.6f;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    bool broken, cracked, shownCracked;
    float bouncingUntil;

    public float HardestImpact { get; private set; }

    void Awake() => item = GetComponent<Carryable>();

    void Update()
    {
        if (shownCracked || item.Grid == null || item.Grid.World == null) return;
        if (!cracked && !item.Grid.World.IsCracked(item.Prop.Thing)) return;
        shownCracked = true;
        var block = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (!r.sharedMaterial.HasProperty(BaseColor)) continue;
            r.GetPropertyBlock(block);
            block.SetColor(BaseColor, r.sharedMaterial.GetColor(BaseColor) * CrackedDim);
            r.SetPropertyBlock(block);
        }
    }

    bool Cracks()
    {
        var grid = item.Grid;
        if (!grid.World.Rules.ItemCracks(item.Def.Id)) return false;
        if (cracked || grid.World.IsCracked(item.Prop.Thing)) return Time.time < bouncingUntil;
        cracked = true;
        bouncingUntil = Time.time + BounceSeconds;
        ChemEffects.Request(grid, ChemEffects.By(item.Prop, EffectKind.Crack, grid.CellAt(transform.position)));
        return true;
    }

    void OnCollisionEnter(Collision hit)
    {
        if (broken || item.Holder != null || item.Def == null || item.Puppet) return;
        float speed = hit.relativeVelocity.magnitude;
        HardestImpact = Mathf.Max(HardestImpact, speed);
        if (!item.Def.BreaksAt(speed) || Cracks()) return;
        broken = true;
        var grid = item.Grid;
        bool spills = item.Def.Breaks.Spill != null;
        if (spills)
        {
            ChemEffects.Request(grid, ChemEffects.By(item.Prop, EffectKind.Spill, grid.CellAt(transform.position)));
            if (item.Prop.Shatters(out var tint)) ShardBurst.At(transform.position, tint);
        }
        else item.Prop.ShowBreak(false);
        if (TryGetComponent<NetProp>(out var net) && net.Hosting) net.Broke(false, quiet: spills);
        else Destroy(gameObject);
    }
}
