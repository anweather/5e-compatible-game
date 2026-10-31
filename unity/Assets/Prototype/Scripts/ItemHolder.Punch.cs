using UnityEngine;

// The free hand's punch (PlayerControls.Punch): empty-handed, a quick jab at
// what's just in front, the way the wizard aims within its front arc. It knocks
// the nearest prop or item through PhysicsProp.Shove, or else hits the nearest
// pane as a heavy thing does, so the first punch cracks the glass and the next
// breaks it (ChemEffects.Strike; the cells are the truth). Not while carrying
// anything, casting, swimming, sitting or in an animal's shape. Only the
// puncher's machine shows the arm, as with a throw (ThrowPose).
public partial class ItemHolder
{
    // How far ahead the fist lands and how much around it counts, in metres, at what height.
    const float PunchAhead = 0.9f, PunchRadius = 0.7f, PaneRadius = 1.2f, PunchHeight = 1f;
    // Between the arm starting and the fist landing, and between one punch and the next, in seconds.
    const float PunchLandSeconds = 0.12f, PunchCooldown = 0.4f;
    // How hard it looks in the arm; the hit is a shove's, so a crate or an item goes off at 2.5 m/s.
    const float PunchHow = 0.5f;

    float punchLandsAt = -1f, nextPunch;

    void TryPunch()
    {
        if (Time.time < nextPunch || !CanPunch) return;
        nextPunch = Time.time + PunchCooldown;
        punchLandsAt = Time.time + PunchLandSeconds;
        Swing(PunchHow);
    }

    bool CanPunch =>
        !Animal
        && (radial == null || !radial.IsOpen)
        && !(TryGetComponent<SpellCaster>(out var caster) && caster.CastInProgress)
        && !(TryGetComponent<SurfaceEffects>(out var feet) && feet.Swimming)
        && !(TryGetComponent<Sitter>(out var sitter) && sitter.Seated != null);

    void PunchLands()
    {
        punchLandsAt = -1f;
        if (Held != null) return;
        var ahead = FrontArc(AimDirection);
        ahead.y = 0f;
        ahead = ahead.sqrMagnitude > 1e-4f ? ahead.normalized : transform.forward;
        var fist = transform.position + Vector3.up * PunchHeight + ahead * PunchAhead;
        var prop = NearestProp(fist);
        if (prop != null)
        {
            prop.Shove(Forces.Thrown(ahead) * Forces.ImpulsePerPush);
            GameSounds.Play(SoundIds.Earth, fist);
            return;
        }
        if (!TryGetComponent<SpellCaster>(out var caster) || caster.grid == null || caster.grid.World == null) return;
        var grid = caster.grid;
        int pane = WindowPanes.NearestPane(grid, fist, PaneRadius);
        if (pane < 0) return;
        ChemEffects.Strike(grid, new Vector2Int(pane % grid.World.W, pane / grid.World.W));
        GameSounds.Play(SoundIds.Earth, fist);
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
}
