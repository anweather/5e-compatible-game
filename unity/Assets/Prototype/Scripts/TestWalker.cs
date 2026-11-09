using UnityEngine;

// Walks the wizard it's on to a cell with no one at the keys (TestWalk), for a clip or a test run in Play mode: add it,
// set the cell, and it pushes the wizard's own stick (sprinting) until it's there, then takes itself off.
public sealed class TestWalker : MonoBehaviour
{
    public Vector2Int to;
    public bool sprint = true;

    TestWalk walk;
    Component inputs;
    System.Reflection.FieldInfo move, run, jump;

    /// <summary>Whether it has arrived (or found no way).</summary>
    public bool Done { get; private set; }

    void Start()
    {
        inputs = GetComponent(WizardRig.InputsComponent);
        move = WizardRig.Field(inputs, "move", isPublic: true);
        run = WizardRig.Field(inputs, "sprint", isPublic: true);
        jump = WizardRig.Field(inputs, "jump", isPublic: true);
    }

    void Update()
    {
        var grid = ChemGrid.Current?.Grid;
        if (Done || grid == null || move == null || Camera.main == null) return;
        walk ??= TestWalk.To(grid, grid.CellAt(transform.position), to);
        if (walk == null || walk.Done)
        {
            Stop();
            return;
        }
        move.SetValue(inputs, walk.Stick(grid, transform, Camera.main.transform, Time.deltaTime, out bool hop));
        run?.SetValue(inputs, sprint);
        jump?.SetValue(inputs, hop);
    }

    void Stop()
    {
        Done = true;
        move.SetValue(inputs, Vector2.zero);
        run?.SetValue(inputs, false);
        jump?.SetValue(inputs, false);
        Destroy(this);
    }
}
