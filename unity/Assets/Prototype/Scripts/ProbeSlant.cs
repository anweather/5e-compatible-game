using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's slant scenario (docs/FAST_LOOP.md): a line aimed off the grid's
// four ways (SpellCaster's aim, World.SpellCells). Through the wizard's own
// controls on a virtual gamepad, with the crosshair on a spot about 20 degrees
// south of east, Earthen Trench is tapped. Checks the trench follows the aim
// (it steps south as it goes east), its squares each share a side with the
// last (so water can run along it), and they're dug.
public class SlantProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var controls = caster.controls;
        bool controlsWereOn = controls != null && controls.enabled, casterWasOn = caster.enabled;
        if (controls != null) controls.enabled = true;
        caster.enabled = true;
        var pad = InputSystem.AddDevice<Gamepad>("Slant pad");
        try
        {
            Place(4, 10);
            Check("bar_has_earthen_trench", caster.Select(SpellIds.EarthenTrench));
            var me = wizard.transform.position;
            var at = me + new Vector3(8f, 0f, -3f);
            at.y = grid.GroundAt(at);
            Cut(me + V(-4f, 5f, 1.5f), at);
            for (int i = 0; i < 3; i++) yield return null;
            var before = World.Cells.Select(c => c.Height).ToArray();
            InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = 1f });
            for (int i = 0; i < 2; i++) yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Wait(1.5f);

            var dug = Enumerable.Range(0, World.W * World.H).Where(i => World.Cells[i].Height < before[i]).Select(i => (x: i % World.W, y: i / World.W)).ToList();
            Metric("dug_squares", dug.Count);
            int rows = dug.Select(c => c.y).Distinct().Count(), cols = dug.Select(c => c.x).Distinct().Count();
            Metric("rows", rows);
            Metric("columns", cols);
            Check("trench_dug", dug.Count >= 6, $"{dug.Count} squares dug");
            Check("trench_slants", rows >= 2 && cols > rows, $"{cols} columns over {rows} rows");
            var set = dug.ToHashSet();
            bool joined = dug.All(c => dug.Count == 1 || set.Contains((c.x + 1, c.y)) || set.Contains((c.x - 1, c.y)) || set.Contains((c.x, c.y + 1)) || set.Contains((c.x, c.y - 1)));
            Check("squares_share_sides", joined, "every dug square has a dug neighbour beside it, not only at a corner");
            Cut(me + V(4f, 12f, -1.6f), me + V(4.01f, 0f, -1.5f));
            yield return Wait(0.3f);
            Shot("slanted-trench");
        }
        finally
        {
            if (controls != null) controls.enabled = controlsWereOn;
            caster.enabled = casterWasOn;
            InputSystem.RemoveDevice(pad);
        }
    }
}
