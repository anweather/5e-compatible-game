using System;
using Chem;
using UnityEngine;

// A wizard who casts, as the world sees one (docs/design/architecture-review.md step 10): the face the grid, the
// weather, the water, the peers and the row houses (MoveTo, out of a house's way) ask of a caster, so they never name
// the wizard. SpellCaster is the one caster; it joins the face's registry beside its own, in the same Awake and
// OnDestroy, so Caster.All and InPlay hand out the same wizards in the same order as SpellCaster's. A meteor from the
// sky (the weather's, a falling star) takes its look from a caster's Meteor; the show itself is MeteorFall's, which
// fills in MeteorShow as the game loads.
public abstract class Caster : MonoBehaviour
{
    public const string SoloPlayer = "solo";

    static readonly Registry<Caster> registry = new();

    public static Caster[] All => registry.All();

    public static Caster[] AllByInstanceId => registry.AllByInstanceId();

    public static Caster InPlay => registry.First;

    public static Action<Caster, Vector3, Vector3, float, float, bool> MeteorShow { get; set; }

    protected void JoinCasters() => registry.Join(this);

    protected void LeaveCasters() => registry.Leave(this);

    public abstract int Slot { get; }

    public abstract string StandPlayer { get; }

    public abstract void PlayTriggered(TriggeredCast cast, float seconds);

    public abstract void MoveTo(Vector3 to);

    public abstract Color? LookGlow(string spellId);

    public abstract bool FireLookOf(string spellId, out LookTint tint, out Color glow);

    public static void Meteor(Caster lookFrom, Vector3 at, Vector3 viewer, float seconds, float size, bool plainIfNoLook) =>
        MeteorShow?.Invoke(lookFrom, at, viewer, seconds, size, plainIfNoLook);
}
