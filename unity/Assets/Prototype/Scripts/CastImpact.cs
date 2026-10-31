using Chem;
using Unity.Cinemachine;
using UnityEngine;

// What a cast does to the player, not to the world (docs/SPELL_COMBOS.md, "Impact"):
// hit-stop, Cinemachine impulses and rumble when the hands strike, and shake and
// rumble where a spell lands, by its size and how far away it is. All view, all on
// this machine: CastBeats calls in for this machine's own wizard only (a remote
// wizard's beats never stop), and a landing is felt by whoever is near, whoever cast
// it, because every peer plays every landing. Numbers in Settings/CastFeel.asset; the
// Shake and rumble setting (GameFeel) scales it all, and at 0 nothing here happens.
public static class CastImpact
{
    static readonly CinemachineImpulseDefinition SlamKick = Kick(CinemachineImpulseDefinition.ImpulseShapes.Bump);
    static readonly CinemachineImpulseDefinition ReleaseKick = Kick(CinemachineImpulseDefinition.ImpulseShapes.Recoil);
    static readonly CinemachineImpulseDefinition LandKick = Kick(CinemachineImpulseDefinition.ImpulseShapes.Explosion);

    // Same strength everywhere: how far away it is goes into the strength here.
    static CinemachineImpulseDefinition Kick(CinemachineImpulseDefinition.ImpulseShapes shape) => new()
    {
        ImpulseShape = shape,
        ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform,
    };

    /// <summary>How many kicks the camera has been given, and the size of the last (m), for the probe.</summary>
    public static int Kicks { get; private set; }
    public static float LastKick { get; private set; }

    /// <summary>A beat has started on this machine's wizard.</summary>
    public static void Began(CastBeats beats, Beat beat)
    {
        if (beat is Beat.First or Beat.Second) PadRumble.Play(beats.feel.gatherRumble, 1f);
        else if (beat == Beat.Fizzle) PadRumble.Stop();
    }

    /// <summary>This machine's wizard's held cast has reached a charge level.</summary>
    public static void Leveled(CastBeats beats, int level) =>
        PadRumble.Play(beats.feel.levelRumble, level / (float)World.MaxCharge);

    /// <summary>This machine's wizard's hands have met (Slam) or thrust home (Release).</summary>
    public static void Struck(CastBeats beats, Beat beat)
    {
        var feel = beats.feel;
        float amount = GameFeel.Shake;
        if (amount <= 0f) return;
        if (beat == Beat.Slam)
        {
            PadRumble.Play(feel.slamRumble, 1f);
            beats.HitStop(feel.slamStop * amount, () => Shake(SlamKick, feel.slamKickSeconds, Vector3.down * (feel.slamKick * amount)));
            return;
        }
        float size = SizeOf(SpellOf(beats), beats.Charge, feel);
        var along = Vector3.ProjectOnPlane(beats.AimPoint - beats.transform.position, Vector3.up).normalized;
        PadRumble.Play(feel.releaseRumble, Mathf.Lerp(feel.smallRumble, 1f, size));
        beats.HitStop(feel.releaseStop * amount, () =>
        {
            float strength = Mathf.Lerp(feel.releaseKickSmall, feel.releaseKickBig, size) * amount;
            Shake(ReleaseKick, feel.releaseKickSeconds, (FromWorld(along) + Vector3.up * feel.releaseLift).normalized * strength);
        });
    }

    /// <summary>A spell has landed, anyone's: those with no force in them (fog, light) aren't felt.</summary>
    public static void Landed(SpellDef def, Vector3 at, int charge)
    {
        var local = EventLog.TrackedCaster;
        if (def == null || local == null || !Forceful(def) || !local.TryGetComponent<CastBeats>(out var beats)) return;
        Felt(at, SizeOf(def, charge, beats.feel));
    }

    /// <summary>Something of this size (0 to 1) hit the ground here: harder the nearer it is to this machine's wizard, and the bigger the further it carries.</summary>
    public static void Felt(Vector3 at, float size)
    {
        var local = EventLog.TrackedCaster;
        float amount = GameFeel.Shake;
        if (local == null || amount <= 0f || !local.TryGetComponent<CastBeats>(out var beats)) return;
        var feel = beats.feel;
        float reach = Mathf.Lerp(feel.reachSmall, feel.reachBig, size);
        float near = 1f - Mathf.Clamp01(Vector3.Distance(at, local.transform.position) / reach);
        near *= near;
        if (near <= 0f) return;
        Shake(LandKick, feel.landKickSeconds, Vector3.down * (Mathf.Lerp(feel.landKickSmall, feel.landKickBig, size) * near * amount));
        PadRumble.Play(feel.landRumble, Mathf.Lerp(feel.smallRumble, 1f, size) * near);
    }

    /// <summary>A spell's size for shake and rumble, 0 to 1: its level, and its charge as levels (CastFeel).</summary>
    public static float SizeOf(SpellDef def, int charge, CastFeel feel)
    {
        int level = def != null ? def.Level : 0;
        return Mathf.Clamp01((level + feel.levelsPerCharge * (World.ClampCharge(charge) - 1f)) / feel.fullSizeLevel);
    }

    static bool Forceful(SpellDef def) =>
        def.Damage != null || def.Push > 0 || def.ForceThrow > 0 || def.ForcePush > 0 || def.Raise > 0 || def.Dig > 0 || def.Height != null;

    static SpellDef SpellOf(CastBeats beats)
    {
        var world = beats.TryGetComponent<SpellCaster>(out var caster) && caster.grid != null ? caster.grid.World : null;
        return world != null && beats.Spell != null ? world.Rules.Spell(beats.Spell) : null;
    }

    // The listener takes a signal in the camera's own axes.
    static Vector3 FromWorld(Vector3 direction) =>
        Camera.main != null ? Quaternion.Inverse(Camera.main.transform.rotation) * direction : direction;

    static void Shake(CinemachineImpulseDefinition kick, float seconds, Vector3 signal)
    {
        Kicks++;
        LastKick = signal.magnitude;
        kick.ImpulseDuration = seconds;
        kick.CreateEvent(Vector3.zero, signal);
    }
}
