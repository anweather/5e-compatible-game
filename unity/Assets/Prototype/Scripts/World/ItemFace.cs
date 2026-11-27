using UnityEngine;

// An item as the things' spawner sees it (docs/design/architecture-review.md step 10), so ThingSpawner never names
// the wizard's items: Carryable is the one item. A thing taken away is let go by whoever held it (Lost), and two
// linked things (a pair of sending stones) are each other's partner (PairWith).
public abstract class ItemFace : MonoBehaviour
{
    public abstract void Lost();

    public abstract void PairWith(ItemFace other);
}
