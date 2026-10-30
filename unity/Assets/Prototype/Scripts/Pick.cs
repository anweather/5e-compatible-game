using System.Collections;
using Chem;
using UnityEngine;

// A pick (items.json "dig"). Using it swings at the cell the holder aims at,
// close by: the arm whips down (ThrowPose) and, as it lands, chips fly and the
// core digs the cell one step at the pick's strength, or says why it won't
// (World.Dig). Like everything a thing does to cells it goes through
// ChemEffects; online the holder asks the host (NetItem), which stamps it for
// every machine.
public class Pick : ItemUse
{
    /// <summary>How far from the wizard a swing can land, in metres.</summary>
    public const float Reach = 3f;

    // How far through the arm's whip the head lands, and how hard the whip looks.
    const float LandSeconds = 0.14f, SwingHow = 0.8f;

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
        holder.Swing(SwingHow);
        StartCoroutine(Land(grid.CellAt(hit.point), hit.point));
    }

    IEnumerator Land(Vector2Int cell, Vector3 at)
    {
        yield return new WaitForSeconds(LandSeconds);
        var ground = CellDressing.Look(Item.Grid.World.At(cell.x, cell.y).T).color;
        BurstFx.Play(BurstFx.Kind.Chips, at, 1f, null, ground);
        GameSounds.Play(SoundIds.Earth, at);
        if (Item.Online) Item.Net.RequestDig(cell);
        else Swing(cell);
    }

    /// <summary>The swing itself: solo at once, online on the host (NetItem).</summary>
    public void Swing(Vector2Int cell) => ChemEffects.Request(Item.Grid, ChemEffects.By(Item.Prop, EffectKind.Dig, cell));
}
