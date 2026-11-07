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
        yield return Casting("different_hands_cast");
        if (Theirs == null) yield break;
        yield return Both("air,water", "-,earth", "with_an_empty_hand");
    }

    /// <summary>
    /// This peer's hands cast as D16 has them, through the host: a held charge let go, both hands held into a merge, then one hand
    /// held while the other's throw fires twice. Both peers at once, with different hands, and the checksums must stay alike.
    /// </summary>
    public static IEnumerator HandCasts(NetChem net)
    {
        net.RequestCombo(ComboCommand.HandDown("", HandsDef.Right));
        yield return new WaitForSecondsRealtime(1.8f);
        net.RequestCombo(ComboCommand.HandUp("", HandsDef.Right));
        yield return new WaitForSecondsRealtime(2.6f);
        net.RequestCombo(ComboCommand.HandDown("", HandsDef.Right));
        yield return new WaitForSecondsRealtime(0.3f);
        net.RequestCombo(ComboCommand.HandDown("", HandsDef.Left));
        yield return new WaitForSecondsRealtime(1.5f);
        net.RequestCombo(ComboCommand.HandUp("", HandsDef.Left));
        net.RequestCombo(ComboCommand.HandUp("", HandsDef.Right));
        yield return new WaitForSecondsRealtime(6f);
        net.RequestCombo(ComboCommand.HandDown("", HandsDef.Left));
        for (int i = 0; i < 2; i++)
        {
            yield return new WaitForSecondsRealtime(0.3f);
            net.RequestCombo(ComboCommand.HandDown("", HandsDef.Right, bumper: true));
            yield return new WaitForSecondsRealtime(0.2f);
            net.RequestCombo(ComboCommand.HandUp("", HandsDef.Right));
            yield return new WaitForSecondsRealtime(2.2f);
        }
        net.RequestCombo(ComboCommand.HandUp("", HandsDef.Left));
        yield return new WaitForSecondsRealtime(2f);
    }

    // Both peers cast at once with different hands; the host hears each one's casts, and both run the same ticks.
    IEnumerator Casting(string name)
    {
        var heard = new FiredCastsHeard();
        heard.StartFrom(grid.World);
        var fired = new System.Collections.Generic.Dictionary<string, int>();
        bool done = false;
        IEnumerator Mine()
        {
            yield return HandCasts(Net);
            done = true;
        }
        StartCoroutine(Mine());
        var asking = Ask("handcasts", null, 20f);
        while (asking.MoveNext())
        {
            foreach (var f in heard.Take(grid.World)) fired[f.Player] = (fired.TryGetValue(f.Player, out int n) ? n : 0) + 1;
            yield return asking.Current;
        }
        for (float t = 0f; t < MostWait && !done; t += Time.unscaledDeltaTime) yield return null;
        yield return Wait(Settle);
        foreach (var f in heard.Take(grid.World)) fired[f.Player] = (fired.TryGetValue(f.Player, out int n) ? n : 0) + 1;
        Metric(name + "_players_casting", fired.Count);
        Check(name + "_both_cast", fired.Count == 2 && fired.Values.All(n => n >= 4), string.Join(", ", fired.Select(p => $"{p.Key}: {p.Value}")));
        if (Theirs == null) yield break;
        yield return Reach(name, Net.NetTick);
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
