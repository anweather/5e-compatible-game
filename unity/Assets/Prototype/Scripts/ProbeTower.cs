using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's tower scenario (docs/FAST_LOOP.md): the climbing tower
// south-west of the yard (ValleyTower, TowerCourse, TowerPlatform) and the
// double jump (DoubleJump). Checks the tower is built with platforms of every
// kind, a moving platform moves, a phasing one goes ghostly and solid again on
// its cycle, a collapsing one drops away under the wizard and comes back, a
// double jump goes higher than a single, and reaching the flag after stepping
// off the start pad sets off fireworks and logs the time. Shots from the yard,
// of the collapse, and from the top.
public class TowerProbe : ProbeScenario
{
    const int AtLeastOfEach = 3;
    static readonly string[] KindNames = { "static", "moving", "collapsing", "phasing" };

    TowerCourse course;

    protected override IEnumerator Run()
    {
        course = FindFirstObjectByType<TowerCourse>();
        Check("tower_built", course != null && course.platforms != null && course.platforms.Length > 0, "a Tower with its TowerCourse and platforms");
        if (course == null || course.platforms == null || course.platforms.Length == 0) yield break;
        Counts();
        yield return FromYard();
        yield return Moving();
        yield return Phasing();
        yield return Collapsing();
        yield return Jumps();
        yield return Finish();
    }

    void Counts()
    {
        foreach (TowerPlatform.Kind kind in System.Enum.GetValues(typeof(TowerPlatform.Kind)))
        {
            int n = course.platforms.Count(p => p.kind == kind);
            string label = KindNames[(int)kind];
            Metric($"platforms_{label}", n);
            Check($"has_{label}_platforms", n >= AtLeastOfEach, $"{n} {kind} platforms");
        }
        float tall = course.summit.y - TerrainGround(course.summit);
        Metric("summit_height_m", tall);
        Check("tower_tall", tall > 30f && tall < 45f, $"the summit stands {tall:0.0} m over the ground");
    }

    // From the yard's east side, looking at the tower.
    IEnumerator FromYard()
    {
        var from = V(24f, 8f, 14f);
        Cut(from, course.summit + Vector3.down * 16f);
        yield return Wait(1f);
        Shot("from-yard");
    }

    TowerPlatform First(TowerPlatform.Kind kind) => course.platforms.First(p => p.kind == kind);

    IEnumerator Moving()
    {
        var p = First(TowerPlatform.Kind.Moving);
        var before = p.transform.position;
        float moved = 0f;
        for (float t = 0f; t < 1f; t += Time.deltaTime)
        {
            moved = Mathf.Max(moved, Vector3.Distance(before, p.transform.position));
            yield return null;
        }
        Metric("moving_platform_moved_m", moved);
        Check("moving_platform_moves", moved > 0.1f, $"{p.name} moved up to {moved:0.00} m in 1 s");
    }

    // A whole cycle and a bit: it is solid for part of it and a ghost (no collider) for the rest.
    IEnumerator Phasing()
    {
        var p = First(TowerPlatform.Kind.Phasing);
        var box = p.GetComponent<BoxCollider>();
        bool solid = false, ghost = false;
        int flips = 0;
        bool last = box.enabled;
        for (float t = 0f; t < p.period + 0.5f; t += Time.deltaTime)
        {
            solid |= box.enabled;
            ghost |= !box.enabled;
            if (box.enabled != last) flips++;
            last = box.enabled;
            yield return null;
        }
        Metric("phasing_flips", flips);
        Check("phasing_platform_toggles", solid && ghost, $"{p.name}: solid seen {solid}, ghost seen {ghost}, {flips} switches in {p.period + 0.5f:0.0} s");
    }

    // The wizard put on the lowest collapsing platform: it drops within 1.5 s and is back within 6 s.
    IEnumerator Collapsing()
    {
        var p = First(TowerPlatform.Kind.Collapsing);
        var at = p.transform.position;
        Teleport(new Vector3(at.x, p.Top + 0.05f, at.z));
        Cut(at + (at - course.summit).normalized * 7f + Vector3.up * 3f, at);
        float fell = -1f, back = -1f;
        bool shot = false;
        for (float t = 0f; t < 7f && back < 0f; t += Time.deltaTime)
        {
            if (fell < 0f && !p.Solid) fell = t;
            if (fell >= 0f && !shot && t > fell + 0.4f)
            {
                shot = true;
                Shot("collapse");
            }
            if (fell >= 0f && p.Solid) back = t;
            yield return null;
        }
        Metric("collapse_fell_s", fell);
        Metric("collapse_back_s", back);
        Check("collapsing_platform_falls", fell >= 0f && fell <= 1.5f, $"{p.name} dropped {fell:0.00} s after the wizard landed");
        Check("collapsing_platform_returns", back >= 0f && back <= 6f, $"back {back:0.00} s after the wizard landed");
    }

    // On open ground off the start pad: a single jump, then a jump and a second one near its top.
    IEnumerator Jumps()
    {
        var jumper = wizard.GetComponent<DoubleJump>();
        Check("double_jump_part", jumper != null, "the wizard has a DoubleJump");
        if (jumper == null) yield break;
        var spot = OffThePad();
        Teleport(spot);
        Cut(spot + V(6f, 2f, 0f), spot + Vector3.up * 1.5f);
        yield return Wait(1f);

        float single = 0f, twice = 0f;
        yield return Jump(false, jumper, h => single = h);
        yield return Wait(0.8f);
        bool second = false;
        yield return Jump(true, jumper, h => twice = h, ok => second = ok);
        Metric("single_jump_m", single);
        Metric("double_jump_m", twice);
        Check("second_jump_taken", second, "DoubleJump.AirJump answered in the air");
        Check("double_jump_higher", twice > single + 0.6f, $"single {single:0.00} m, double {twice:0.00} m");
    }

    // Jumps (through the Starter Assets input, as the jump button does) and reports how high the feet rose.
    IEnumerator Jump(bool again, DoubleJump jumper, System.Action<float> peak, System.Action<bool> took = null)
    {
        var input = wizard.GetComponent(WizardRig.InputsComponent);
        float from = wizard.transform.position.y, top = from;
        bool asked = false;
        input.SendMessage("JumpInput", true);
        for (float t = 0f; t < 1.6f; t += Time.deltaTime)
        {
            top = Mathf.Max(top, wizard.transform.position.y);
            if (again && !asked && t >= 0.3f)
            {
                asked = true;
                took?.Invoke(jumper.AirJump());
            }
            yield return null;
        }
        input.SendMessage("JumpInput", false);
        peak(top - from);
    }

    // On the ground 5 m further out from the start pad, away from the tower.
    Vector3 OffThePad()
    {
        var outward = course.startPad - course.summit;
        outward.y = 0f;
        var spot = course.startPad + outward.normalized * 5f;
        spot.y = TerrainGround(spot) + 0.05f;
        return spot;
    }

    // Onto the start pad, off it (the clock starts), then up by the flag: fireworks and the time in the log.
    IEnumerator Finish()
    {
        Teleport(course.startPad + Vector3.up * 0.05f);
        yield return Wait(0.5f);
        Teleport(OffThePad());
        yield return Wait(0.5f);
        Check("clock_starts_off_pad", course.Timing, "stepping off the start pad starts the climb's clock");
        int mark = ConsoleMark;
        var toYard = course.startPad - course.summit;
        toYard.y = 0f;
        toYard.Normalize();
        Teleport(course.summit + toYard * 1.5f + Vector3.up * 0.05f);
        yield return Wait(0.8f);
        Check("finish_fireworks", FindFirstObjectByType<TowerFireworks>() != null, "fireworks over the summit");
        Check("finish_logged", ConsoleSaid("You reached the top in", mark), "\"You reached the top in m:ss!\" in the log");
        Metric("best_time_s", TowerCourse.Best);
        Cut(course.summit + Vector3.up * 3f - toYard * 6f, course.summit + toYard * 60f + Vector3.down * 25f);
        yield return Wait(0.5f);
        Shot("top");
        Cut(course.summit + toYard * 30f + Vector3.up * 6f, course.fireworksAt);
        yield return Wait(0.6f);
        Shot("fireworks");
    }
}
