using UnityEngine;

// The part of a carried item that does something in hand: Carryable adds the
// ones its row in rules/items.json asks for, and ItemHolder calls them.

/// <summary>What a held item does when its holder presses "use" (PlayerControls.Use).</summary>
[RequireComponent(typeof(Carryable))]
public abstract class ItemUse : MonoBehaviour
{
    protected Carryable Item { get; private set; }

    protected virtual void Awake() => Item = GetComponent<Carryable>();

    /// <summary>Use was pressed this frame.</summary>
    public virtual void Use(ItemHolder holder) { }

    /// <summary>Every frame it is in hand, after the holder has placed it; <paramref name="useDown"/> while use is held.</summary>
    public virtual void WhileHeld(ItemHolder holder, bool useDown) { }

    /// <summary>It just left the hand.</summary>
    public virtual void Released() { }
}
