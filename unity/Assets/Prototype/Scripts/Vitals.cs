using Chem;
using UnityEngine;

// A wizard's health, stamina and magic, for the bars on the HUD (EventLog).
// Health's maximum is the wizard's hit points in rules/entities.json; stamina
// and magic are out of 100. Health drops when something hurts the wizard
// (Hurt: thorns underfoot, SurfaceEffects); a dodge roll asks for a little
// stamina (TrySpendStamina), which comes back over time; magic doesn't drain
// yet. While `infinite` is on (the sandbox) health comes back a few seconds
// after the last hurt and stamina stays full. At 0 health the wizard is downed
// (Downed, WizardDown), and a few seconds later it's back at the yard, full.
// Spells that catch a wizard hurt it too (TakeDamage, SpellForces), and an
// animal shape takes that first (Shapeshift: when the animal drops to 0 the
// wizard changes back, and what's left over carries on to the wizard).
// Breath (Breath, sim.json breath) runs down while the head is under water
// (SurfaceEffects says when, Breathe) and drowning hurts through TakeDamage.
// Like the rest it is the wizard's own, kept on the machine that moves it.
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

    /// <summary>The wizard's breath under water (null until Start has read the rules).</summary>
    public Breath Breath { get; private set; }

    float lastHurt = float.NegativeInfinity;
    Downed down = new(null);

    /// <summary>Health ran out: the wizard is down (WizardDown).</summary>
    public event System.Action Fell;
    /// <summary>The wizard is up again, full: its time down is over, or an online reset ended it.</summary>
    public event System.Action Rose;
    /// <summary>Its time down is over: it comes back at the yard, just after <see cref="Rose"/>.</summary>
    public event System.Action Respawned;

    /// <summary>The wizard is down, and takes no more harm until it's back.</summary>
    public bool IsDown => down.IsDown;

    /// <summary>Takes damage before the wizard does, and returns what's left over (Shapeshift's animal form).</summary>
    public System.Func<float, float> Absorb { get; set; }

    void Start()
    {
        var grid = FindAnyObjectByType<ChemGrid>();
        var wizard = grid != null && grid.World != null ? grid.World.Rules.Entity(EntityIds.Wizard) : null;
        if (wizard != null) HealthMax = Mathf.Max(1, wizard.Hp);
        Breath = new Breath(grid != null && grid.World != null ? grid.World.Rules.Sim.Breath : null, sides => Random.Range(1, sides + 1));
        down = new Downed(grid != null && grid.World != null ? grid.World.Rules.Sim.Downed : null);
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
    /// <summary>Everything full again (an online reset: OnlineReset).</summary>
    public void Refill()
    {
        Health = HealthMax;
        Stamina = StaminaMax;
        Magic = MagicMax;
        Breath?.Refill();
        lastHurt = float.NegativeInfinity;
        if (down.Clear()) Rose?.Invoke();
    }

    public void Heal(float amount) => Health = Mathf.Min(HealthMax, Health + Mathf.Max(0f, amount));

    /// <summary>Takes <paramref name="amount"/> health away; at none left the wizard falls.</summary>
    public void Hurt(float amount)
    {
        if (amount <= 0f || IsDown) return;
        Health = Mathf.Max(0f, Health - amount);
        lastHurt = Time.time;
        if (Health <= 0f && down.Fall()) Fell?.Invoke();
    }

    void Update()
    {
        if (down.Step(Time.deltaTime))
        {
            Refill();
            Respawned?.Invoke();
            return;
        }
        if (!infinite && Stamina < StaminaMax) Stamina = Mathf.Min(StaminaMax, Stamina + StaminaRegen * Time.deltaTime);
        if (IsDown || !infinite || Health >= HealthMax || Time.time - lastHurt < RecoverAfter) return;
        Health = Mathf.Min(HealthMax, Health + RecoverPerSecond * Time.deltaTime);
    }

    /// <summary>
    /// A frame of breathing, the head under water or not (SurfaceEffects): out of breath, the wizard
    /// drowns, hurt every few seconds until its head comes out.
    /// </summary>
    public void Breathe(bool headUnder, float seconds)
    {
        if (Breath == null || IsDown) return;
        bool was = Breath.Drowning;
        int damage = Breath.Step(seconds, headUnder);
        if (Breath.Drowning && !was) EventLog.Add("You run out of breath and start to drown");
        if (damage <= 0) return;
        EventLog.Add($"You choke on water: {damage} damage");
        TakeDamage(damage);
    }

    /// <summary>The wizard is hurt: an animal shape takes it first; the rest comes off health.</summary>
    public void TakeDamage(float amount)
    {
        if (amount <= 0f || IsDown) return;
        if (Absorb != null) amount = Absorb(amount);
        Hurt(amount);
    }
}
