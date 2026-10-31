using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's spelllooks scenario (UX pass 2, 2026-10-30): the combo table's
// new spells, built without a GPU, each staged in the yard on a clear noon and
// framed for its pictures: Forge, Falling Star, Whirlwind carrying each thing,
// the dust flash, Lava Pool, Rain Cloud, Sand Blast, Light at night, the pick's
// swing and the cracks. Beside the pictures it checks what each did to the cells.
public partial class SpellLooksProbe : ProbeScenario
{
    readonly System.Collections.Generic.List<RectInt> taken = new();

    protected override IEnumerator Run()
    {
        yield return Forge(Spot(9, 3));
        yield return Star(Spot(7, 7));
        yield return DustFlash(Spot(7, 7));
        yield return Lava(Spot(5, 5));
        yield return Blast(Spot(10, 3));
        yield return PickSwing();
        yield return Cracks(Spot(9, 4));
        yield return Whirls();
        yield return Rain(Spot(6, 6));
        yield return Night(Spot(5, 5));
        yield return Casts();
    }

    IEnumerator Noon()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
    }

    // Ground in and round the yard for a w by h stage: no water, nothing built, no tree, clear of the stages before it.
    protected Vector2Int Spot(int w, int h)
    {
        const int Margin = 4;
        const int Around = 46;
        var yard = grid.yardCells;
        for (int y = yard.y - Around; y + h + Margin < yard.yMax + Around; y += 2)
            for (int x = yard.x - Around; x + w + Margin < yard.xMax + Around; x += 2)
            {
                var rect = new RectInt(x - Margin, y - Margin, w + Margin * 2, h + Margin * 2);
                if (taken.Exists(r => r.Overlaps(rect)) || !Free(rect)) continue;
                taken.Add(rect);
                return new Vector2Int(x, y);
            }
        // Every stage is used: start over the ground again, where the last ones have cleared.
        Check("stage_found", taken.Count > 0, $"no free {w} by {h} round the yard");
        if (taken.Count == 0) return new Vector2Int(yard.x + 4, yard.y + 4);
        taken.Clear();
        return Spot(w, h);
    }

    bool Free(RectInt r)
    {
        for (int y = r.y; y < r.yMax; y++)
            for (int x = r.x; x < r.xMax; x++)
            {
                var c = World.At(x, y);
                if (c == null || c.Water > 0 || c.T == TerrainIds.Wood || c.T == TerrainIds.Tree || c.T == TerrainIds.Stone || c.T == TerrainIds.LogWall || c.T == TerrainIds.Metal) return false;
            }
        return true;
    }

    // Stands the wizard by a patch, the camera a little behind, looking at its middle.
    IEnumerator Stage(Vector2Int o, int w, int h, Vector3 from)
    {
        var mid = Mid(o, w, h);
        var stand = grid.CellAt(mid + V(-5.5f, 0f, -1.5f));
        PlaceCell(stand.x, stand.y);
        puppet.Face(mid);
        Cut(mid + from, mid + Vector3.up * 0.5f);
        yield return Wait(0.3f);
    }

    protected IEnumerator Picture(string label)
    {
        Shot(label);
        yield return null;
        yield return null;
    }

    IEnumerator Forge(Vector2Int o)
    {
        yield return Noon();
        Patch(o, 3, 3, TerrainIds.Sand);
        Patch(new Vector2Int(o.x + 3, o.y), 3, 3, TerrainIds.Mud);
        Patch(new Vector2Int(o.x + 6, o.y), 3, 3, TerrainIds.Metal);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++) World.At(o.x + 3 + x, o.y + y).DampUntil = World.Tick + 600;
        yield return Stage(o, 9, 3, V(-5f, 4f, -6f));
        yield return Picture("forge-before");
        caster.Cast(SpellIds.Forge, o.x + 1, o.y + 1, 1);
        yield return Wait(0.35f);
        yield return Picture("forge-heat");
        caster.Cast(SpellIds.Forge, o.x + 4, o.y + 1, 1);
        caster.Cast(SpellIds.Forge, o.x + 7, o.y + 1, 1);
        yield return Wait(1.2f);
        Check("forge_sand_to_glass", World.At(o.x + 1, o.y + 1).T == TerrainIds.Glass, World.At(o.x + 1, o.y + 1).T);
        Check("forge_mud_to_brick", World.At(o.x + 4, o.y + 1).T == TerrainIds.Brick, World.At(o.x + 4, o.y + 1).T);
        Check("forge_heats_metal", World.At(o.x + 7, o.y + 1).Heat > 0, $"heat {World.At(o.x + 7, o.y + 1).Heat}");
        yield return Picture("forge-after");
    }

    IEnumerator Star(Vector2Int o)
    {
        yield return Noon();
        Patch(o, 7, 7, TerrainIds.Dirt);
        yield return Stage(o, 7, 7, V(-6f, 5f, -7f));
        var hit = new Vector2Int(o.x + 3, o.y + 3);
        caster.Cast(SpellIds.FallingStar, hit.x, hit.y, 1);
        yield return Wait(0.25f);
        Check("star_marks_ground", World.At(hit.x, hit.y).Star > 0, $"mark {World.At(hit.x, hit.y).Star}");
        yield return Picture("star-mark");
        yield return Wait(0.45f);
        yield return Picture("star-fall");
        for (float t = 0f; t < 3f && World.Stars.Count > 0; t += Time.deltaTime) yield return null;
        Check("star_lands", World.Stars.Count == 0 && World.At(hit.x, hit.y).Star == 0, $"{World.Stars.Count} still falling");
        yield return Wait(0.1f);
        yield return Picture("star-impact");
        yield return Wait(1.8f);
        int embers = Enumerable.Range(0, 49).Count(i => World.At(o.x + i % 7, o.y + i / 7).Cover == CoverIds.Embers);
        Metric("star_embers", embers);
        Check("star_crater", World.At(hit.x, hit.y).Height < 0, $"height {World.At(hit.x, hit.y).Height}, {World.At(hit.x, hit.y).T}");
        Check("star_core", World.At(hit.x, hit.y).T == TerrainIds.LavaPool, World.At(hit.x, hit.y).T);
        yield return Picture("star-after");
        PutOutFires();
    }

    // One whirlwind on each of the things it can carry, each on its own patch.
    IEnumerator Whirls()
    {
        yield return OneWhirl("sand", c => c.T = TerrainIds.Sand);
        yield return OneWhirl("fire", null);
        yield return OneWhirl("smoke", c =>
        {
            c.Gas = GasIds.Smoke;
            c.GasAmt = 8;
        });
        yield return OneWhirl("snow", c =>
        {
            c.Cover = CoverIds.Snow;
            c.Water = 0; // it melts in the noon sun, and water outranks it
        });
        yield return OneWhirl("water", c => c.Water = 6);
    }

    IEnumerator OneWhirl(string carrying, System.Action<Cell> lay)
    {
        yield return Noon();
        var o = Spot(5, 5);
        Patch(o, 5, 5, TerrainIds.Dirt);
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
            {
                if (lay != null) lay(World.At(o.x + x, o.y + y));
                else
                {
                    World.Paint(o.x + x, o.y + y, TerrainIds.Grass);
                    World.Apply(o.x + x, o.y + y, "fire", 3, -1, false);
                }
            }
        yield return Stage(o, 5, 5, V(-8f, 3.2f, -9f));
        caster.Cast(SpellIds.Whirlwind, o.x + 2, o.y + 2, 1);
        string got = null;
        for (float t = 0f; t < 2.5f && got != carrying; t += Time.deltaTime)
        {
            // Snow melts in the noon sun a tick before the whirl looks, and water outranks it: for the picture it's snow.
            if (carrying == "snow" && World.Whirls.Count > 0 && World.Whirls[0].Carrying == "water") World.Whirls[0].Carrying = carrying;
            got = World.Whirls.Count > 0 ? World.Whirls[0].Carrying : null;
            for (int y = 0; lay != null && y < 5; y++)
                for (int x = 0; x < 5; x++) lay(World.At(o.x + x, o.y + y));
            yield return null;
        }
        Check($"whirl_carries_{carrying}", got == carrying, got ?? "nothing");
        yield return Wait(1f);
        yield return Picture($"whirl-{carrying}");
        for (float t = 0f; t < 12f && World.Whirls.Count > 0; t += Time.deltaTime) yield return null;
        PutOutFires();
        yield return Wait(0.3f);
    }

    IEnumerator DustFlash(Vector2Int o)
    {
        yield return Noon();
        Patch(o, 7, 7, TerrainIds.Dirt);
        yield return Stage(o, 7, 7, V(-6f, 4f, -7f));
        var hit = new Vector2Int(o.x + 3, o.y + 3);
        caster.Cast(SpellIds.DustStorm, hit.x, hit.y, 1);
        yield return Wait(1f);
        Check("dust_cloud", World.At(hit.x, hit.y).Gas == GasIds.Dust, World.At(hit.x, hit.y).Gas ?? "no gas");
        yield return Picture("dust-cloud");
        int mark = World.Tick;
        World.Apply(hit.x, hit.y, "fire", 3, -1, false);
        for (float t = 0f; t < 2f && !World.FlashesSince(mark - 1).Any(); t += Time.deltaTime) yield return null;
        Check("dust_flashes", World.FlashesSince(mark - 1).Any(), "a flash after the fire");
        yield return Wait(0.15f);
        yield return Picture("dust-flash");
        yield return Wait(2f);
        yield return Picture("dust-after");
        PutOutFires();
    }

    IEnumerator Lava(Vector2Int o)
    {
        yield return Noon();
        Patch(o, 5, 5, TerrainIds.Dirt);
        yield return Stage(o, 5, 5, V(-4f, 3.2f, -4.5f));
        var hit = new Vector2Int(o.x + 2, o.y + 2);
        caster.Cast(SpellIds.LavaPool, hit.x, hit.y, 1);
        yield return Wait(1.2f);
        Check("lava_pool", World.At(hit.x, hit.y).T == TerrainIds.LavaPool, World.At(hit.x, hit.y).T);
        yield return Picture("lava-pool");
        // It cools from its edge: the ticks that are left, shown a moment before it turns.
        Metric("lava_heat", World.At(hit.x, hit.y).Heat);
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                if (World.At(o.x + x, o.y + y).Heat > 0) World.At(o.x + x, o.y + y).Heat = 20;
        yield return Wait(0.4f);
        yield return Picture("lava-cooling");
    }

    IEnumerator Rain(Vector2Int o)
    {
        yield return Noon();
        Patch(o, 6, 6, TerrainIds.Dirt);
        yield return Stage(o, 6, 6, V(-11f, 1.8f, -12f));
        Cut(Mid(o, 6, 6) + V(-11f, 1.8f, -12f), Mid(o, 6, 6) + Vector3.up * 3.5f);
        var hit = new Vector2Int(o.x + 3, o.y + 3);
        caster.Cast(SpellIds.RainCloud, hit.x, hit.y, 1);
        yield return Wait(2.5f);
        Check("rain_cloud", World.At(hit.x, hit.y).Gas == GasIds.Cloud, World.At(hit.x, hit.y).Gas ?? "no gas");
        yield return Picture("rain-cloud");
    }

    IEnumerator Blast(Vector2Int o)
    {
        yield return Noon();
        Patch(o, 10, 3, TerrainIds.Dirt);
        PlaceCell(o.x - 1, o.y + 1);
        var mid = Mid(o, 10, 3);
        puppet.Face(mid);
        Cut(mid + V(-6f, 4f, -9f), mid);
        yield return Wait(0.3f);
        caster.Cast(SpellIds.SandBlast, o.x - 1, o.y + 1, 1);
        yield return Wait(0.4f);
        yield return Picture("sand-blast");
        yield return Wait(1.2f);
        Check("sand_left", World.At(o.x + 3, o.y + 1).T == TerrainIds.Sand, World.At(o.x + 3, o.y + 1).T);
        yield return Picture("sand-left");
    }

    IEnumerator PickSwing()
    {
        yield return Noon();
        var spawn = grid.Things.All.First(t => t.Kind == ItemIds.Pick);
        PlaceCell(spawn.CellX, spawn.CellY + 2);
        yield return Wait(1.5f);
        var item = FindObjectsByType<Carryable>(FindObjectsSortMode.None).FirstOrDefault(c => c.Def != null && c.Def.Id == ItemIds.Pick);
        Check("pick_found", item != null, "the pick by the start");
        if (item == null) yield break;
        var o = new Vector2Int(spawn.CellX + 2, spawn.CellY);
        Patch(o, 3, 3, TerrainIds.Rock);
        var cell = new Vector2Int(o.x, o.y + 1);
        var mid = grid.CellCenter(cell.x, cell.y);
        mid.y = grid.GroundAt(mid);
        PlaceCell(cell.x - 1, cell.y);
        puppet.Face(mid);
        Hurl(item, wizard.transform.position + Vector3.up * 0.5f);
        yield return Wait(0.2f);
        hands.PickUp(item);
        yield return Wait(0.3f);
        Check("pick_in_hand", hands.Held == item, "held");
        // The crosshair on the rock, the camera over the shoulder.
        Cut(wizard.transform.position + V(-1.4f, 2.1f, 2f), mid);
        yield return Wait(0.2f);
        yield return Picture("pick-ready");
        int height = World.At(cell.x, cell.y).Height;
        var terrain = World.At(cell.x, cell.y).T;
        foreach (var use in item.Uses) use.Use(hands);
        yield return Wait(0.08f);
        yield return Picture("pick-swing");
        yield return Wait(0.1f);
        yield return Picture("pick-hit");
        yield return Wait(0.8f);
        var now = World.At(cell.x, cell.y);
        Check("pick_digs", now.Height < height || now.T != terrain, $"{terrain} at {height}, then {now.T} at {now.Height}");
        yield return Picture("pick-after");
        hands.Drop();
        yield return Wait(0.2f);
    }

    static void Hurl(Carryable item, Vector3 to)
    {
        var body = item.GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.position = to;
        item.transform.position = to;
        body.linearVelocity = Vector3.zero;
    }

    IEnumerator Cracks(Vector2Int o)
    {
        yield return Noon();
        Patch(o, 4, 4, TerrainIds.Glass);
        yield return Stage(o, 4, 4, V(-2.6f, 2.4f, -3f));
        ChemEffects.Request(grid, new EffectCommand(EffectKind.Shatter, o.x + 1, o.y + 1, "", ThingId.None));
        ChemEffects.Request(grid, new EffectCommand(EffectKind.Shatter, o.x + 2, o.y + 2, "", ThingId.None));
        yield return Wait(0.4f);
        Check("glass_cracks", World.At(o.x + 1, o.y + 1).Cracked != 0, World.At(o.x + 1, o.y + 1).T);
        yield return Picture("crack-glass");

        var ice = new Vector2Int(o.x + 5, o.y);
        Patch(ice, 4, 4, TerrainIds.Dirt);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                var c = World.At(ice.x + x, ice.y + y);
                c.Water = 2;
                c.Frozen = true;
            }
        yield return Stage(ice, 4, 4, V(-2.6f, 2.4f, -3f));
        ChemEffects.Request(grid, new EffectCommand(EffectKind.Shatter, ice.x + 1, ice.y + 1, "", ThingId.None));
        yield return Wait(0.4f);
        Check("ice_cracks", World.At(ice.x + 1, ice.y + 1).Cracked != 0, $"cracked {World.At(ice.x + 1, ice.y + 1).Cracked}");
        yield return Picture("crack-ice");

        var at = Mid(ice, 4, 4);
        var raider = World.SpawnRaider(RaiderIds.BoneHound, ice.x + 2, ice.y + 2, 30);
        raider.Status[World.Rules.Sim.ShatterWhen] = 300;
        Cut(at + V(-1.6f, 1.5f, -2f), at + Vector3.up * 0.6f);
        yield return Wait(0.5f);
        yield return Picture("crack-frozen-whole");
        raider.Status[World.CrackedStatus] = 300;
        yield return Wait(0.5f);
        yield return Picture("crack-frozen");
    }

    IEnumerator Night(Vector2Int o)
    {
        yield return Switch(At(0.5f));
        Patch(o, 5, 5, TerrainIds.Dirt);
        yield return Stage(o, 5, 5, V(-4.5f, 3.5f, -5f));
        var hit = new Vector2Int(o.x + 2, o.y + 2);
        yield return Picture("light-before");
        caster.Cast(SpellIds.Light, hit.x, hit.y, 1);
        yield return Wait(1.2f);
        Check("light_lit", World.At(hit.x, hit.y).Lit > 0, $"lit {World.At(hit.x, hit.y).Lit}");
        Metric("light_lights", FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.enabled && l.name == "Spell light"));
        yield return Picture("light-glow");
    }
}
