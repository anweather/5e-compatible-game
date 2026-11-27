using Punchy.Fauna;
using UnityEngine;

// The valley's animals as the wizard and the things see them (docs/design/architecture-review.md step 10): what
// footprints, Shapeshift and the things' creature bodies ask of the animals, so they never name the fauna. ValleyFauna
// is the one fauna; it joins the face's registry beside its own, in the same Awake and OnDestroy, so InPlay hands out
// the same animals as ValleyFauna.InPlay.
public abstract class FaunaFace : MonoBehaviour
{
    static readonly Registry<FaunaFace> registry = new();

    public static FaunaFace InPlay => registry.First;

    protected void JoinFauna() => registry.Join(this);

    protected void LeaveFauna() => registry.Leave(this);

    public abstract Material CreatureMaterial { get; }

    public abstract int Count { get; }

    public abstract CreatureState State(int index);

    public abstract int Id(int index);

    public abstract int SpeciesOf(int index);

    public abstract int Coat(int index);

    public abstract CreatureBody Body(int speciesIndex);

    public abstract string SpeciesName(int speciesIndex);

    public abstract int Aimed(Ray ray, float maxDistance);
}
