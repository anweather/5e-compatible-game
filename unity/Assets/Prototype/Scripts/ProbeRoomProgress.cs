using System.Collections;
using System.Linq;
using Chem;
using Unity.Netcode;
using UnityEngine;

// The probe's roomprogress scenario (docs/design/the-hold.md, phase 5): two peers with different points in one run. This Editor
// hosts and plays from its save (F7), so it has what its points unlock; the virtual player is a dev build's everything. Both attune,
// the run starts, and each peer must see both wizards' hands as the core has them; wave 1 falls to the host's Fireballs, cast as
// any player casts; the host picks an upgrade, the joiner's is picked for it as wave 2 starts, and every checksum agrees.
// Nothing here is in the game.
public class RoomProgressProbe : RoomProbe
{
    const float MostWait = 15f, WaveWait = 180f, CastEvery = 0.8f, Settle = 6f;

    protected override ChangeMode Mode => ChangeMode.Open;
    protected override bool StartsWithAMod => false;

    protected override IEnumerator Steps()
    {
        var world = grid.World;
        var keeper = grid.GetComponent<ProgressKeeper>();
        var me = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().GetComponent<SpellCaster>();
        string host = me.CastPlayer;
        keeper.PlayFromTheSave(true);
        for (float t = 0f; t < MostWait && world.PointsOf(host) != keeper.Saved; t += Time.unscaledDeltaTime) yield return null;
        Metric("host_points", keeper.Saved);
        Check("the_host_plays_from_its_save", world.PointsOf(host) == keeper.Saved, $"{world.PointsOf(host)} of {keeper.Saved}");

        yield return HandLooksProbe.Attune(grid, Net, "-,fire");
        yield return Ask("attune", "water,air");
        if (Theirs == null) yield break;
        string joiner = world.Stands.Keys.FirstOrDefault(p => p != host);
        Check("the_joiner_has_everything", joiner != null && world.PointsOf(joiner) == World.AllPoints, $"{joiner}: {(joiner != null ? world.PointsOf(joiner) : null)}");
        Check("the_hosts_left_hand_is_locked", !world.HandOpen(host, HandsDef.Left) && world.HandOpen(joiner ?? "", HandsDef.Left));

        // The host onto the plate, which starts the run (hold-batch: attuning no longer happens in a room round it).
        var plate = world.HoldSite.Plate;
        var at = grid.CellCenter(plate.x, plate.y);
        at.y = grid.GroundAt(at) + 0.05f;
        var body = me.GetComponent<CharacterController>();
        body.enabled = false;
        me.transform.position = at;
        body.enabled = true;
        for (float t = 0f; t < MostWait && !world.HoldRunning; t += Time.unscaledDeltaTime) yield return null;
        Check("a_run_starts", world.HoldRunning);
        if (!world.HoldRunning) yield break;
        HashesFrom = Net.NetTick;
        yield return Wait(1f);
        yield return Reach("attuned", Net.NetTick);
        if (Theirs == null) yield break;
        Check("hands_here", Mine.hands.Contains($"{host}:-:fire") && Mine.hands.Contains($"{joiner}:water:air"), Mine.hands);
        Check("hands_on_the_joiner", Theirs.hands == Mine.hands, $"{Theirs.hands}, here {Mine.hands}");

        // Wave 1 falls to Fireballs at the raiders, each a cast like any player's.
        float cast = 0f;
        for (float t = 0f; t < WaveWait && world.Hold.Wave == 1; t += Time.unscaledDeltaTime)
        {
            cast += Time.unscaledDeltaTime;
            if (cast >= CastEvery && world.Raiders.FirstOrDefault(r => r.Alive) is { } raider)
            {
                cast = 0f;
                me.Cast(SpellIds.Fireball, raider.X, raider.Y, 0);
            }
            yield return null;
        }
        Check("wave_1_is_beaten", world.Hold.Wave == 2, $"wave {world.Hold.Wave}, {world.Raiders.Count} raiders");
        if (world.Hold.Wave != 2) yield break;
        var run = world.RunOf(host);
        yield return null;
        Check("the_host_is_offered", run?.Offer.Count == world.Rules.Sim.Hold.Offer && keeper.OfferText().Length > 0, keeper.OfferText());
        string picked = run?.Offer.FirstOrDefault();
        Check("the_host_picks", keeper.Take(me, run, 0));
        for (float t = 0f; t < MostWait && run.Picks.Count == 0; t += Time.unscaledDeltaTime) yield return null;
        Check("the_pick_is_in", run.Picks.SequenceEqual(new[] { picked }), string.Join(",", run.Picks));

        for (float t = 0f; t < WaveWait && world.Hold.InWave == 0; t += Time.unscaledDeltaTime) yield return null;
        var theirs = world.RunOf(joiner ?? "");
        Check("the_joiner_is_picked_for", theirs != null && theirs.Picks.Count == 1, theirs != null ? string.Join(",", theirs.Picks) : "no run");
        yield return Wait(Settle);
        yield return Reach("wave_2", Net.NetTick);
        if (Theirs == null) yield break;
        CompareHashes("checksums_match_through_the_picks", HashesFrom);
        keeper.PlayFromTheSave(false);
    }
}
