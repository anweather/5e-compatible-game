using Chem;
using UnityEngine;

// The hands in the Hold (docs/design/the-hold.md, phase 3): the wizard's side of World.Hands. During a run each hand's button
// sends a ComboCommand, and what comes out goes off on the meter like any other cast (FiredCastsHeard): a spell aimed from the
// crosshair, pulled back to its gesture's range, or an empty hand's act. In the attuning room a hand's trigger moves it to the
// next of the elements it has and nothing, so nothing else is needed to choose. A locked hand or gesture says so when pressed.
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
        bool throwLeft = controls.ThrowLeft.WasPressedThisFrame(), throwRight = controls.ThrowRight.WasPressedThisFrame();
        if (thrustRight && Open(player, HandsDef.Right, HandsDef.Thrust)) Send(ComboCommand.Hand(player, HandsDef.Right));
        if (thrustLeft && Open(player, HandsDef.Left, HandsDef.Thrust)) Send(ComboCommand.Hand(player, HandsDef.Left));
        // Both of a hand's buttons are its sweep: the throw that makes one isn't a locked throw.
        if (throwRight && Open(player, HandsDef.Right, thrustRight || controls.ThrustRight.IsPressed() ? HandsDef.Sweep : HandsDef.Throw)) Send(ComboCommand.Hand(player, HandsDef.Right, bumper: true));
        if (throwLeft && Open(player, HandsDef.Left, thrustLeft || controls.ThrustLeft.IsPressed() ? HandsDef.Sweep : HandsDef.Throw)) Send(ComboCommand.Hand(player, HandsDef.Left, bumper: true));
    }

    const float LockedEvery = 2f;
    float lockedSaidAt = float.NegativeInfinity;

    /// <summary>What the player last heard about something locked (the probe).</summary>
    public string LockedLine { get; private set; }

    // Whether this hand and gesture are the wizard's; if not it hears why, and the press goes nowhere as it would in the core.
    bool Open(string player, int hand, int gesture)
    {
        var world = grid.World;
        string side = hand == HandsDef.Left ? "left" : "right";
        string why = null;
        if (!world.HandOpen(player, hand)) why = Locked($"Your {side} hand", world, player, u => u.Hand == side);
        else if (!world.GestureOpen(player, gesture)) why = Locked($"The {HandsDef.GestureName(gesture)}", world, player, u => u.Gesture == gesture);
        if (why == null) return true;
        if (Time.unscaledTime - lockedSaidAt > LockedEvery || why != LockedLine)
        {
            lockedSaidAt = Time.unscaledTime;
            EventLog.Add(LockedLine = why);
        }
        return false;
    }

    static string Locked(string what, World world, string player, System.Func<UnlockDef, bool> gives)
    {
        var row = world.UnlockGiving(gives);
        int away = row != null ? world.PointsAway(player, row) : 0;
        return $"{what} is locked: {away} more {(away == 1 ? "point" : "points")} to unlock it in the Hold";
    }

    /// <summary>Picks an upgrade from this wizard's offer (ProgressKeeper).</summary>
    public bool PickUpgrade(string id) => grid != null && grid.World != null && Send(ComboCommand.Pick(CastPlayer, id));

    /// <summary>Tells every peer this wizard's points (ProgressKeeper).</summary>
    public bool BringPoints(int points) => grid != null && grid.World != null && Send(ComboCommand.Bring(CastPlayer, points));

    /// <summary>Goes into a moment, for every peer (HoldMoments).</summary>
    public bool Moment(string id) => ComboCommand.Moment(CastPlayer, id).TakenBy(HoldMoments.Offered) && grid != null && grid.World != null && Send(ComboCommand.Moment(CastPlayer, id));

    // Round the elements this wizard has, then nothing, then the elements again; a locked hand says so.
    void AttuneNext(string player, int hand)
    {
        var world = grid.World;
        if (!Open(player, hand, HandsDef.Thrust)) return;
        var elements = world.Rules.Combos.Elements;
        string holds = world.HandsOf(player).Holds[hand];
        int at = -1;
        for (int i = 0; i < elements.Count; i++)
            if (elements[i] == holds) at = i;
        string next = null;
        for (int i = at + 1; i < elements.Count && next == null; i++)
            if (world.ElementOpen(player, elements[i])) next = elements[i];
        if (next == holds) return;
        if (Send(ComboCommand.Attune(player, hand, next))) EventLog.Add($"Your {(hand == HandsDef.Left ? "left" : "right")} hand holds {next ?? "nothing"}");
    }

    // A hand's fired spell or act, for this wizard.
    void FromHand(ComboFired fired)
    {
        if (fired.Spell == null) hands?.HandAct(fired.Act);
        else CastAtCrosshair(fired.Spell, fired.Charge, fired.Range);
    }
}
