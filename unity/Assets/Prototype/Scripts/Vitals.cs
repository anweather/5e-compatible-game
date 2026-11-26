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
// Like the rest it is the wizard's own, kept on the machine that moves it. The
// dragon's breath downs a wizard whatever its health (KnockDown): WizardDown asks
// the core, on the owner's machine only.
public class Vitals : MonoBehaviour
{
    public const float StaminaMax = 100f, MagicMax = 100f;
    public const float StaminaRegen = 12f;
    const float RecoverAfter = 4f, RecoverPerSecond = 2f;

    public bool infinite = true;

    public float Health { get; private set; } = 12f;
    public float HealthMax { get; private set; } = 12f;
    float baseHealth = 12f;
    public float Stamina { get; private set; } = StaminaMax;
    public float Magic { get; private set; } = MagicMax;

    public int ExtraHealth { get; private set; }
    public float HealthGrewAt { get; private set; } = float.NegativeInfinity;

    public void SetExtraHealth(int extra)
    {
        float before = HealthMax, maximum = baseHealth + extra;
        if (Mathf.Approximately(before, maximum)) return;
        ExtraHealth = extra;
        if (maximum > before) HealthGrewAt = Time.unscaledTime;
        HealthMax = maximum;
        Health = Mathf.Clamp(Health + maximum - before, 1f, maximum);
    }

    public Breath Breath { get; private set; }

    float lastHurt = float.NegativeInfinity;
    Downed down = new(null);

    public event System.Action Fell;
    public event System.Action Rose;
    public event System.Action Respawned;

    public bool IsDown => down.IsDown;

    public string FellLine { get; private set; } = HealthRanOut;

    const string HealthRanOut = "You fall, out of health";

    public System.Func<float, float> Absorb { get; set; }

    void Start()
    {
        var rules = ChemGrid.Current?.Rules;
        var wizard = rules?.Entity(EntityIds.Wizard);
        if (wizard != null) HealthMax = baseHealth = Mathf.Max(1, wizard.Hp);
        Breath = new Breath(rules?.Sim.Breath, sides => Random.Range(1, sides + 1));
        down = new Downed(rules?.Sim.Downed);
        Health = HealthMax;
    }

    public bool TrySpendStamina(float amount)
    {
        if (infinite) return true;
        if (Stamina < amount) return false;
        Stamina -= amount;
        return true;
    }

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

    public void Hurt(float amount)
    {
        if (amount <= 0f || IsDown) return;
        Health = Mathf.Max(0f, Health - amount);
        lastHurt = Time.time;
        if (Health <= 0f) GoDown(HealthRanOut);
    }

    public void KnockDown(string line)
    {
        if (IsDown) return;
        Health = 0f;
        GoDown(line);
    }

    void GoDown(string line)
    {
        if (!down.Fall()) return;
        FellLine = line;
        Fell?.Invoke();
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

    public void TakeDamage(float amount)
    {
        if (amount <= 0f || IsDown) return;
        if (Absorb != null) amount = Absorb(amount);
        Hurt(amount);
    }
}
