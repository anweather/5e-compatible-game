using System;
using System.Collections;
using System.Linq;
using Chem;
using Unity.Netcode;
using UnityEngine;

// The probe's handlooks scenario (docs/design/the-hold.md, "Showing it"): two peers attune differently, an empty hand among them,
// and each must show both wizards' hands as the core has them, the other peer's copy above all. Nothing here is in the game.
public class HandLooksProbe : RoomProbe
{
    const float Settle = 1.5f, MostWait = 10f;
    const string Nothing = "-";

    protected override ChangeMode Mode => ChangeMode.Open;
    protected override bool StartsWithAMod => false;

    /// <summary>What every wizard here shows in its hands, "player:left:right" sorted by player.</summary>
    public static string Shown() => string.Join(" ", FindObjectsByType<HandPose>(FindObjectsSortMode.None)
        .Where(h => h.isActiveAndEnabled)
        .Select(h => $"{h.GetComponent<CastBeats>().Player}:{h.Showing(HandsDef.Left) ?? Nothing}:{h.Showing(HandsDef.Right) ?? Nothing}")
        .OrderBy(s => s, StringComparer.Ordinal));

    /// <summary>This peer's wizard onto the plate, in the attuning room, and its hands to "left,right", "-" for nothing.</summary>
    public static IEnumerator Attune(ChemGrid grid, NetChem net, string hands)
    {
        var wizard = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().gameObject;
        var plate = grid.World.HoldSite.Plate;
        var at = grid.CellCenter(plate.x, plate.y);
        at.y = grid.GroundAt(at) + 0.05f;
        var body = wizard.GetComponent<CharacterController>();
        body.enabled = false;
        wizard.transform.position = at;
        body.enabled = true;
        string me = wizard.GetComponent<CastBeats>().Player;
        for (float t = 0f; t < MostWait && !grid.World.InAttuning(me); t += Time.unscaledDeltaTime) yield return null;
        var wanted = hands.Split(',').Select(h => h == Nothing ? null : h).ToArray();
        net.RequestCombo(ComboCommand.Attune("", HandsDef.Left, wanted[0]));
        net.RequestCombo(ComboCommand.Attune("", HandsDef.Right, wanted[1]));
        for (float t = 0f; t < MostWait; t += Time.unscaledDeltaTime)
        {
            var held = grid.World.HandsOf(me).Holds;
            if (held[HandsDef.Left] == wanted[0] && held[HandsDef.Right] == wanted[1]) break;
            yield return null;
        }
    }

    string Core()
    {
        var world = grid.World;
        return string.Join(" ", FindObjectsByType<HandPose>(FindObjectsSortMode.None)
            .Select(h => h.GetComponent<CastBeats>().Player)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => $"{p}:{world.HandsOf(p).Holds[HandsDef.Left] ?? Nothing}:{world.HandsOf(p).Holds[HandsDef.Right] ?? Nothing}"));
    }

    protected override IEnumerator Steps()
    {
        yield return Both("fire,earth", "water,air", "each_element");
        if (Theirs == null) yield break;
        yield return Both("air,water", "-,earth", "with_an_empty_hand");
    }

    IEnumerator Both(string mine, string theirs, string name)
    {
        yield return Attune(grid, Net, mine);
        yield return Ask("attune", theirs);
        if (Theirs == null) yield break;
        yield return Wait(Settle);
        yield return Reach(name, Net.NetTick);
        if (Theirs == null) yield break;
        string wanted = Core();
        Metric(name + "_wizards", wanted.Count(c => c == ' ') + 1);
        Check(name + "_two_wizards", wanted.Count(c => c == ' ') == 1, wanted);
        Check(name + "_here", Mine.hands == wanted, $"{Mine.hands}, the core has {wanted}");
        Check(name + "_on_the_other_peer", Theirs.hands == wanted, $"{Theirs.hands}, the core has {wanted}");
    }
}
