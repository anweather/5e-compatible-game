using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The spelllooks probe's second half (UX pass 3, 2026-10-30), the casts the
// rules gained that day, each staged on its own patch and framed for its
// pictures: Geyser, Wave, Frost, Rolling Boulder, vines, the stepping stones and
// the ramp, Lightning through water to a coil and its gate, the storm and snow
// clouds, Sand Blast's scour, Fireball's debris, lava running and steam rusting.
// CastLooksProbe (castlooks) runs these alone.
public partial class SpellLooksProbe
{
    protected IEnumerator Casts()
    {
        yield return Geyser(Spot(9, 7));
        yield return WaveRoll(Spot(14, 5));
        yield return Frosted(Spot(11, 5));
        yield return Boulder(Spot(14, 5));
        yield return Vines(Spot(9, 7));
        yield return Stones(Spot(13, 9));
        yield return BoltToGate(Spot(13, 7));
        yield return StormCloud(Spot(7, 7));
        yield return SnowCloud(Spot(7, 7));
        yield return Scour(Spot(10, 5));
        yield return Debris(Spot(9, 9));
        yield return LavaRun(Spot(12, 3));
        yield return SteamRust(Spot(6, 4));
        yield return IceOnAMound(Spot(7, 7));
        yield return FrozenRaider(Spot(5, 5));
    }

    // A pond: a pit full of water.
    void Pond(Vector2Int o, int w, int h, int water = 8)
    {
        Patch(o, w, h, TerrainIds.Pit);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) World.At(o.x + x, o.y + y).Water = water;
    }

    static PhysicsProp NearestCrate(Vector3 to) => FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None)
        .Where(p => p.kind == EntityIds.Crate && p.State != null)
        .OrderBy(p => Vector3.Distance(p.transform.position, to)).FirstOrDefault();

    static void Drop(PhysicsProp prop, Vector3 at)
    {
        var body = prop.GetComponent<Rigidbody>();
        body.position = at;
        prop.transform.position = at;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    IEnumerator Frame(Vector2Int o, int w, int h, Vector3 from, float wizardBack = 6f)
    {
        yield return Noon();
        var mid = Mid(o, w, h);
        var stand = grid.CellAt(mid + V(-wizardBack, 0f, -1.5f));
        PlaceCell(stand.x, stand.y);
        puppet.Face(mid);
        Cut(mid + from, mid + Vector3.up * 0.6f);
        yield return Wait(0.3f);
    }

    IEnumerator Geyser(Vector2Int o)
    {
        Pond(o, 9, 7);
        yield return Frame(o, 9, 7, V(-9f, 3.5f, -11f), 8f);
        var mid = Mid(o, 9, 7);
        var crate = NearestCrate(mid);
        Check("geyser_crate", crate != null, "a crate to throw");
        if (crate != null) Drop(crate, mid + Vector3.up * 0.8f);
        yield return Wait(0.7f);
        var hit = new Vector2Int(o.x + 4, o.y + 3);
        int water = World.At(hit.x, hit.y).Water;
        float before = crate != null ? crate.transform.position.y : 0f;
        Cast(SpellIds.Geyser, hit.x, hit.y, 1);
        for (float t = 0f; t < 1.5f && World.At(hit.x, hit.y).Water >= water; t += Time.deltaTime) yield return null;
        yield return Wait(0.25f);
        Check("geyser_boils", World.At(hit.x, hit.y).Water < water, $"water {water} then {World.At(hit.x, hit.y).Water}");
        yield return Picture("geyser-jet");
        float highest = 0f;
        for (float t = 0f; t < 1f; t += Time.deltaTime)
        {
            if (crate != null) highest = Mathf.Max(highest, crate.transform.position.y - before);
            yield return null;
        }
        Metric("geyser_throw_m", highest);
        Check("geyser_throws", highest > 1f, $"the crate rose {highest:0.0} m");
        yield return Picture("geyser-falling");
        yield return Wait(1.5f);
        // The wizard goes up too, on its own feet.
        float feet = wizard.transform.position.y, top = 0f;
        wizard.GetComponent<SurfaceEffects>().Toss(9f);
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            top = Mathf.Max(top, wizard.transform.position.y - feet);
            yield return null;
        }
        Metric("geyser_wizard_m", top);
        Check("geyser_throws_wizard", top > 1.5f, $"rose {top:0.0} m");
        yield return Wait(1f);
    }

    IEnumerator WaveRoll(Vector2Int o)
    {
        Patch(o, 14, 5, TerrainIds.Dirt);
        for (int y = 0; y < 3; y++) World.Apply(o.x + 9, o.y + 1 + y, "fire", 3, -1, false);
        yield return Frame(o, 14, 5, V(-8f, 3.2f, -11f), 8.5f);
        var crate = NearestCrate(Mid(o, 14, 5));
        var start = grid.CellCenter(o.x + 4, o.y + 2);
        if (crate != null) Drop(crate, new Vector3(start.x, grid.GroundAt(start) + 0.5f, start.z));
        yield return Wait(0.6f);
        float crateFrom = crate != null ? crate.transform.position.x : 0f;
        Cast(SpellIds.Wave, o.x, o.y + 2, 1);
        yield return Wait(0.8f);
        Check("wave_rolls", World.Waves.Count == 1, $"{World.Waves.Count} waves");
        yield return Picture("wave-rolling");
        yield return Wait(0.5f);
        yield return Picture("wave-carrying");
        for (float t = 0f; t < 6f && World.Waves.Count > 0; t += Time.deltaTime) yield return null;
        Check("wave_breaks", World.Waves.Count == 0, "it ran out");
        yield return Wait(0.2f);
        yield return Picture("wave-break");
        if (crate != null)
        {
            float went = crate.transform.position.x - crateFrom;
            Metric("wave_carried_m", went);
            Check("wave_carries", went > 1.5f, $"the crate went {went:0.0} m");
        }
        yield return Wait(0.8f);
        int mud = Enumerable.Range(0, 70).Count(i => World.At(o.x + i % 14, o.y + i / 14).T == TerrainIds.Mud);
        Metric("wave_mud_cells", mud);
        Check("wave_mud", mud > 0, $"{mud} cells of mud");
        yield return Picture("wave-mud");
    }

    IEnumerator Frosted(Vector2Int o)
    {
        Patch(o, 11, 5, TerrainIds.Dirt);
        Pond(new Vector2Int(o.x + 2, o.y), 3, 5, 3);
        var raider = World.SpawnRaider(RaiderIds.BoneHound, o.x + 6, o.y + 2, 30);
        raider.Status["wet"] = 120;
        yield return Frame(o, 11, 5, V(-3f, 4f, -9f), 8f);
        Cast(SpellIds.Frost, o.x, o.y + 2, 1);
        yield return Wait(0.25f);
        yield return Picture("frost-beam");
        yield return Wait(1.1f);
        Check("frost_bridge", World.At(o.x + 3, o.y + 2).Frozen, $"water {World.At(o.x + 3, o.y + 2).Water}");
        Check("frost_cover", World.At(o.x + 1, o.y + 2).Cover == CoverIds.Frost, World.At(o.x + 1, o.y + 2).Cover ?? "no cover");
        Check("frost_slips", World.IsSlippery(World.At(o.x + 1, o.y + 2)), "frost is slippery");
        Check("frost_freezes_wet", raider.StatusOf(World.Rules.Sim.ShatterWhen) > 0, "the wet raider");
        yield return Picture("frost-bridge");
        Cut(grid.CellCenter(o.x + 6, o.y + 2) + V(-2f, 2f, -3f) + Vector3.up * grid.SurfaceY(o.x + 6, o.y + 2), grid.CellCenter(o.x + 6, o.y + 2) + Vector3.up * (grid.SurfaceY(o.x + 6, o.y + 2) + 0.5f));
        yield return Wait(0.2f);
        yield return Picture("frost-raider");
        raider.Hp = 0;
    }

    IEnumerator Boulder(Vector2Int o)
    {
        Patch(o, 14, 5, TerrainIds.Dirt);
        Patch(new Vector2Int(o.x + 8, o.y + 1), 1, 3, TerrainIds.Glass);
        yield return Frame(o, 14, 5, V(-3f, 4f, -11f), 8.5f);
        Cast(SpellIds.RollingBoulder, o.x, o.y + 2, 1);
        yield return Wait(0.9f);
        Check("boulder_rolls", World.Boulders.Count == 1, $"{World.Boulders.Count} boulders");
        yield return Picture("boulder-rolling");
        yield return Wait(0.6f);
        yield return Picture("boulder-dust");
        for (float t = 0f; t < 6f && World.Boulders.Count > 0; t += Time.deltaTime) yield return null;
        Check("boulder_rests", World.Boulders.Count == 0, "it stopped");
        Check("boulder_cracks_glass", World.At(o.x + 8, o.y + 2).Cracked != 0 || World.At(o.x + 8, o.y + 2).T != TerrainIds.Glass, World.At(o.x + 8, o.y + 2).T);
        yield return Wait(0.8f);
        yield return Picture("boulder-rest");
    }

    IEnumerator Vines(Vector2Int o)
    {
        Patch(o, 9, 7, TerrainIds.Dirt);
        Patch(new Vector2Int(o.x + 1, o.y + 4), 7, 1, TerrainIds.Stone);
        Pond(new Vector2Int(o.x + 3, o.y + 1), 3, 2, 2);
        yield return Frame(o, 9, 7, V(-5f, 3.6f, -8f), 8f);
        Cast(SpellIds.PlantGrowth, o.x + 4, o.y + 3, 1);
        yield return Wait(0.8f);
        var wall = new Vector2Int(o.x + 4, o.y + 4);
        Check("vines_on_wall", World.At(wall.x, wall.y).Cover == CoverIds.Vines, World.At(wall.x, wall.y).Cover ?? "no cover");
        Check("vines_on_pit", World.At(o.x + 4, o.y + 2).Cover == CoverIds.Vines, World.At(o.x + 4, o.y + 2).Cover ?? "no cover");
        yield return Picture("vines-wall");
        Cut(Mid(o, 9, 7) + V(-2f, 2.6f, -4.5f), Mid(o, 9, 7) + V(0f, 0f, 1f));
        yield return Wait(0.2f);
        yield return Picture("vines-close");
        // Walking into them climbs them.
        var foot = grid.CellCenter(wall.x, wall.y - 1);
        PlaceCell(wall.x, wall.y - 1);
        yield return Wait(0.3f);
        var far = grid.CellCenter(wall.x, wall.y + 1);
        float low = wizard.transform.position.y, high = low;
        var walk = StartCoroutine(puppet.WalkTo(new Vector3(far.x, low, far.z)));
        for (float t = 0f; t < 2.5f; t += Time.deltaTime)
        {
            high = Mathf.Max(high, wizard.transform.position.y);
            yield return null;
        }
        StopCoroutine(walk);
        puppet.Move(Vector2.zero);
        Metric("vines_climbed_m", high - low);
        Check("vines_climb", high - low > 0.9f, $"up {high - low:0.0} m at {foot}");
        yield return Wait(0.3f);
        // They die: the roots break the stone they held.
        for (int x = 1; x < 8; x++) World.At(o.x + x, o.y + 4).Life = 2;
        yield return Wait(1f);
        Check("roots_break", World.At(wall.x, wall.y).T == TerrainIds.Rubble, World.At(wall.x, wall.y).T);
        yield return Picture("vines-rubble");
    }

    IEnumerator Stones(Vector2Int o)
    {
        Patch(o, 13, 9, TerrainIds.Dirt);
        Pond(new Vector2Int(o.x + 3, o.y), 6, 4, 8);
        yield return Frame(o, 13, 9, V(-4f, 4.5f, -9f), 9f);
        Cast(SpellIds.WallOfStone, o.x + 3, o.y + 2, 1);
        yield return Wait(1.4f);
        Check("stones_a_step", World.At(o.x + 4, o.y + 2).Height == 1, $"height {World.At(o.x + 4, o.y + 2).Height}");
        yield return Picture("stepping-stones");
        var ramp = grid.CellCenter(o.x + 5, o.y + 6) + Vector3.up * grid.SurfaceY(o.x + 5, o.y + 6);
        Cut(ramp + V(-6f, 2.4f, -5.5f), ramp + Vector3.up * 0.4f);
        Cast(SpellIds.StoneRamp, o.x + 3, o.y + 6, 1);
        yield return Wait(1.4f);
        Check("ramp_climbs", World.At(o.x + 6, o.y + 6).Height > World.At(o.x + 3, o.y + 6).Height, $"{World.At(o.x + 3, o.y + 6).Height} up to {World.At(o.x + 6, o.y + 6).Height}");
        yield return Picture("stone-ramp");
    }

    IEnumerator BoltToGate(Vector2Int o)
    {
        Patch(o, 13, 7, TerrainIds.Dirt);
        for (int x = 3; x < 11; x++)
        {
            World.At(o.x + x, o.y + 3).Water = 3;
            World.At(o.x + x, o.y + 4).Water = 3;
        }
        World.Paint(o.x + 11, o.y + 3, TerrainIds.Coil);
        for (int y = 0; y < 7; y++) World.Paint(o.x + 12, o.y + y, TerrainIds.Gate);
        yield return Frame(o, 13, 7, V(-7f, 5.5f, -7f), 8f);
        Cut(Mid(o, 13, 7) + V(-8f, 6f, -8f), Mid(o, 13, 7) + V(3f, 0f, 0f));
        yield return Picture("bolt-gate-shut");
        Check("gate_shut", !World.GatesOpen, "before the bolt");
        // Aimed at the bare ground by the puddle: it jumps to the water.
        Cast(SpellIds.LightningBolt, o.x + 1, o.y + 3, 1);
        yield return Wait(0.3f);
        yield return Picture("bolt-jump");
        yield return Wait(0.15f);
        yield return Picture("bolt-charge");
        for (float t = 0f; t < 3f && !World.GatesOpen; t += Time.deltaTime) yield return null;
        Check("gate_opens", World.GatesOpen, $"coil charge {World.At(o.x + 11, o.y + 3).Charge}");
        yield return Wait(0.9f);
        yield return Picture("bolt-gate-open");
        for (int y = 3; y < 5; y++)
            for (int x = 3; x < 11; x++) World.At(o.x + x, o.y + y).Water = 0;
    }

    IEnumerator StormCloud(Vector2Int o)
    {
        Patch(o, 7, 7, TerrainIds.Dirt);
        yield return Frame(o, 7, 7, V(-11f, 1.8f, -12f), 6f);
        Cut(Mid(o, 7, 7) + V(-11f, 1.8f, -12f), Mid(o, 7, 7) + Vector3.up * 3.5f);
        var hit = new Vector2Int(o.x + 3, o.y + 3);
        Cast(SpellIds.RainCloud, hit.x, hit.y, 1);
        yield return Wait(1.5f);
        Over(hit, "lightning");
        yield return Wait(0.6f);
        Check("storm_cloud", World.At(hit.x, hit.y).Gas == GasIds.Storm, World.At(hit.x, hit.y).Gas ?? "no gas");
        yield return Picture("storm-cloud");
        int since = World.Tick;
        for (float t = 0f; t < 8f && !World.ShedStrikesSince(since).Any(); t += Time.deltaTime) yield return null;
        Check("storm_strikes", World.ShedStrikesSince(since).Any(), "a bolt from the cloud");
        yield return Wait(0.05f);
        yield return Picture("storm-strike");
    }

    // An element over all of a cloud, as a charged bolt or a blast of cold across it would be.
    void Over(Vector2Int hit, string element)
    {
        for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++) World.Apply(hit.x + x, hit.y + y, element, 1, -1, false);
    }

    IEnumerator SnowCloud(Vector2Int o)
    {
        Patch(o, 7, 7, TerrainIds.Dirt);
        yield return Frame(o, 7, 7, V(-11f, 1.8f, -12f), 6f);
        Cut(Mid(o, 7, 7) + V(-11f, 1.8f, -12f), Mid(o, 7, 7) + Vector3.up * 3.5f);
        var hit = new Vector2Int(o.x + 3, o.y + 3);
        Cast(SpellIds.RainCloud, hit.x, hit.y, 1);
        yield return Wait(1.5f);
        Over(hit, "cold");
        yield return Wait(2.4f);
        Check("snow_cloud", World.At(hit.x, hit.y).Gas == GasIds.SnowCloud, World.At(hit.x, hit.y).Gas ?? "no gas");
        yield return Picture("snow-cloud");
    }

    IEnumerator Scour(Vector2Int o)
    {
        Patch(o, 10, 5, TerrainIds.Dirt);
        Patch(new Vector2Int(o.x + 3, o.y + 1), 3, 3, TerrainIds.RustedMetal);
        for (int y = 1; y < 4; y++) World.Paint(o.x + 7, o.y + y, TerrainIds.LogWall);
        yield return Frame(o, 10, 5, V(-3f, 4f, -8f), 8f);
        yield return Picture("scour-before");
        Cast(SpellIds.SandBlast, o.x, o.y + 2, 1);
        yield return Wait(0.3f);
        yield return Picture("scour-blast");
        yield return Wait(1f);
        Check("scour_rust", World.At(o.x + 4, o.y + 2).T == TerrainIds.Metal, World.At(o.x + 4, o.y + 2).T);
        Check("scour_wall", World.At(o.x + 7, o.y + 2).T == TerrainIds.WornLogWall, World.At(o.x + 7, o.y + 2).T);
        yield return Picture("scour-after");
        Cast(SpellIds.SandBlast, o.x, o.y + 2, 1);
        yield return Wait(1f);
        Check("scour_through", World.At(o.x + 7, o.y + 2).T == TerrainIds.Rubble, World.At(o.x + 7, o.y + 2).T);
    }

    IEnumerator Debris(Vector2Int o)
    {
        Patch(o, 9, 9, TerrainIds.Dirt);
        yield return Frame(o, 9, 9, V(-7f, 5f, -9f), 9f);
        Cast(SpellIds.Fireball, o.x + 4, o.y + 4, 1);
        for (float t = 0f; t < 2f && World.At(o.x + 4, o.y + 4).Fire == 0; t += Time.deltaTime) yield return null;
        yield return Wait(0.1f);
        yield return Picture("fireball-burst");
        yield return Wait(1.2f);
        int embers = Enumerable.Range(0, 81).Count(i => World.At(o.x + i % 9, o.y + i / 9).Cover == CoverIds.Embers);
        Metric("fireball_embers", embers);
        Check("fireball_debris", embers > 0, $"{embers} cells of burning debris");
        yield return Picture("fireball-debris");
        PutOutFires();
    }

    IEnumerator LavaRun(Vector2Int o)
    {
        Patch(o, 12, 3, TerrainIds.Dirt);
        // A slope down to the east, into a puddle at its foot.
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 12; x++) World.At(o.x + x, o.y + y).Height = 3 - x / 2;
        for (int y = 0; y < 3; y++)
            for (int x = 10; x < 12; x++) World.At(o.x + x, o.y + y).Water = 3;
        yield return Frame(o, 12, 3, V(-9f, 6f, -7f), 8f);
        Cast(SpellIds.LavaPool, o.x + 1, o.y + 1, 1);
        yield return Wait(1.5f);
        yield return Picture("lava-running");
        yield return Wait(3f);
        int crust = Enumerable.Range(0, 36).Count(i => World.At(o.x + i % 12, o.y + i / 12).T == TerrainIds.Rock);
        Metric("lava_crust_cells", crust);
        Check("lava_crusts", crust > 0, $"{crust} cells of crust");
        yield return Picture("lava-crust");
    }

    // Ice over a mound: the crack follows it.
    IEnumerator IceOnAMound(Vector2Int o)
    {
        Patch(o, 7, 7, TerrainIds.Dirt);
        for (int y = 0; y < 7; y++)
            for (int x = 0; x < 7; x++)
            {
                var c = World.At(o.x + x, o.y + y);
                c.Height = Mathf.Max(0, 2 - Mathf.Max(Mathf.Abs(x - 3), Mathf.Abs(y - 3)) + 1);
                c.Water = 3;
                c.Frozen = true;
            }
        yield return Frame(o, 7, 7, V(-3f, 3f, -4f), 7f);
        foreach (var (x, y) in new[] { (2, 2), (3, 2), (4, 3), (2, 4), (3, 4), (4, 4), (3, 3), (2, 3), (4, 2) })
            ChemEffects.Request(grid, new EffectCommand(EffectKind.Shatter, o.x + x, o.y + y, "", ThingId.None));
        yield return Wait(0.5f);
        yield return Picture("crack-ice-mound");
    }

    IEnumerator SteamRust(Vector2Int o)
    {
        Patch(o, 6, 4, TerrainIds.Metal);
        yield return Frame(o, 6, 4, V(-3f, 2.8f, -4f), 6f);
        int every = World.TerrainOf(World.At(o.x, o.y)).RustEvery;
        // Part of the way there, more to the east, then steam over it.
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 6; x++) World.At(o.x + x, o.y + y).Rust = every * (x + 1) / 8;
        yield return Wait(0.4f);
        yield return Picture("rust-partway");
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 6; x++)
            {
                World.At(o.x + x, o.y + y).Rust = every - 3;
                World.At(o.x + x, o.y + y).DampUntil = World.Tick + 600; // it counts on wet metal
            }
        Cast(SpellIds.ScaldingSteam, o.x + 2, o.y + 2, 1);
        for (float t = 0f; t < 8f && World.At(o.x + 2, o.y + 2).T != TerrainIds.RustedMetal; t += Time.deltaTime) yield return null;
        yield return Wait(0.3f);
        Check("steam_rusts", World.At(o.x + 2, o.y + 2).T == TerrainIds.RustedMetal, $"{World.At(o.x + 2, o.y + 2).T}, rust {World.At(o.x + 2, o.y + 2).Rust} of {every}");
        yield return Picture("rust-steam");
    }

    // A frozen raider, whole and cracked: the web on its ice.
    IEnumerator FrozenRaider(Vector2Int o)
    {
        Patch(o, 5, 5, TerrainIds.Dirt);
        yield return Frame(o, 5, 5, V(-1.6f, 1.2f, -2.2f), 6f);
        var raider = World.SpawnRaider(RaiderIds.BoneHound, o.x + 2, o.y + 2, 30);
        raider.Status[World.Rules.Sim.ShatterWhen] = 300;
        raider.Status[World.CrackedStatus] = 300;
        yield return Wait(0.5f);
        yield return Picture("crack-frozen-web");
        raider.Hp = 0;
    }

    void Cast(string spell, int x, int y, int dir) => caster.Cast(spell, x, y, dir, 1);
}

/// <summary>The spelllooks probe's new casts alone, for working on them.</summary>
public class CastLooksProbe : SpellLooksProbe
{
    protected override IEnumerator Run()
    {
        yield return Casts();
    }
}
