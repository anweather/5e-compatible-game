using Chem;
using UnityEngine;

// A wizard's health, stamina and magic, for the bars on the HUD (EventLog).
// Health's maximum is the wizard's hit points in rules/entities.json; stamina
// and magic are out of 100. Health drops when something hurts the wizard
// (Hurt: thorns underfoot, SurfaceEffects); a dodge roll asks for a little
// stamina (TrySpendStamina), which comes back over time; magic doesn't drain
// yet. While `infinite` is on (the sandbox) health never drops below 1 and
// comes back a few seconds after the last hurt, and stamina stays full.
// Spells that catch a wizard hurt it too (TakeDamage, SpellForces), and an
// animal shape takes that first (Shapeshift: when the animal drops to 0 the
// wizard changes back, and what's left over carries on to the wizard).
public class Vitals : MonoBehaviour
{
    public const float StaminaMax = 100f, MagicMax = 100f;
    /// <summary>Stamina that comes back each second.</summary>
    public const float StaminaRegen = 12f;
    // The sandbox's health coming back: after this long unhurt, this much a second.
    const float RecoverAfter = 4f, RecoverPerSecond = 2f;

    /// <summary>The sandbox: nothing runs out.</summary>
    public bool infinite = true;

    public float Health { get; private set; } = 12f;
    public float HealthMax { get; private set; } = 12f;
    public float Stamina { get; private set; } = StaminaMax;
    public float Magic { get; private set; } = MagicMax;

    float lastHurt = float.NegativeInfinity;

    /// <summary>Takes damage before the wizard does, and returns what's left over (Shapeshift's animal form).</summary>
    public System.Func<float, float> Absorb { get; set; }

    void Start()
    {
        var grid = FindFirstObjectByType<ChemGrid>();
        var wizard = grid != null && grid.World != null ? grid.World.Rules.Entity(EntityIds.Wizard) : null;
        if (wizard != null) HealthMax = Mathf.Max(1, wizard.Hp);
        Health = HealthMax;
    }

    /// <summary>
    /// Takes stamina for an effort (a dodge roll, DodgeRoll); false, taking nothing, if there isn't
    /// that much left. Free while infinite.
    /// </summary>
    public bool TrySpendStamina(float amount)
    {
        if (infinite) return true;
        if (Stamina < amount) return false;
        Stamina -= amount;
        return true;
    }

    /// <summary>Gives back up to <paramref name="amount"/> health (scripts and the probe).</summary>
    public void Heal(float amount) => Health = Mathf.Min(HealthMax, Health + Mathf.Max(0f, amount));

    /// <summary>Takes <paramref name="amount"/> health away (in the sandbox, never the last point).</summary>
    public void Hurt(float amount)
    {
        if (amount <= 0f) return;
        Health = Mathf.Max(infinite ? 1f : 0f, Health - amount);
        lastHurt = Time.time;
    }

    void Update()
    {
        if (!infinite && Stamina < StaminaMax) Stamina = Mathf.Min(StaminaMax, Stamina + StaminaRegen * Time.deltaTime);
        if (!infinite || Health >= HealthMax || Time.time - lastHurt < RecoverAfter) return;
        Health = Mathf.Min(HealthMax, Health + RecoverPerSecond * Time.deltaTime);
    }

    /// <summary>The wizard is hurt: an animal shape takes it first; the rest comes off health unless it's infinite.</summary>
    public void TakeDamage(float amount)
    {
        if (amount <= 0f) return;
        if (Absorb != null) amount = Absorb(amount);
        Hurt(amount);
    }
}
