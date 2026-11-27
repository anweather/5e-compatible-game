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
    public const string Name = "Wizard";

    public const string CameraRoot = "PlayerCameraRoot";

    public const string InputsComponent = "StarterAssetsInputs";
    public const string ControllerComponent = "ThirdPersonController";
    const string PushComponent = "BasicRigidBodyPush";

    static readonly string[] OwnerOnlyByName = { InputsComponent, ControllerComponent, PushComponent };

    public static GameObject FindLocal() => GameObject.Find(Name);

    public static void SetPlayerDriven(GameObject wizard, bool on)
    {
        wizard.GetComponent<CharacterController>().enabled = on;
        wizard.GetComponent<PlayerInput>().enabled = on;
        foreach (var typeName in OwnerOnlyByName)
        {
            if (wizard.GetComponent(typeName) is Behaviour b) b.enabled = on;
        }
    }

    public static void PrepareForAnimationEvents(GameObject wizard)
    {
        var controller = wizard.GetComponent(ControllerComponent);
        var field = Field(controller, "_controller", isPublic: false);
        if (field != null && field.GetValue(controller) == null) field.SetValue(controller, wizard.GetComponent<CharacterController>());
    }

    static readonly (string field, float value)[] Movement =
    {
        ("MoveSpeed", 4.5f), ("SprintSpeed", 7f), ("RotationSmoothTime", 0.08f), ("SpeedChangeRate", 14f),
        ("JumpHeight", 1.3f), ("Gravity", -24f),
    };

    public static void TuneMovement(GameObject wizard)
    {
        var controller = wizard.GetComponent(ControllerComponent);
        foreach (var (name, value) in Movement)
        {
            var field = Field(controller, name, isPublic: true);
            if (field != null) field.SetValue(controller, value);
        }
    }

    public static FieldInfo Field(Component component, string field, bool isPublic)
    {
        if (component == null) return null;
        var visibility = isPublic ? BindingFlags.Public : BindingFlags.NonPublic;
        return component.GetType().GetField(field, BindingFlags.Instance | visibility);
    }
}
