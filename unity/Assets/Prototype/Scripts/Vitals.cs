using Chem;
using UnityEngine;

// A wizard's health, stamina and magic, for the bars on the HUD (EventLog).
// Health's maximum is the wizard's hit points in rules/entities.json; stamina
// and magic are out of 100. Health drops when something hurts the wizard
// (Hurt: thorns underfoot, SurfaceEffects); stamina and magic don't drain yet.
// While `infinite` is on (the sandbox) health never drops below 1 and comes
// back a few seconds after the last hurt, and the bars show what they will
// track once sprinting and climbing, and spell costs, are wired in.
public class Vitals : MonoBehaviour
{
    public const float StaminaMax = 100f, MagicMax = 100f;
    // The sandbox's health coming back: after this long unhurt, this much a second.
    const float RecoverAfter = 4f, RecoverPerSecond = 2f;

    /// <summary>The sandbox: nothing runs out.</summary>
    public bool infinite = true;

    public float Health { get; private set; } = 12f;
    public float HealthMax { get; private set; } = 12f;
    public float Stamina { get; private set; } = StaminaMax;
    public float Magic { get; private set; } = MagicMax;

    float lastHurt = float.NegativeInfinity;

    void Start()
    {
        var grid = FindFirstObjectByType<ChemGrid>();
        var wizard = grid != null && grid.World != null ? grid.World.Rules.Entity(EntityIds.Wizard) : null;
        if (wizard != null) HealthMax = Mathf.Max(1, wizard.Hp);
        Health = HealthMax;
    }

    /// <summary>Takes <paramref name="amount"/> health away (in the sandbox, never the last point).</summary>
    public void Hurt(float amount)
    {
        if (amount <= 0f) return;
        Health = Mathf.Max(infinite ? 1f : 0f, Health - amount);
        lastHurt = Time.time;
    }

    void Update()
    {
        if (!infinite || Health >= HealthMax || Time.time - lastHurt < RecoverAfter) return;
        Health = Mathf.Min(HealthMax, Health + RecoverPerSecond * Time.deltaTime);
    }
}
