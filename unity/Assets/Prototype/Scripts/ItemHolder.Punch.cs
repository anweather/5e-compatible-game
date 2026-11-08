using System;
using UnityEngine;

// The free hand's punch (PlayerControls.Punch): empty-handed, a quick jab at
// what's just in front, the way the wizard aims within its front arc. It knocks
// the nearest prop or item through PhysicsProp.Shove, or else hits the nearest
// pane as a heavy thing does, so the first punch cracks the glass and the next
// breaks it (ChemEffects.Strike; the cells are the truth). Not while carrying
// anything, casting, swimming, sitting, rolling, down or in an animal's shape, and
// one that's stopped by any of those before the fist gets there doesn't land. On the
// pad it's Circle, which cancels a cast or the radial first, so those presses don't
// punch. Only the puncher's machine shows the arm (PunchPose), as with a throw.
//
// PunchLanded is the one moment the fist arrives, hit or miss, once for each punch that
// gets there, on the puncher's machine: the core's unarmed strike is sent from it
// (WizardStrike). Strength and timing are in Settings/CastFeel.asset.
public partial class ItemHolder
{
    // How far ahead the fist lands and how much around it counts, in metres, at what height.
    const float PunchAhead = 0.9f, PunchRadius = 0.7f, PaneRadius = 1.2f, PunchHeight = 1f;

    /// <summary>A punch: who threw it, what it's going to or has met, and the way it went.</summary>
    public readonly struct PunchLanding
    {
        public readonly ItemHolder Puncher;
        /// <summary>The prop or item it knocks; null if none.</summary>
        public readonly PhysicsProp Prop;
        /// <summary>The pane cell it strikes, when there's no prop; null if none.</summary>
        public readonly Vector2Int? Pane;
        /// <summary>Another wizard by the fist, prop or not; null if none. The arm goes to it; whether it's hit is the core's (WizardStrike).</summary>
        public readonly Knockback Wizard;
        /// <summary>Where the fist goes on what it met, or the middle of the air it punched.</summary>
        public readonly Vector3 Point;
        /// <summary>The level way the punch goes.</summary>
        public readonly Vector3 Way;

        public PunchLanding(ItemHolder puncher, PhysicsProp prop, Vector2Int? pane, Knockback wizard, Vector3 point, Vector3 way)
        {
            Puncher = puncher;
            Prop = prop;
            Pane = pane;
            Wizard = wizard;
            Point = point;
            Way = way;
        }

        /// <summary>It knocked a thing or struck a pane.</summary>
        public bool Hit => Prop != null || Pane != null;
        /// <summary>There's something for the fist to go to; otherwise it goes straight ahead.</summary>
        public bool Reached => Hit || Wizard != null;
    }

    /// <summary>A punch has started, at what's in reach now.</summary>
    public event Action<PunchLanding> PunchStarted;
    /// <summary>The fist has got there: once for each punch, hit or miss.</summary>
    public event Action<PunchLanding> PunchLanded;

    float punchLandsAt = -1f, nextPunch;
    bool handsPunch;

    void TryPunch(bool fromHand = false)
    {
        if (Time.time < nextPunch || !(fromHand ? MayPunchUnlessCasting(false) : MayPunchNow)) return;
        handsPunch = fromHand;
        var feel = beats.feel;
        nextPunch = Time.time + feel.punchCooldown;
        punchLandsAt = Time.time + feel.punchDraw.seconds + feel.punchOut.seconds;
        PunchStarted?.Invoke(InReachOfFist());
    }

    /// <summary>
    /// An empty hand's gesture in the Hold (rules/hold.json): a throw lets go of what's carried, and the rest are a punch, which
    /// is all a free hand has today.
    /// </summary>
    public void HandAct(string act)
    {
        if (act == "throw") Throw(hardThrow);
        else TryPunch(fromHand: true);
    }

    /// <summary>Nothing stops the wizard punching right now.</summary>
    public bool MayPunchNow => MayPunchUnlessCasting((TryGetComponent<SpellCaster>(out var caster) && caster.CastInProgress) || beats.HandsBusy || (radial != null && radial.BusyThisFrame));

    // A hand's own act is the cast that just went off, so it isn't one the punch would cancel.
    bool MayPunchUnlessCasting(bool cancelling) => Held == null && MayPunch(
        cancelling,
        Animal,
        TryGetComponent<SurfaceEffects>(out var feet) && feet.Swimming,
        TryGetComponent<Sitter>(out var sitter) && sitter.Seated != null,
        TryGetComponent<RollPose>(out var roll) && roll.Playing,
        TryGetComponent<DownedPose>(out var down) && down.Lying);

    /// <summary>Whether the punch button punches: <paramref name="cancelling"/> is a cast under way or the radial open, which that press cancels.</summary>
    public static bool MayPunch(bool cancelling, bool animal, bool swimming, bool seated, bool rolling, bool downed) =>
        !cancelling && !animal && !swimming && !seated && !rolling && !downed;

    void PunchLands()
    {
        punchLandsAt = -1f;
        if (!(handsPunch ? MayPunchUnlessCasting(false) : MayPunchNow)) return;
        var landing = InReachOfFist();
        if (landing.Prop != null) landing.Prop.Shove(Forces.Thrown(AwayFromMe(landing)) * (beats.feel.punchShove * Forces.ImpulsePerPush));
        else if (landing.Pane != null) ChemEffects.Strike(GetComponent<SpellCaster>().grid, landing.Pane.Value);
        if (landing.Hit) FistLanded(landing.Point, landing.Way);
        PunchLanded?.Invoke(landing);
    }

    // The puncher's flash at the fist: small, warm white, quick.
    const float FistFlash = 0.22f;
    static readonly Color FistFlashTint = new(1f, 0.93f, 0.75f, 1f);

    /// <summary>How many of this wizard's punches have landed, as its puncher feels them (FistLanded), for the probe.</summary>
    public int FistHits { get; private set; }

    /// <summary>
    /// The puncher's own feel of a fist that lands, on its machine: on a thing or a pane here, on a wizard or a raider when the
    /// core says the strike hit (WizardStrike; playtest 2026-11-08 B3, the puncher had nothing). The thud the one struck hears,
    /// a small flash at the fist, and the stop, camera kick and pad rumble (CastImpact.Punched).
    /// </summary>
    public void FistLanded(Vector3 at, Vector3 way)
    {
        FistHits++;
        GameSounds.Play(SoundIds.Earth, at);
        BurstFx.Play(BurstFx.Kind.Flash, at, FistFlash, tint: FistFlashTint);
        if (beats != null) CastImpact.Punched(beats, way);
    }

    // A thing flies on from the fist, away from the wizard: the aim can be 70 degrees off where it stands.
    Vector3 AwayFromMe(PunchLanding landing)
    {
        var away = landing.Prop.transform.position - transform.position;
        away.y = 0f;
        return away.sqrMagnitude > 1e-4f ? away.normalized : landing.Way;
    }

    // What the fist would land on now.
    PunchLanding InReachOfFist()
    {
        var ahead = FrontArc(AimDirection);
        ahead.y = 0f;
        ahead = ahead.sqrMagnitude > 1e-4f ? ahead.normalized : transform.forward;
        var chest = transform.position + Vector3.up * PunchHeight;
        var fist = chest + ahead * PunchAhead;
        var prop = NearestProp(fist);
        var pane = prop != null ? null : NearestPane(fist);
        var wizard = NearestWizard(fist);
        var point = fist;
        if (prop != null) point = prop.GetComponentInChildren<Collider>().bounds.ClosestPoint(chest);
        else if (pane != null) point = PaneAt(pane.Value, fist.y);
        else if (wizard != null) point = wizard.transform.position + Vector3.up * PunchHeight;
        return new PunchLanding(this, prop, pane, wizard, point, ahead);
    }

    Vector2Int? NearestPane(Vector3 fist)
    {
        if (!TryGetComponent<SpellCaster>(out var caster) || caster.grid == null || caster.grid.World == null) return null;
        var grid = caster.grid;
        int pane = WindowPanes.NearestPane(grid, fist, PaneRadius);
        return pane < 0 ? null : new Vector2Int(pane % grid.World.W, pane / grid.World.W);
    }

    Vector3 PaneAt(Vector2Int cell, float height)
    {
        var at = GetComponent<SpellCaster>().grid.CellCenter(cell.x, cell.y);
        at.y = height;
        return at;
    }

    // The prop or item nearest the fist, not in anyone's hand.
    PhysicsProp NearestProp(Vector3 fist)
    {
        PhysicsProp best = null;
        float bestDistance = float.MaxValue;
        foreach (var prop in Forces.InSphere<PhysicsProp>(fist, PunchRadius, transform))
        {
            if (prop.TryGetComponent<Carryable>(out var item) && item.Holder != null) continue;
            float d = (prop.transform.position - fist).sqrMagnitude;
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = prop;
        }
        return best;
    }

    // Another wizard by the fist, found by distance: other players' have no live collider on this machine (Forces).
    Knockback NearestWizard(Vector3 fist)
    {
        Knockback best = null;
        float bestDistance = PunchRadius * PunchRadius;
        foreach (var wizard in Knockback.All)
        {
            float d = (wizard.transform.position + Vector3.up * PunchHeight - fist).sqrMagnitude;
            if (wizard.gameObject == gameObject || d >= bestDistance) continue;
            bestDistance = d;
            best = wizard;
        }
        return best;
    }
}
