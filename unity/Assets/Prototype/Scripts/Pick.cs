using Chem;
using UnityEngine;

// A pick (items.json "dig"). Using it swings at the cell the holder aims at,
// close by: the core digs it one step at the pick's strength, or says why it
// won't (World.Dig). Like everything a thing does to cells it goes through
// ChemEffects; online the holder asks the host (NetItem), which stamps it for
// every machine.
public class Pick : ItemUse
{
    /// <summary>How far from the wizard a swing can land, in metres.</summary>
    public const float Reach = 3f;

    public override void Use(ItemHolder holder)
    {
        var grid = Item.Grid;
        if (grid == null) return;
        if (!Aiming.PastSelf(Aiming.Ray(holder.controls), holder.transform, out var hit)
            || Vector3.Distance(hit.point, holder.transform.position) > Reach)
        {
            EventLog.Add($"The {Item.Name.ToLowerInvariant()} can't reach that");
            return;
        }
        var cell = grid.CellAt(hit.point);
        if (Item.Online) Item.Net.RequestDig(cell);
        else Swing(cell);
    }

    /// <summary>The swing itself: solo at once, online on the host (NetItem).</summary>
    public void Swing(Vector2Int cell) => ChemEffects.Request(Item.Grid, ChemEffects.By(Item.Prop, EffectKind.Dig, cell));
}
