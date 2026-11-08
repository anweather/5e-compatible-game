using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// Casting a spell by name through the hands, for the playtest bot and the probes (D16): they ask for a spell, and this finds it
// in the hands' table (rules/hold.json), attunes the hands it needs (a moment each, outside the Hold) and makes its gesture, on
// both hands for a merged pair, tapped or held to a charge. The same commands as the pad and the keys (Attune, Hand, HandDown,
// HandUp), so nothing here decides anything a player couldn't. It replaces the element path (LoadElement), which only these used.
public partial class SpellCaster
{
    const float ByHandsWait = 8f;

    /// <summary>
    /// What a spell needs of the hands: the left hand's element (null when one hand casts it alone), the right hand's, and the
    /// gesture. Null if no gesture of the hands brings it out.
    /// </summary>
    public static (string left, string right, int gesture)? HandsFor(World world, string spell)
    {
        var (slot, gesture) = world.Rules.Sim.Hold.Hands.Find(spell);
        if (slot == null || gesture == HandsDef.NoGesture || !world.Rules.Combos.Elements.Contains(slot.Split('+')[0])) return null;
        int plus = slot.IndexOf('+');
        return plus < 0 ? (null, slot, gesture) : (slot.Substring(0, plus), slot.Substring(plus + 1), gesture);
    }

    /// <summary>
    /// Casts <paramref name="spell"/> with the hands: waits for a cast under way, attunes what's needed, then the gesture, held
    /// to <paramref name="charge"/> (1 is a tap) and let go. <paramref name="done"/> hears whether the gesture went in; the spell
    /// itself goes off as the core sets it off (FiredCastsHeard), aimed from the crosshair or AimOverride.
    /// </summary>
    public IEnumerator CastByHands(string spell, int charge = 1, System.Action<bool> done = null)
    {
        var world = grid != null ? grid.World : null;
        var want = world != null ? HandsFor(world, spell) : null;
        if (want == null)
        {
            done?.Invoke(false);
            yield break;
        }
        var (left, right, gesture) = want.Value;
        string player = CastPlayer;
        for (float t = 0f; t < ByHandsWait && (world.CastOf(player)?.Casting == true || world.HandsOf(player).Busy); t += Time.unscaledDeltaTime) yield return null;
        // One hand casting alone: the other may hold anything but the same element, or the two would merge.
        string other = left ?? (world.HandsOf(player).Holds[HandsDef.Left] == right ? null : world.HandsOf(player).Holds[HandsDef.Left]);
        yield return AttuneByHands(HandsDef.Left, other);
        yield return AttuneByHands(HandsDef.Right, right);
        bool ok = world.HandsOf(player).Holds[HandsDef.Left] == other && world.HandsOf(player).Holds[HandsDef.Right] == right;
        if (ok) yield return Gesture(world, player, gesture, left != null, charge);
        done?.Invoke(ok);
    }

    IEnumerator AttuneByHands(int hand, string element)
    {
        var world = grid.World;
        string player = CastPlayer;
        if (world.HandsOf(player).Holds[hand] == element) yield break;
        if (AttuneTo(hand, element) != null) yield break;
        for (float t = 0f; t < ByHandsWait && (world.HandsOf(player).Holds[hand] != element || world.HandsOf(player).AttuneLeft > 0); t += Time.unscaledDeltaTime)
            yield return null;
    }

    // The gesture on the right hand, and on the left a frame later inside the window when both make it; a sweep is a hand's
    // two buttons. Tapped, the core sets it off at once; held, it charges till it reaches the charge, then lets go.
    IEnumerator Gesture(World world, string player, int gesture, bool both, int charge)
    {
        int[] making = both ? new[] { HandsDef.Right, HandsDef.Left } : new[] { HandsDef.Right };
        foreach (int hand in making)
        {
            if (gesture == HandsDef.Sweep)
            {
                Send(ComboCommand.HandDown(player, hand));
                yield return null;
                Send(ComboCommand.HandDown(player, hand, bumper: true));
            }
            else Send(ComboCommand.HandDown(player, hand, gesture == HandsDef.Throw));
            yield return null;
        }
        if (charge > 1)
            for (float t = 0f; t < ByHandsWait && HeldHandCharge(world, player) < charge; t += Time.unscaledDeltaTime) yield return null;
        else
            for (int tick = world.Tick, i = 0; i < 120 && world.Tick == tick; i++) yield return null;
        foreach (int hand in making) Send(ComboCommand.HandUp(player, hand));
    }
}
