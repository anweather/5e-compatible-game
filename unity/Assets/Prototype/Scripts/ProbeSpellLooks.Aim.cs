using System.Collections;
using System.Linq;
using Chem;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's aimlooks scenario (UX pass 4, GitHub #2): the aim marker of each
// kind of cast (AimMarker), staged on flat ground, then on water and on a mound,
// one picture each (--shots): lanes at an angle, a ring growing as Cast is held on
// a virtual gamepad, the outlines of what rises, the area of Spike Growth, the
// lock-on on a deer. Checks: each spell's kind, and that one mesh draws it.
public class AimLooksProbe : SpellLooksProbe
{
    Gamepad pad;

    protected override IEnumerator Run()
    {
        var controls = caster.controls;
        bool controlsWereOn = controls.enabled, elementPadWas = controls.ElementPad;
        var lockWas = Cursor.lockState;
        controls.enabled = true;
        caster.enabled = true;
        // The crosshair at the screen's centre, focused or not (Aiming.Ray).
        Cursor.lockState = CursorLockMode.Locked;
        pad = InputSystem.AddDevice<Gamepad>("Aim pad");
        try
        {
            EverySpellHasAKind();
            yield return Lanes(Spot(16, 11));
            yield return Rings(Spot(11, 9));
            yield return Rising(Spot(11, 9));
            yield return OnWater(Spot(13, 11));
            yield return OnAMound(Spot(13, 11));
            yield return FollowsTheCast(Spot(11, 9));
            yield return LockOn();
        }
        finally
        {
            caster.AimOverride = null;
            controls.ElementPad = elementPadWas;
            controls.enabled = controlsWereOn;
            caster.enabled = false;
            Cursor.lockState = lockWas;
            InputSystem.RemoveDevice(pad);
        }
    }

    // Aims a spell at a point through the crosshair's own path, and pictures what it marks.
    IEnumerator Aimed(string spell, Vector3 at, AimKind kind, string label)
    {
        Check($"selects_{spell}", caster.Select(spell));
        caster.AimOverride = at;
        yield return Wait(0.25f);
        Expect(spell, kind);
        yield return Picture(label);
    }

    void Expect(string spell, AimKind kind)
    {
        var marker = caster.Marker;
        var shown = marker?.Shown;
        Check($"marks_{spell}_as_{kind}", marker != null && marker.Kind == kind && shown != null && shown.activeSelf && marker.Vertices > 0, $"{marker?.Kind}, {marker?.Vertices} vertices");
        Metric($"vertices_{spell}", marker?.Vertices ?? 0);
        // The old marker was a cube per cell: nothing like that any more, one mesh and one draw.
        Check($"one_mesh_{spell}", shown != null && shown.GetComponentsInChildren<Renderer>(true).Length == 1 && GameObject.Find("Aim") == null);
    }

    // Every spell in the rules has a marker, and the ones that fit none of the four kinds have one too.
    void EverySpellHasAKind()
    {
        var none = World.Rules.Spells.Where(s => AimMarker.KindOf(s) == AimKind.None && !World.IsSkySpell(s)).Select(s => s.Id).ToList();
        Check("every_spell_has_a_marker", none.Count == 0, string.Join(", ", none));
        foreach (var kind in new[] { AimKind.Lane, AimKind.Wedge, AimKind.Ring, AimKind.Rising, AimKind.Area, AimKind.Lock })
            Metric($"spells_{kind}", World.Rules.Spells.Count(s => AimMarker.KindOf(s) == kind));
    }

    // Casting by elements: the marker follows the cast under way, whatever spell is selected.
    IEnumerator FollowsTheCast(Vector2Int o)
    {
        Patch(o, 11, 9, TerrainIds.Dirt);
        yield return Frame(o, 11, 9, V(-7f, 8f, -9f), 6f);
        caster.Select(SpellIds.Fireball);
        caster.AimOverride = Mid(o, 11, 9);
        caster.LoadElement(ElementIds.Earth, true);
        yield return Wait(0.2f);
        Check("marker_follows_the_element_cast", caster.Marker?.Kind == AimKind.Rising, $"{caster.Marker?.Kind} for Raise Earth, Fireball selected");
        yield return Picture("element-raise-earth");
        caster.LetGo();
        yield return Wait(1.6f);
    }

    IEnumerator Lanes(Vector2Int o)
    {
        Patch(o, 16, 11, TerrainIds.Dirt);
        yield return Frame(o, 16, 11, V(-9f, 9f, -12f), 7.5f);
        var mid = Mid(o, 16, 11);
        yield return Aimed(SpellIds.Wave, mid + V(4f, 0f, 4f), AimKind.Lane, "wave-slant");
        yield return Aimed(SpellIds.RollingBoulder, mid + V(5f, 0f, -3f), AimKind.Lane, "boulder-slant");
        yield return Aimed(SpellIds.LightningBolt, mid + V(6f, 0f, 1.5f), AimKind.Lane, "lightning");
        yield return Aimed(SpellIds.GustOfWind, mid + V(5f, 0f, 6f), AimKind.Lane, "gust");
        yield return Aimed("flame-wave", mid + V(4f, 0f, 2f), AimKind.Wedge, "cone");
    }

    IEnumerator Rings(Vector2Int o)
    {
        Patch(o, 11, 9, TerrainIds.Dirt);
        yield return Frame(o, 11, 9, V(-7f, 8f, -9f), 6f);
        var mid = Mid(o, 11, 9);
        yield return Aimed(SpellIds.FallingStar, mid, AimKind.Ring, "falling-star");
        yield return Aimed(SpellIds.Thunderwave, mid, AimKind.Ring, "thunderwave");
        yield return Aimed(SpellIds.Fireball, mid, AimKind.Ring, "fireball-tap");
        // Cast held on the pad's radial layout: the ring grows a level at a time.
        RadialPad();
        InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = 1f });
        yield return Wait(0.2f);
        float tap = RingRadius();
        float level = (float)World.SecondsPerLevel;
        yield return Wait(level + 0.25f);
        float one = RingRadius();
        yield return Picture("fireball-held");
        yield return Wait(level + 0.25f);
        float two = RingRadius();
        yield return Picture("fireball-held-full");
        Metric("ring_tap", tap);
        Metric("ring_one_level", one);
        Metric("ring_two_levels", two);
        Check("ring_grows_with_charge", tap > 0f && one > tap && two > one, $"{tap:0.00} m, {one:0.00} m, {two:0.00} m");
        // Letting go casts it; the next stage is clear of it.
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return Wait(2.5f);
    }

    float RingRadius() => caster.Marker != null ? caster.Marker.Radius : 0f;

    IEnumerator Rising(Vector2Int o)
    {
        Patch(o, 11, 9, TerrainIds.Dirt);
        yield return Frame(o, 11, 9, V(-7f, 8f, -9f), 6f);
        var mid = Mid(o, 11, 9);
        yield return Aimed(SpellIds.WallOfStone, mid + V(2f, 0f, 2f), AimKind.Rising, "wall-of-stone");
        yield return Aimed(SpellIds.MoldEarthHeap, mid, AimKind.Rising, "raise-earth");
        yield return Aimed(SpellIds.SpikeGrowth, mid, AimKind.Area, "spike-growth");
    }

    IEnumerator OnWater(Vector2Int o)
    {
        Patch(o, 13, 11, TerrainIds.Dirt);
        Pond(new Vector2Int(o.x + 2, o.y + 2), 9, 7, 8);
        yield return Frame(o, 13, 11, V(-8f, 7f, -10f), 9f);
        var mid = Mid(o, 13, 11);
        yield return Aimed(SpellIds.Geyser, mid, AimKind.Rising, "water-geyser");
        yield return Aimed(SpellIds.SpikeGrowth, mid, AimKind.Area, "water-spike-growth");
        yield return Aimed(SpellIds.Fireball, mid, AimKind.Ring, "water-fireball");
        yield return Aimed(SpellIds.Wave, mid + V(5f, 0f, 3f), AimKind.Lane, "water-wave");
    }

    // Steps of raised ground, for a marker to lie over.
    IEnumerator OnAMound(Vector2Int o)
    {
        Patch(o, 13, 11, TerrainIds.Dirt);
        for (int y = 0; y < 11; y++)
            for (int x = 0; x < 13; x++)
                World.At(o.x + x, o.y + y).Height = Mathf.Max(0, 4 - Mathf.Max(Mathf.Abs(x - 6), Mathf.Abs(y - 5)) / 2 * 2 + 1) / 2;
        // The ground eases up to it.
        yield return Wait(1.5f);
        yield return Frame(o, 13, 11, V(-9f, 3.5f, -9f), 9f);
        var mid = Mid(o, 13, 11);
        yield return Aimed(SpellIds.SpikeGrowth, mid, AimKind.Area, "mound-spike-growth");
        yield return Aimed(SpellIds.Fireball, mid, AimKind.Ring, "mound-fireball");
        yield return Aimed(SpellIds.RollingBoulder, mid + V(4f, 0f, 2f), AimKind.Lane, "mound-boulder");
        yield return Aimed(SpellIds.WallOfStone, mid + V(1f, 0f, 2f), AimKind.Rising, "mound-wall");
    }

    // A deer held still 8 m off, the camera on it, as the crosshair sees it.
    IEnumerator LockOn()
    {
        caster.AimOverride = null;
        var fauna = FindAnyObjectByType<ValleyFauna>();
        int deer = fauna != null ? fauna.SpeciesIndex("Deer") : -1;
        Check("deer_found", deer >= 0, $"species {deer}");
        if (deer < 0) yield break;
        var home = V(27f, 0f, 26f);
        Teleport(new Vector3(home.x, grid.GroundAt(home) + 0.05f, home.z));
        yield return Wait(0.5f);
        var at = wizard.transform.position + Vector3.forward * 8f;
        at.y = grid.GroundAt(at);
        int target = fauna.Spawn(deer, new float3(at.x, at.y, at.z), math.PI);
        fauna.Puppet(target, math.PI, 0f);
        puppet.Face(at);
        Cut(wizard.transform.position + V(1f, 3f, -4f), at + Vector3.up * 0.8f);
        caster.Select(SpellIds.Shapeshift);
        yield return Wait(0.4f);
        Expect(SpellIds.Shapeshift, AimKind.Lock);
        yield return Picture("shapeshift-lock");
    }
}
