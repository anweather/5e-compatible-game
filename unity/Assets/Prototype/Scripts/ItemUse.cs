using UnityEngine;

// The part of a carried item that does something in hand: Carryable adds the
// ones its row in rules/items.json asks for, and ItemHolder calls them.

[RequireComponent(typeof(Carryable))]
public abstract class ItemUse : MonoBehaviour
{
    protected Carryable Item { get; private set; }

    protected virtual void Awake() => Item = GetComponent<Carryable>();

    public virtual void Use(ItemHolder holder) { }

    public virtual void WhileHeld(ItemHolder holder, bool useDown) { }

    public virtual void Released() { }
}
