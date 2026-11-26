using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's sleet scenario (docs/FAST_LOOP.md): Sleet Storm cast from the
// bar on the meadow east of the yard, as a player casts it (SpellCaster,
// SleetStorm). First a Fire Bolt sets the middle of it alight and puddles are
// laid round the fire, so there's something for the storm to do. Checks that
// a storm (a cloud and sleet, not a projectile or a landing effect) rages over
// the spell's squares, that the sleet falls and lands on them, that the
// chemistry waits for it to reach the ground and then freezes the puddles and
// puts out the fire, and that the storm dies away after. Shots of it raging
// and of the ice after.
public class SleetProbe : ProbeScenario
{
    const int TargetX = 14, TargetY = 3;

    readonly ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[6000];
    readonly List<Cell> puddles = new();
    List<(int x, int y)> squares;
    Vector2Int target;
    Vector3 at;
    int flyingBefore;

    protected override IEnumerator Run()
    {
        Place(4, 10);
        target = grid.YardCell(TargetX, TargetY);
        at = Yard(TargetX, TargetY);
        at.y = grid.GroundAt(at);
        squares = World.SpellCells(World.Rules.Spell(SpellIds.SleetStorm), target.x, target.y, 1);
        // From behind the wizard, high enough to see the cloud over the storm and the ground under it.
        var back = wizard.transform.position - at;
        back.y = 0f;
        Cut(at + back.normalized * 24f + V(0f, 8f, 0f), at + V(0f, 3f, 0f));

        yield return CastFromBar(SpellIds.FireBolt, TargetX, TargetY, 1, 1.5f);
        LayPuddles();
        int burning = Burning();
        Metric("burning_before", burning);
        Metric("puddles_laid", puddles.Count);
        Check("something_to_freeze", burning > 0 && puddles.Count >= 20, $"{burning} squares burning, {puddles.Count} puddles laid");

        // Half a second in: the storm is gathering, nothing has landed yet.
        float castAt = Time.time;
        flyingBefore = Flying(); // the Fire Bolt's, which lingers a moment after it lands
        yield return CastFromBar(SpellIds.SleetStorm, TargetX, TargetY, 1, 0.5f);
        var storm = FindAnyObjectByType<SleetStorm>();
        CheckShow(storm);
        Check("not_landed_yet", squares.All(s => !World.At(s.x, s.y).Frozen), "the puddles wait for the sleet to reach the ground");
        if (storm == null) yield break;

        // Well in: the sleet falls on the spell's squares, and they froze and went out when it landed.
        yield return Wait(1.1f);
        yield return CheckFalling(storm);
        CheckGround();
        yield return Wait(0.6f);
        Metric("strength_raging", storm.Strength);
        Shot("storm");

        // It dies away: weaker near its end, gone a few seconds after.
        yield return Wait(castAt + SleetStorm.Seconds - 0.7f - Time.time);
        float fading = storm != null ? storm.Strength : 0f;
        Metric("strength_near_end", fading);
        Check("storm_fades", fading > 0f && fading < 0.9f, $"strength {fading:0.00} near its end");
        yield return Wait(castAt + SleetStorm.Seconds + 5.5f - Time.time);
        bool gone = storm == null; // Unity's null check: destroyed
        Check("storm_ends", gone, gone ? "the storm is gone" : "the storm is still there");
        Shot("after");
    }

    // Puddles on every other dry, open square of the storm that isn't burning (round the fire).
    void LayPuddles()
    {
        foreach (var (x, y) in squares)
        {
            var c = World.At(x, y);
            if (((x + y) & 1) == 0 || c.Water > 0 || c.Fire > 0 || World.IsBlocking(c)) continue;
            c.Water = 1;
            puddles.Add(c);
        }
    }

    static int Flying() =>
        FindObjectsByType<SpellProjectile>(FindObjectsSortMode.None).Length + FindObjectsByType<MeteorFall>(FindObjectsSortMode.None).Length;

    int Burning() => squares.Count(s => World.At(s.x, s.y).Fire > 0);

    // A storm over the target, with its cloud and sleet, and nothing flying or landing like a ball.
    void CheckShow(SleetStorm storm)
    {
        Check("storm_shown", storm != null && storm.Cloud != null && storm.Sleet != null && storm.Pellets != null,
            storm != null ? "a Sleet Storm with its cloud, sleet and pellets" : "no SleetStorm");
        if (storm != null)
        {
            var off = storm.Center - at;
            off.y = 0f;
            float want = (World.Rules.Spell(SpellIds.SleetStorm).Radius + 0.5f) * grid.cellSize;
            Metric("storm_half_width_m", storm.HalfWidth);
            Check("storm_over_target", off.magnitude < 0.5f && Mathf.Abs(storm.HalfWidth - want) < 0.01f,
                $"{off.magnitude:0.00} m from the target, {storm.HalfWidth:0.00} m each way (the burst's {want:0.00} m)");
            Check("cloud_overhead", storm.Cloud.transform.position.y > at.y + 6f, $"the cloud {storm.Cloud.transform.position.y - at.y:0.0} m up");
        }
        bool projectile = Flying() > flyingBefore;
        bool lance = GameObject.Find("IceLance(Clone)") != null;
        Check("no_projectile", !projectile && !lance, $"projectile {(projectile ? "flew" : "none")}, ice impact {(lance ? "played" : "none")}");
    }

    // The sleet: plenty of it, moving down from one frame to the next, and what's near the ground is over the squares.
    IEnumerator CheckFalling(SleetStorm storm)
    {
        var sleet = storm.Sleet;
        int n = sleet.GetParticles(buffer);
        var heights = new Dictionary<uint, float>();
        for (int i = 0; i < n; i++) heights[buffer[i].randomSeed] = buffer[i].position.y;
        Metric("sleet_particles", n);
        Metric("pellet_particles", storm.Pellets.particleCount);
        Metric("cloud_particles", storm.Cloud.particleCount);
        yield return null;
        yield return null;
        n = sleet.GetParticles(buffer);
        int matched = 0, fell = 0, low = 0, lowOver = 0;
        float reach = storm.HalfWidth + 0.75f;
        for (int i = 0; i < n; i++)
        {
            var p = buffer[i].position;
            if (heights.TryGetValue(buffer[i].randomSeed, out float before))
            {
                matched++;
                if (p.y < before - 0.01f) fell++;
            }
            if (p.y > at.y + 2f) continue;
            low++;
            if (Mathf.Abs(p.x - at.x) <= reach && Mathf.Abs(p.z - at.z) <= reach) lowOver++;
        }
        Check("sleet_falls", n > 500 && matched > 100 && fell >= matched * 0.95f && storm.Cloud.particleCount > 10,
            $"{n} streaks, {fell} of {matched} seen twice moved down, {storm.Pellets.particleCount} pellets, {storm.Cloud.particleCount} cloud billows");
        Check("sleet_lands_on_squares", low > 50 && lowOver >= low * 0.9f, $"{lowOver} of {low} streaks near the ground are over the spell's squares");
    }

    // What the rules say it does: the water in its squares freezes (wherever the puddles ran to) and the flames go out.
    void CheckGround()
    {
        int wet = squares.Count(s => World.At(s.x, s.y).Water > 0);
        int frozen = squares.Count(s => World.At(s.x, s.y).Water > 0 && World.At(s.x, s.y).Frozen);
        int burning = Burning();
        Metric("wet_squares", wet);
        Metric("puddles_frozen", frozen);
        Metric("burning_after", burning);
        Check("puddles_froze", wet >= 20 && frozen == wet, $"{frozen} of {wet} wet squares frozen ({puddles.Count} puddles laid)");
        Check("fire_out", burning == 0, $"{burning} squares still burning");
    }
}
