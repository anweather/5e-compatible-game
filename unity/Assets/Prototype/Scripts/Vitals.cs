using Chem;
using UnityEngine;

// A wizard's health, stamina and magic, for the bars on the HUD (EventLog).
// Health's maximum is the wizard's hit points in rules/entities.json; stamina
// and magic are out of 100. Nothing drains them yet: while `infinite` is on
// (the sandbox) they stay full, and the bars show what they will track once
// damage, sprinting and climbing, and spell costs are wired in.
public class Vitals : MonoBehaviour
{
    public const float StaminaMax = 100f, MagicMax = 100f;

    /// <summary>The sandbox: nothing runs out.</summary>
    public bool infinite = true;

    public float Health { get; private set; } = 12f;
    public float HealthMax { get; private set; } = 12f;
    public float Stamina { get; private set; } = StaminaMax;
    public float Magic { get; private set; } = MagicMax;

    void Start()
    {
        var grid = FindFirstObjectByType<ChemGrid>();
        var wizard = grid != null && grid.World != null ? grid.World.Rules.Entity(EntityIds.Wizard) : null;
        if (wizard != null) HealthMax = Mathf.Max(1, wizard.Hp);
        Health = HealthMax;
    }
}
