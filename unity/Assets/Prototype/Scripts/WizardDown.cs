using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

// A wizard at 0 health (#14; Vitals, Downed), or caught by the dragon's breath: it
// falls where it stands, losing its cast, its animal shape and what it carries, and
// lies there with no controls. A few seconds later (sim.json downed) it's back at
// the yard with full health, or on its last checkpoint if it fell on a run of the
// Gauntlet (World.CourseRespawn). This isn't the world reset: the world carries on.
// Online each wizard's health is its owner's, so this runs on the owner's machine,
// which alone asks the core whether the breath caught it, and NetWizard lays the
// wizard down on the others (DownedPose). The breath is checked once a tick: the
// band is two ticks deep (sim.json dragon), so no frame short of a third of a
// second can jump a wizard over it.
public class WizardDown : MonoBehaviour
{
    Vitals vitals;
    PlayerControls controls;
    PlayerInput input;
    SpellCaster caster;
    ItemHolder hands;
    Sitter sitter;
    Shapeshift shape;
    DownedPose pose;
    NetWizard net;
    Component inputs;
    FieldInfo move;
    // The controls are off because of the fall, and come back once the menu isn't holding them.
    bool heldOff;
    // The cell it fell on, and the last tick the breath was asked about.
    Vector2Int fellOn;
    int breathTick = -1;

    bool Online => net != null && net.IsSpawned;
    bool Mine => !Online || net.IsOwner;

    void Awake()
    {
        TryGetComponent(out vitals);
        TryGetComponent(out controls);
        TryGetComponent(out input);
        TryGetComponent(out caster);
        TryGetComponent(out hands);
        TryGetComponent(out sitter);
        TryGetComponent(out shape);
        TryGetComponent(out pose);
        TryGetComponent(out net);
        inputs = GetComponent(WizardRig.InputsComponent);
        move = WizardRig.Field(inputs, "move", isPublic: true);
    }

    void OnEnable()
    {
        vitals.Fell += Fall;
        vitals.Rose += Rise;
        vitals.Respawned += BackAtTheYard;
    }

    void OnDisable()
    {
        vitals.Fell -= Fall;
        vitals.Rose -= Rise;
        vitals.Respawned -= BackAtTheYard;
    }

    void Update()
    {
        if (Mine) WatchBreath();
        if (vitals.IsDown && Mine) HoldOff();
        else if (heldOff && (PauseMenu.Instance == null || !PauseMenu.Instance.Open)) GiveBack();
    }

    void WatchBreath()
    {
        var grid = caster != null ? caster.grid : null;
        var world = grid != null ? grid.World : null;
        if (world?.Dragon == null || vitals.IsDown || world.Tick == breathTick) return;
        breathTick = world.Tick;
        var cell = grid.CellAt(transform.position);
        if (world.CaughtByBreath(cell.x, cell.y)) vitals.KnockDown("The dragon's breath knocks you down");
    }

    void Fall()
    {
        if (!Mine) return;
        if (caster != null && caster.grid != null) fellOn = caster.grid.CellAt(transform.position);
        if (sitter != null) sitter.Stand();
        if (shape != null && shape.Shifted) shape.ChangeBack("You're yourself again", force: true);
        if (hands != null && hands.Held != null) hands.Drop();
        if (caster != null) caster.DropCast();
        HoldOff();
        Lie(true);
        EventLog.Add(vitals.FellLine);
    }

    void Rise()
    {
        if (Mine) Lie(false);
    }

    void Lie(bool lying)
    {
        if (pose != null) pose.Lying = lying;
        if (Online) net.Downed(lying);
    }

    // Each frame, as the menu gives the controls back when it closes. The stick's last push stays
    // in the controller's input once PlayerInput is off, so it's cleared too.
    void HoldOff()
    {
        heldOff = true;
        if (controls != null) controls.enabled = false;
        if (input != null) input.enabled = false;
        if (move != null) move.SetValue(inputs, Vector2.zero);
    }

    void GiveBack()
    {
        heldOff = false;
        if (controls != null) controls.enabled = true;
        if (input != null) input.enabled = true;
    }

    void BackAtTheYard()
    {
        if (!Mine) return;
        var grid = caster.grid;
        if (grid.World.HoldRespawn() is { } rise)
        {
            ToSpot(Above(grid, new Vector2Int(rise.x, rise.y)));
            EventLog.Add("You come to in the castle");
            return;
        }
        var checkpoint = grid.World.CourseRespawn(caster.CastPlayer, fellOn.x, fellOn.y);
        if (checkpoint is { } cell)
        {
            ToSpot(Above(grid, new Vector2Int(cell.x, cell.y)));
            EventLog.Add("You come to at your last checkpoint");
            return;
        }
        if (Online) net.JumpToStart();
        else ToSpot(Above(grid, grid.PlayerStart));
        EventLog.Add("You come to at the yard, back on your feet");
    }

    static Vector3 Above(ChemGrid grid, Vector2Int cell) =>
        grid.CellCenter(cell.x, cell.y) + Vector3.up * (grid.SurfaceY(cell.x, cell.y) + 0.1f);

    void ToSpot(Vector3 at)
    {
        if (Online)
        {
            net.JumpTo(at);
            return;
        }
        // The character controller keeps its own idea of where it is: off for the jump.
        bool hasBody = TryGetComponent<CharacterController>(out var body) && body.enabled;
        if (hasBody) body.enabled = false;
        transform.position = at;
        Physics.SyncTransforms();
        if (hasBody) body.enabled = true;
    }
}
