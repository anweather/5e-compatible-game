using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

// A wizard at 0 health (#14; Vitals, Downed): it falls where it stands, losing
// its cast, its animal shape and what it carries, and lies there with no
// controls. A few seconds later (sim.json downed) it's back at the yard with
// full health. This isn't the world reset: the world carries on. Online each
// wizard's health is its owner's, so this runs on the owner's machine, and
// NetWizard lays the wizard down on the others (DownedPose).
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
        if (vitals.IsDown && Mine) HoldOff();
        else if (heldOff && (PauseMenu.Instance == null || !PauseMenu.Instance.Open)) GiveBack();
    }

    void Fall()
    {
        if (!Mine) return;
        if (sitter != null) sitter.Stand();
        if (shape != null && shape.Shifted) shape.ChangeBack("You're yourself again", force: true);
        if (hands != null && hands.Held != null) hands.Drop();
        if (caster != null) caster.DropCast();
        HoldOff();
        Lie(true);
        EventLog.Add("You fall, out of health");
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
        if (Online) net.JumpToStart();
        else ToStart();
        EventLog.Add("You come to at the yard, back on your feet");
    }

    void ToStart()
    {
        var grid = caster.grid;
        var start = grid.PlayerStart;
        // The character controller keeps its own idea of where it is: off for the jump.
        bool hasBody = TryGetComponent<CharacterController>(out var body) && body.enabled;
        if (hasBody) body.enabled = false;
        transform.position = grid.CellCenter(start.x, start.y) + Vector3.up * (grid.SurfaceY(start.x, start.y) + 0.1f);
        Physics.SyncTransforms();
        if (hasBody) body.enabled = true;
    }
}
