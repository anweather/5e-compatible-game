using Chem;
using UnityEngine;

// A wizard's side of the unarmed strike (World.Strike.cs, J-045), on the machine that
// moves it (its owner's). It tells the core where the wizard stands: a Stand on a new
// cell, or when its health changes, which covers being hurt, going down and getting up,
// and again whenever the world is new (a reset forgets stands). The fist's landing
// (ItemHolder.PunchLanded) sends a Strike along the punch, hit or miss: the core decides
// whom it hits, from its cells. Each frame it takes the core's results, and if this
// wizard is the one struck it takes the damage and is moved to the cell the core says,
// through the controller (Knockback.Shunt), so a wall or a slope it can't take stops it
// here too. Being struck is applied here and nowhere else, so once, on the owner's
// machine; the Stand after a hit puts the core right if the wizard ended up elsewhere or
// an animal shape took the damage. The doors are ApplyStrike solo and NetChem online,
// where the host stamps it into a tick, the same one on every peer. Leaving is the host's
// to send (NetChem.PlayerLeft).
[RequireComponent(typeof(Vitals), typeof(SpellCaster), typeof(ItemHolder))]
public class WizardStrike : MonoBehaviour
{
    // Grid steps a level unit vector is scaled by, for a whole aim.
    const int AimScale = 100;

    SpellCaster caster;
    Vitals vitals;
    Knockback shove;
    NetWizard net;
    CastBeats beats;
    World told;
    Vector2Int cell;
    int health = -1;
    // When to tell the core again however little seems to have changed (-1: no need).
    float tellAt = -1f;

    /// <summary>A strike's result has reached this machine, whoever was in it.</summary>
    public event System.Action<StrikeHit> Struck;
    /// <summary>The hits this wizard has taken, and the health they cost it (a shape's soaking none), for the probe.</summary>
    public int HitsTaken { get; private set; }
    public float DamageTaken { get; private set; }

    bool Mine => net == null || !net.IsSpawned || net.IsOwner;

    void Awake()
    {
        caster = GetComponent<SpellCaster>();
        vitals = GetComponent<Vitals>();
        TryGetComponent(out shove);
        TryGetComponent(out net);
        TryGetComponent(out beats);
        hands = GetComponent<ItemHolder>();
        hands.PunchLanded += landing =>
        {
            fist = landing;
            Strike(landing.Way);
        };
    }

    ItemHolder hands;
    // The last punch that got there: where the fist is when the core says it hit someone.
    ItemHolder.PunchLanding fist;

    /// <summary>Strikes from the wizard's cell along this level way.</summary>
    public void Strike(Vector3 way)
    {
        var grid = caster.grid;
        if (!Mine || grid == null || grid.World == null) return;
        var at = grid.CellAt(transform.position);
        // Cell rows run the other way from the world's z (ChemGrid.CellAt).
        Send(grid, StrikeCommand.Strike(caster.CastPlayer, at.x, at.y, Mathf.RoundToInt(way.x * AimScale), -Mathf.RoundToInt(way.z * AimScale)));
    }

    void Update()
    {
        var grid = caster.grid;
        var world = grid != null ? grid.World : null;
        if (!Mine || world == null) return;
        foreach (var hit in world.TakeStrikeHits())
        {
            if (hit.Hit && hit.TargetPlayer == caster.CastPlayer) Take(hit, grid);
            // This wizard's own punch hit a wizard or a raider: the puncher feels it too (a thing it knocked was felt already).
            else if (hit.Hit && hit.Attacker == caster.CastPlayer && !fist.Hit) hands.FistLanded(Fist(), fist.Way.sqrMagnitude > 0f ? fist.Way : transform.forward);
            Struck?.Invoke(hit);
        }
        var at = grid.CellAt(transform.position);
        int now = Mathf.CeilToInt(vitals.Health);
        bool due = tellAt >= 0f && Time.time >= tellAt;
        if (world == told && at == cell && now == health && !due) return;
        told = world;
        cell = at;
        health = now;
        if (due) tellAt = -1f;
        Send(grid, StrikeCommand.Stand(caster.CastPlayer, at.x, at.y, now));
    }

    void Take(StrikeHit hit, ChemGrid grid)
    {
        float was = vitals.Health;
        vitals.TakeDamage(hit.Damage);
        HitsTaken++;
        DamageTaken += was - vitals.Health;
        GameSounds.Play(SoundIds.Earth, transform.position);
        if (TryGetComponent<HandPose>(out var body)) body.Flinch();
        HoldShow.Hurt();
        var feel = beats.feel;
        CastImpact.Felt(transform.position, feel.punchFelt);
        tellAt = Time.time;
        var to = new Vector2Int(hit.ToX, hit.ToY);
        if (shove == null || to == grid.CellAt(transform.position)) return;
        var there = grid.CellCenter(to.x, to.y);
        there.y = transform.position.y;
        shove.Shunt(there, feel.punchKnockSeconds);
        tellAt += feel.punchKnockSeconds;
    }

    // Where the fist met them: the last landing's point, or just ahead at chest height before any.
    Vector3 Fist() => fist.Puncher != null ? fist.Point : transform.position + transform.forward * 0.9f + Vector3.up;

    static void Send(ChemGrid grid, StrikeCommand command)
    {
        if (grid.TryGetComponent<NetChem>(out var online) && online.Online) online.RequestStrikeCommand(command);
        else grid.World.ApplyStrike(command);
    }
}
