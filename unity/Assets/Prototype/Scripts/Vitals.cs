using Chem;
using UnityEngine;

// A wizard's health, stamina and magic, for the bars on the HUD (EventLog).
// Health's maximum is the wizard's hit points in rules/entities.json; stamina
// and magic are out of 100. While `infinite` is on (the sandbox) they stay
// full; the bars show what they will track once damage, sprinting and
// climbing, and spell costs are wired in. Damage already arrives (TakeDamage,
// from the spells that catch a wizard: SpellForces), and an animal shape
// takes it first (Shapeshift: when the animal drops to 0 the wizard changes
// back, and what's left over carries on to the wizard).
public class Vitals : MonoBehaviour
{
    public const float StaminaMax = 100f, MagicMax = 100f;

    /// <summary>The sandbox: nothing runs out.</summary>
    public bool infinite = true;

    public float Health { get; private set; } = 12f;
    public float HealthMax { get; private set; } = 12f;
    public float Stamina { get; private set; } = StaminaMax;
    public float Magic { get; private set; } = MagicMax;

    /// <summary>Takes damage before the wizard does, and returns what's left over (Shapeshift's animal form).</summary>
    public System.Func<float, float> Absorb { get; set; }

    void Start()
    {
        var grid = FindFirstObjectByType<ChemGrid>();
        var wizard = grid != null && grid.World != null ? grid.World.Rules.Entity(EntityIds.Wizard) : null;
        if (wizard != null) HealthMax = Mathf.Max(1, wizard.Hp);
        Health = HealthMax;
    }

    /// <summary>The wizard is hurt: an animal shape takes it first; the rest comes off health unless it's infinite.</summary>
    public void TakeDamage(float amount)
    {
        if (amount <= 0f) return;
        if (Absorb != null) amount = Absorb(amount);
        if (amount > 0f && !infinite) Health = Mathf.Max(0f, Health - amount);
    }
}
