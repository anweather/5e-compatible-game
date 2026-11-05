using Chem;

// The hands in the Hold (docs/design/the-hold.md, phase 3): the wizard's side of World.Hands. During a run a hand's button sends
// a ComboCommand, with its bumper or Shift for modified, and the spell goes off on the meter like any other (FiredCastsHeard,
// CastAtCrosshair). In the attuning room the same button attunes that hand to the next element, so nothing else is needed to choose.
public partial class SpellCaster
{
    void Hands(string player, bool busy)
    {
        if (busy) return;
        bool left = controls.HandLeft.WasPressedThisFrame(), right = controls.HandRight.WasPressedThisFrame();
        if (grid.World.InAttuning(player))
        {
            if (left) AttuneNext(player, HandsDef.Left);
            if (right) AttuneNext(player, HandsDef.Right);
            return;
        }
        if (right) Send(ComboCommand.Hand(player, HandsDef.Right, controls.ModifyRight.IsPressed()));
        if (left) Send(ComboCommand.Hand(player, HandsDef.Left, controls.ModifyLeft.IsPressed()));
    }

    void AttuneNext(string player, int hand)
    {
        var elements = grid.World.Rules.Combos.Elements;
        string next = elements[(IndexOf(elements, grid.World.HandsOf(player).Element[hand]) + 1) % elements.Count];
        if (Send(ComboCommand.Attune(player, hand, next))) EventLog.Add($"Your {(hand == HandsDef.Left ? "left" : "right")} hand holds {next}");
    }

    static int IndexOf(System.Collections.Generic.IReadOnlyList<string> list, string item)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == item) return i;
        return -1;
    }
}
