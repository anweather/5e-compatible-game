using System;
using Chem;
using UnityEngine;

// The wizard's body through a cast (docs/SPELL_COMBOS.md, "Beats"): the arms by
// two-bone IK, the knees, the lean and a recoil, from CastFeel's poses, no
// animation clips. It runs on every copy of every wizard and reads only what
// all peers share: that player's cast in the core, the core's fired casts, and
// the cast announcement (SpellCaster.PlayCastEffect) for the moment a spell
// leaves the hands, so everyone sees the same beats. Nothing here feeds back:
// the recoil moves the drawn bones, never the wizard. It also shows the
// elements in the hands (CastOrbs). After CastPose, whose gesture stands aside
// while a beat is playing, and before the robe and outfit.
//
// For what reacts to a cast (camera, rumble, sound): BeatChanged says when each
// beat starts, Struck when the hands land a slam or a thrust, ChargeLevel each
// level reached; IsLocal, Charge, AimPoint, Spell and Elements say whose, how
// strong, where and what. This machine's own wizard tells CastImpact, whose
// hit-stop (HitStop) stands this body's clock and Animator still for a few frames:
// the game's clock isn't touched, and another player's copy never stops, so a
// beat can run a frame or three behind on its owner's screen but never adds up.
[DefaultExecutionOrder(205)]
[RequireComponent(typeof(Animator), typeof(CastPose))]
public class CastBeats : MonoBehaviour
{
    [Tooltip("Every pose and timing. The builder sets it; without it, the defaults in CastFeel.")]
    public CastFeel feel;

    /// <summary>A beat has started on this wizard, on this machine's screen.</summary>
    public event Action<Beat> BeatChanged;
    /// <summary>The hands have got there: met, in a Slam, or thrust home, in a Release.</summary>
    public event Action<Beat> Struck;
    /// <summary>A held cast has reached this charge level (from 2).</summary>
    public event Action<int> ChargeLevel;

    /// <summary>The beat now.</summary>
    public Beat Now { get; private set; }
    /// <summary>How long it has been playing (s), by the frame's clock.</summary>
    public float InBeat { get; private set; }
    /// <summary>This is the wizard this machine plays.</summary>
    public bool IsLocal => net == null || net.IsOwner;
    /// <summary>The charge of the cast in hand: while held the level so far, once fired what it went off at.</summary>
    public int Charge { get; private set; } = 1;
    /// <summary>Where the spell is going, from its announcement (Release on).</summary>
    public Vector3 AimPoint { get; private set; }
    /// <summary>The spell last announced for this wizard.</summary>
    public string Spell { get; private set; }
    /// <summary>The elements of the cast in hand, in order; null for none.</summary>
    public (string first, string second) Elements => (first, second);
    /// <summary>A beat is posing the body; CastPose stands aside.</summary>
    public bool Driving => Now != Beat.Neutral && PoseOn;
    /// <summary>Off, the beats still run and the elements still show but the body is left to CastPose (the probe's "before").</summary>
    public bool PoseOn { get; set; } = true;

    /// <summary>In a hit-stop: this body and the camera stand still, the game runs on.</summary>
    public bool Stopped => stopLeft > 0f;
    /// <summary>How many hit-stops this wizard has had, for the probe.</summary>
    public int Stops { get; private set; }

    /// <summary>Where the hands were last put, for the probe.</summary>
    public (Vector3 right, Vector3 left) HandTargets { get; private set; }

    // What a pose is made of; what's on the body at any moment is a blend of two.
    struct Blend
    {
        public Vector3 right, left, fingers, palm;
        public float rightWeight, leftWeight, dip, lean, recoil, merge;

        public static Blend Of(CastFeel.BeatPose p) => new()
        {
            right = p.rightHand, left = p.leftHand, fingers = p.fingers, palm = p.palm, rightWeight = p.rightWeight, leftWeight = p.leftWeight,
            dip = p.dip, lean = p.lean, recoil = p.recoil, merge = p.merge,
        };

        public static Blend Lerp(in Blend a, in Blend b, float t) => new()
        {
            right = Vector3.LerpUnclamped(a.right, b.right, t), left = Vector3.LerpUnclamped(a.left, b.left, t),
            fingers = Vector3.LerpUnclamped(a.fingers, b.fingers, t), palm = Vector3.LerpUnclamped(a.palm, b.palm, t),
            rightWeight = Mathf.Clamp01(Mathf.LerpUnclamped(a.rightWeight, b.rightWeight, t)), leftWeight = Mathf.Clamp01(Mathf.LerpUnclamped(a.leftWeight, b.leftWeight, t)),
            dip = Mathf.LerpUnclamped(a.dip, b.dip, t), lean = Mathf.LerpUnclamped(a.lean, b.lean, t),
            recoil = Mathf.LerpUnclamped(a.recoil, b.recoil, t), merge = Mathf.Clamp01(Mathf.LerpUnclamped(a.merge, b.merge, t)),
        };
    }

    CastPose pose;
    SpellCaster caster;
    SitPose sit;
    NetWizard net;
    CastOrbs orbs;
    ChemGrid heard;
    Animator animator;
    Blend from, body;
    string first, second;
    // The core fired this spell, and the announcement of one: they arrive in either order.
    string firedSpell, announcedSpell;
    float firedAt, announcedAt, thrustAt, kick;
    bool struck, standing;
    float stopLeft, speedWas = 1f;
    Action afterStop;

    void Awake()
    {
        if (feel == null) feel = ScriptableObject.CreateInstance<CastFeel>();
        pose = GetComponent<CastPose>();
        animator = GetComponent<Animator>();
        TryGetComponent(out caster);
        TryGetComponent(out sit);
        TryGetComponent(out net);
        orbs = new CastOrbs(transform, pose);
    }

    void OnDisable()
    {
        Listen(null);
        EndStop();
    }

    void OnDestroy() => orbs?.Destroy();

    /// <summary>
    /// A cast was announced for this wizard: its spell, where it's going, and how long until it
    /// leaves the hands. Only a cast the core fired for this wizard thrusts (not one from the radial).
    /// </summary>
    public void Announce(string spell, Vector3 at, float leavesIn)
    {
        announcedSpell = spell;
        announcedAt = Time.time;
        thrustAt = Time.time + Mathf.Max(0f, leavesIn - feel.thrustLead);
        AimPoint = at;
        Spell = spell;
    }

    /// <summary>
    /// Stands this body and the camera still for a moment, then runs <paramref name="after"/>.
    /// A view of this machine's own: the Animator and the beat's clock, never the game's time.
    /// </summary>
    public void HitStop(float seconds, Action after)
    {
        if (seconds <= 0f || !IsLocal) return;
        if (!standing) speedWas = animator.speed;
        standing = true;
        animator.speed = 0f;
        stopLeft = seconds;
        afterStop = after;
        Stops++;
    }

    // The beat's time this frame: none in a hit-stop.
    float BeatTime()
    {
        if (!Stopped) return Time.deltaTime;
        stopLeft -= Time.deltaTime;
        if (Stopped) return 0f;
        var after = afterStop;
        EndStop();
        after?.Invoke();
        return 0f;
    }

    void EndStop()
    {
        if (standing) animator.speed = speedWas;
        standing = false;
        stopLeft = 0f;
        afterStop = null;
    }

    void Listen(ChemGrid grid)
    {
        if (heard == grid) return;
        if (heard != null) heard.TickDone -= OnTick;
        heard = grid;
        if (heard != null) heard.TickDone += OnTick;
    }

    // A tick's fired casts are only there until the next one.
    void OnTick()
    {
        string player = Player;
        foreach (var fired in heard.World.CombosFired)
        {
            if (fired.Player != player) continue;
            firedSpell = fired.Spell;
            firedAt = Time.time;
            Charge = fired.Charge;
        }
    }

    string Player => net != null && net.IsSpawned ? net.CastPlayer : SpellCaster.SoloPlayer;

    void LateUpdate()
    {
        var grid = caster != null ? caster.grid : null;
        Listen(grid);
        if (grid == null || grid.World == null) return;
        float time = BeatTime();
        InBeat += time;
        Strike();
        Step(caster.Replaying ? null : grid.World.CastOf(Player), grid.World);
        var p = feel.Of(Now);
        float t = p.seconds > 0f ? Mathf.Clamp01(InBeat / p.seconds) : 1f;
        body = Blend.Lerp(from, Blend.Of(p), p.curve.Evaluate(t));
        if (Driving) Lay();
        kick = Mathf.Max(0f, kick - time / Mathf.Max(0.01f, feel.pulseSeconds));
        Show();
    }

    // Before the beat can move on: a thrust can follow the slam in the frame the hands meet.
    void Strike()
    {
        if (struck || Now is not (Beat.Slam or Beat.Release) || InBeat < feel.Of(Now).seconds) return;
        struck = true;
        Struck?.Invoke(Now);
        if (IsLocal) CastImpact.Struck(this, Now);
    }

    // Works out the beat from the cast.
    void Step(PlayerCast cast, World world)
    {
        // A tick in slices (World.StepSome) can clear a cast a frame or two before it says it fired.
        if (world.TickInProgress) return;
        if (cast?.Casting == true)
        {
            first = cast.First;
            second = cast.Second;
            if (firedSpell == null) Level(world.HeldCharge(cast));
        }
        if (announcedSpell != null && announcedSpell != firedSpell && Time.time - announcedAt > feel.announceWait) announcedSpell = null;
        if (firedSpell != null && announcedSpell != firedSpell && Time.time - firedAt > feel.announceWait) firedSpell = null;
        bool thrust = firedSpell != null && announcedSpell == firedSpell && Time.time >= thrustAt;
        var next = CastBeat.Next(Now, InBeat, CastFacts.Of(cast, feel.slamTicks, firedSpell != null, thrust), feel);
        if (next != Now) Enter(next);
    }

    void Level(int level)
    {
        bool up = level > Charge;
        Charge = level;
        if (!up) return;
        kick = 1f;
        ChargeLevel?.Invoke(level);
        if (IsLocal) CastImpact.Leveled(this, level);
    }

    void Enter(Beat beat)
    {
        Now = beat;
        InBeat = 0f;
        struck = false;
        from = body;
        switch (beat)
        {
            case Beat.Slam:
                kick = 1f;
                break;
            case Beat.Release:
                firedSpell = announcedSpell = null;
                break;
            case Beat.Fizzle:
                orbs.Fizzle(first, second);
                firedSpell = null;
                break;
        }
        if (beat is Beat.Release or Beat.Fizzle or Beat.Neutral) first = second = null;
        if (beat == Beat.Neutral) Charge = 1;
        BeatChanged?.Invoke(beat);
        if (IsLocal) CastImpact.Began(this, beat);
    }

    // Lays the pose on the bones: the body, then the hands on it.
    void Lay()
    {
        var forward = transform.forward;
        var aim = Quaternion.identity;
        bool aimed = Now is Beat.Release or Beat.Recovery;
        if (aimed)
        {
            var to = AimPoint - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 1e-4f) forward = to.normalized;
        }
        var right = Vector3.Cross(Vector3.up, forward);
        if (aimed) aim = pose.AimAt(AimPoint, forward, right);
        bool seated = sit != null && sit.Weight > 0.1f;
        float back = seated ? 0f : body.recoil * pose.Scale;
        pose.LayBody(seated ? 0f : body.dip, body.lean, forward, -forward * back);
        float tremble = Now == Beat.Charge ? feel.tremble * Charge * Charge * pose.Scale : 0f;
        var frame = new CastPose.HandFrame(transform.position - forward * back, forward, right, aim, tremble);
        HandTargets = (
            pose.LayHand(true, frame, body.right, body.rightWeight, body.fingers, body.palm),
            pose.LayHand(false, frame, body.left, body.leftWeight, body.fingers, body.palm));
    }

    void Show()
    {
        bool gathering = Now is Beat.First or Beat.Second;
        orbs.Motes(gathering ? first : null, gathering ? second : null);
        float energy = 1f + feel.growPerLevel * (Charge - 1) + feel.pulse * kick * kick;
        orbs.Show(first, second, energy, body.merge);
    }
}
