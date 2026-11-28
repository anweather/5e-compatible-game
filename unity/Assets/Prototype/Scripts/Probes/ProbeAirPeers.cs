using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using Chem;
using Unity.Netcode;
using UnityEngine;

// The probe's airpeers scenario: the air spells aimed-spells brought in (D16), on two peers. This Editor hosts and the virtual
// player joins; the waves start (a raider joins the checksum only while a defense runs), and the same three raiders are put on
// both peers at the same net tick. Then Lightning at the flyer, Gust of Wind at a raider (tapped from the host, held from the
// client, then a third shove it braces against) and a Whirlwind from the client on another. The checksums must stay equal.
// Nothing here is in the game: no command puts a raider down, so each peer puts them in itself, after the same tick.
public class AirPeersProbe : RoomProbe
{
    const int Row = 462, FlyerX = 104, FlyerY = 446, HoundX = 110, HoundY = 457, OtherX = 117, OtherY = 452;
    const int Hp = 400, StandStill = 600, Lead = 18;
    const float Settle = 1.5f, Lands = 2f;

    protected override ChangeMode Mode => ChangeMode.Open;
    protected override bool StartsWithAMod => false;

    public static void StandAt(ChemGrid grid, int x, int y)
    {
        var wizard = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().gameObject;
        var body = wizard.GetComponent<CharacterController>();
        var at = grid.CellCenter(x, y);
        at.y = grid.GroundAt(at) + 0.05f;
        body.enabled = false;
        wizard.transform.SetPositionAndRotation(at, Quaternion.LookRotation(Vector3.forward));
        body.enabled = true;
    }

    public static IEnumerator SpawnAfter(ChemGrid grid, NetChem net, int tick, Action<bool> done = null)
    {
        bool put = false;
        void On(int t, string hash)
        {
            if (t != tick) return;
            Put(grid.World);
            put = true;
        }
        net.TickApplied += On;
        for (float t = 0f; t < 20f && !put; t += Time.unscaledDeltaTime) yield return null;
        net.TickApplied -= On;
        done?.Invoke(put);
    }

    static void Put(World w)
    {
        foreach (var (kind, x, y) in new[] { (RaiderIds.AshWing, FlyerX, FlyerY), (RaiderIds.BoneHound, HoundX, HoundY), (RaiderIds.BoneHound, OtherX, OtherY) })
            if (w.SpawnRaider(kind, x, y, Hp) is { } r) r.Wait = StandStill;
    }

    public static void CastFrom(string args)
    {
        var a = args.Split(',');
        int N(int i) => int.Parse(a[i], CultureInfo.InvariantCulture);
        NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().GetComponent<SpellCaster>()
            .Cast(new CastCommand(a[0], N(1), N(2), N(3), N(4), N(5), N(6)));
    }

    static string Args(string spell, int x, int y, int dir, int charge, int aimX = 0, int aimY = 0) =>
        string.Join(",", spell, x, y, dir, charge, aimX, aimY);

    Raider At(int x, int y) => World.Raiders.FirstOrDefault(r => r.Alive && r.X == x && r.Y == y);

    protected override IEnumerator Steps()
    {
        yield return Ask("defense");
        if (Theirs == null) yield break;
        Check("waves_run", World.Defense() != null, World.Defense() == null ? "no defense" : World.Defense().Phase.ToString());
        StandAt(grid, HoundX, Row);
        yield return Ask("stand", $"{HoundX + 2},{Row}");
        if (Theirs == null) yield break;
        yield return Wait(Settle);

        int tick = Net.NetTick + Lead;
        bool putHere = false;
        StartCoroutine(SpawnAfter(grid, Net, tick, p => putHere = p));
        yield return Ask("airspawn", tick.ToString(CultureInfo.InvariantCulture), 20f);
        if (Theirs == null) yield break;
        for (float t = 0f; t < 20f && !putHere; t += Time.unscaledDeltaTime) yield return null;
        var flyer = At(FlyerX, FlyerY);
        var hound = At(HoundX, HoundY);
        var other = At(OtherX, OtherY);
        Check("raiders_put_on_both", putHere && flyer != null && hound != null && other != null && World.Flying(flyer),
            $"at net tick {tick}: flyer {flyer != null}, hound {hound != null}, other {other != null}");
        if (flyer == null || hound == null || other == null) yield break;
        int from = Net.NetTick;
        yield return Reach("raiders_down", Net.NetTick);
        if (Theirs == null) yield break;

        StandAt(grid, FlyerX, Row);
        yield return Wait(0.5f);
        int hp = flyer.Hp;
        CastFrom(Args(SpellIds.LightningBolt, FlyerX, Row - 1, 0, 1));
        yield return Wait(Lands);
        Metric("lightning_hurt", hp - flyer.Hp);
        Check("lightning_hits_the_flyer", !flyer.Alive || flyer.Hp < hp, $"{hp - flyer.Hp} hurt");

        yield return Gust(hound);
        if (Theirs == null) yield break;
        yield return Whirlwind(other);
        if (Theirs == null) yield break;

        yield return Wait(11f);
        yield return Reach("air_spells", Net.NetTick);
        if (Theirs == null) yield break;
        CompareHashes("air_checksums_match", from);
        Metric("peers", NetworkManager.Singleton.ConnectedClientsIds.Count);
    }

    IEnumerator Gust(Raider hound)
    {
        StandAt(grid, HoundX, Row);
        yield return Wait(0.5f);
        int y0 = hound.Y;
        CastFrom(Args(SpellIds.GustOfWind, HoundX, Row - 1, 0, 1));
        yield return Wait(Lands);
        int tapped = y0 - hound.Y;
        yield return Ask("aircast", Args(SpellIds.GustOfWind, HoundX, Row - 1, 0, World.MaxCharge));
        if (Theirs == null) yield break;
        yield return Wait(Lands);
        int held = y0 - hound.Y - tapped;
        StandAt(grid, HoundX, hound.Y + 6);
        yield return Wait(0.5f);
        CastFrom(Args(SpellIds.GustOfWind, HoundX, hound.Y + 5, 0, 1));
        yield return Wait(Lands);
        int third = y0 - hound.Y - tapped - held;
        Metric("gust_tapped_cells", tapped);
        Metric("gust_held_cells", held);
        Metric("gust_third_cells", third);
        Check("gust_shoves_then_braces", tapped > 0 && held > 0 && third == 0 && hound.StatusOf(World.ShovedStatus) == 2,
            $"tapped {tapped}, held {held}, third {third} cells; shoved {hound.StatusOf(World.ShovedStatus)}");
    }

    IEnumerator Whirlwind(Raider other)
    {
        var start = new Vector2Int(other.X, other.Y);
        int hp = other.Hp;
        bool lifted = false;
        yield return Ask("aircast", Args(SpellIds.Whirlwind, other.X, other.Y, 0, 1));
        if (Theirs == null) yield break;
        int most = 0;
        for (float t = 0f; t < 6f; t += Time.deltaTime)
        {
            lifted |= other.StatusOf(World.LiftedStatus) > 0;
            most = Mathf.Max(most, Mathf.RoundToInt(Vector2Int.Distance(start, new Vector2Int(other.X, other.Y))));
            yield return null;
        }
        Metric("whirlwind_flung_cells", most);
        Check("whirlwind_lifts_and_flings", lifted && most > 0, $"lifted {lifted}, {most} cells, {hp - other.Hp} hurt");
    }
}
