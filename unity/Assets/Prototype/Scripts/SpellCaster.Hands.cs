using Chem;
using UnityEngine;

// The hands in the Hold (docs/design/the-hold.md, phase 3): the wizard's side of World.Hands. During a run each hand's button
// sends a ComboCommand, and what comes out goes off on the meter like any other cast (FiredCastsHeard): a spell aimed from the
// crosshair, pulled back to its gesture's range, or an empty hand's act. In the attuning room a hand's trigger moves it to the
// next of the elements and nothing, so nothing else is needed to choose.
public partial class SpellCaster
{
    /// <summary>Where the last hand spell was aimed (the probe).</summary>
    public Vector2Int LastHandAim { get; private set; }

    void Hands(string player, bool busy)
    {
        if (busy) return;
        bool thrustLeft = controls.ThrustLeft.WasPressedThisFrame(), thrustRight = controls.ThrustRight.WasPressedThisFrame();
        if (grid.World.InAttuning(player))
        {
            if (thrustLeft) AttuneNext(player, HandsDef.Left);
            if (thrustRight) AttuneNext(player, HandsDef.Right);
            return;
        }
        if (thrustRight) Send(ComboCommand.Hand(player, HandsDef.Right));
        if (thrustLeft) Send(ComboCommand.Hand(player, HandsDef.Left));
        if (controls.ThrowRight.WasPressedThisFrame()) Send(ComboCommand.Hand(player, HandsDef.Right, bumper: true));
        if (controls.ThrowLeft.WasPressedThisFrame()) Send(ComboCommand.Hand(player, HandsDef.Left, bumper: true));
    }

    /// <summary>Picks an upgrade from this wizard's offer (ProgressKeeper).</summary>
    public bool PickUpgrade(string id) => grid != null && grid.World != null && Send(ComboCommand.Pick(CastPlayer, id));

    /// <summary>Tells every peer this wizard's points (ProgressKeeper).</summary>
    public bool BringPoints(int points) => grid != null && grid.World != null && Send(ComboCommand.Bring(CastPlayer, points));

    /// <summary>Goes into a moment, for every peer (HoldMoments).</summary>
    public bool Moment(string id) => ComboCommand.Moment(CastPlayer, id).TakenBy(HoldMoments.Offered) && grid != null && grid.World != null && Send(ComboCommand.Moment(CastPlayer, id));

    // Round the elements, then nothing, then the elements again.
    void AttuneNext(string player, int hand)
    {
        var elements = grid.World.Rules.Combos.Elements;
        string holds = grid.World.HandsOf(player).Holds[hand];
        int at = -1;
        for (int i = 0; i < elements.Count; i++)
            if (elements[i] == holds) at = i;
        string next = at + 1 < elements.Count ? elements[at + 1] : null;
        if (Send(ComboCommand.Attune(player, hand, next))) EventLog.Add($"Your {(hand == HandsDef.Left ? "left" : "right")} hand holds {next ?? "nothing"}");
    }

    // A hand's fired spell or act, for this wizard.
    void FromHand(ComboFired fired)
    {
        if (fired.Spell == null) hands?.HandAct(fired.Act);
        else CastAtCrosshair(fired.Spell, fired.Charge, fired.Range);
    }
}
