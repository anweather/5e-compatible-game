using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

// What our code knows about the wizard: the Starter Assets third-person
// character (PlayerArmature) with our spellcasting added. One place for its
// names and for reaching into the Starter Assets scripts.
//
// Starter Assets components are reached by name and reflection, not by type,
// so our code compiles before the Asset Store packs are restored
// (Prototype > Restore Asset Packs).
public static class WizardRig
{
    /// <summary>The local player's wizard is called this, solo and online (other players' are "Wizard (player N)").</summary>
    public const string Name = "Wizard";

    /// <summary>The child the follow camera tracks.</summary>
    public const string CameraRoot = "PlayerCameraRoot";

    public const string InputsComponent = "StarterAssetsInputs";
    public const string ControllerComponent = "ThirdPersonController";
    const string PushComponent = "BasicRigidBodyPush";

    // The Starter Assets behaviours that only the owning player runs.
    static readonly string[] OwnerOnlyByName = { InputsComponent, ControllerComponent, PushComponent };

    /// <summary>The local player's wizard in the scene, or null.</summary>
    public static GameObject FindLocal() => GameObject.Find(Name);

    /// <summary>
    /// Switches on or off what makes a wizard player-driven: its CharacterController,
    /// PlayerInput and the Starter Assets input, movement and push scripts. A networked
    /// wizard ships with them off; NetWizard switches them on for the owner.
    /// </summary>
    public static void SetPlayerDriven(GameObject wizard, bool on)
    {
        wizard.GetComponent<CharacterController>().enabled = on;
        wizard.GetComponent<PlayerInput>().enabled = on;
        foreach (var typeName in OwnerOnlyByName)
        {
            if (wizard.GetComponent(typeName) is Behaviour b) b.enabled = on;
        }
    }

    /// <summary>
    /// Readies another player's wizard (a puppet, its controller switched off) for its animation
    /// events. The Animator still sends OnFootstep and OnLand to the switched-off
    /// ThirdPersonController, which plays their sounds at its CharacterController's centre. It
    /// looks that up in Start, which a switched-off controller never runs, so without this a
    /// puppet's landings and footsteps threw a NullReferenceException on every other screen.
    /// </summary>
    public static void PrepareForAnimationEvents(GameObject wizard)
    {
        var controller = wizard.GetComponent(ControllerComponent);
        var field = Field(controller, "_controller", isPublic: false);
        if (field != null && field.GetValue(controller) == null) field.SetValue(controller, wizard.GetComponent<CharacterController>());
    }

    // How the wizard moves: quicker and snappier than the Starter Assets' defaults (a 2 m/s walk,
    // 5.3 m/s sprint), toward Punchy's 6 and 12; kept under about 7 m/s, past which the run
    // animation's feet slide.
    static readonly (string field, float value)[] Movement =
    {
        ("MoveSpeed", 4.5f), ("SprintSpeed", 7f), ("RotationSmoothTime", 0.08f), ("SpeedChangeRate", 14f),
        ("JumpHeight", 1.3f), ("Gravity", -24f),
    };

    /// <summary>Sets the wizard's movement (the builder calls this for both wizards).</summary>
    public static void TuneMovement(GameObject wizard)
    {
        var controller = wizard.GetComponent(ControllerComponent);
        foreach (var (name, value) in Movement)
        {
            var field = Field(controller, name, isPublic: true);
            if (field != null) field.SetValue(controller, value);
        }
    }

    /// <summary>A field of a Starter Assets component, found by name; null if the component or field is missing.</summary>
    public static FieldInfo Field(Component component, string field, bool isPublic)
    {
        if (component == null) return null;
        var visibility = isPublic ? BindingFlags.Public : BindingFlags.NonPublic;
        return component.GetType().GetField(field, BindingFlags.Instance | visibility);
    }
}
