using System.Collections;
using Chem;
using UnityEngine;

// A thing a wizard can pick up, carry, drop and throw (rules/items.json; the
// item id is its PhysicsProp's kind). On the ground it is an ordinary physics
// prop: it floats on the pond, slides on oil, and Thunderwave, Gust of Wind
// and Levitate move it. In hand (ItemHolder) its body is kinematic and rides
// along with the wizard, so spells and the wizard's own collider leave it be.
//
// What it does when used comes from its row in items.json, as behaviours
// added at Start: a light (ItemLight), breaking on a hard landing (Fragile),
// and its "use" (Spyglass, SendingStone, DancingLights).
[RequireComponent(typeof(PhysicsProp), typeof(Rigidbody))]
public class Carryable : MonoBehaviour
{
    [Tooltip("Sending stones: the other stone of the pair.")]
    public Carryable partner;

    // How long after leaving a hand it ignores the wizard who threw it, so it clears them.
    const float ClearHolderSeconds = 0.4f;

    PhysicsProp prop;
    Rigidbody body;
    Collider[] shapes;
    Transform restParent;

    public ItemDef Def { get; private set; }
    public ChemGrid Grid => prop.grid;
    /// <summary>Who has it in hand, or null when it lies about.</summary>
    public ItemHolder Holder { get; private set; }
    public string Name => Def != null ? Def.Name : name;
    /// <summary>What it does in hand (ItemLight, Spyglass, SendingStone, DancingLights).</summary>
    public ItemUse[] Uses { get; private set; } = System.Array.Empty<ItemUse>();

    void Awake()
    {
        prop = GetComponent<PhysicsProp>();
        body = GetComponent<Rigidbody>();
        shapes = GetComponents<Collider>();
        restParent = transform.parent;
    }

    void Start()
    {
        var world = prop.grid != null ? prop.grid.World : null;
        Def = world?.Rules.Item(prop.kind);
        if (Def == null)
        {
            Debug.LogWarning($"{name}: no item '{prop.kind}' in rules/items.json");
            enabled = false;
            return;
        }
        AddBehaviours();
        Uses = GetComponents<ItemUse>();
        prop.grid.RulesChanged += ReadRules;
    }

    void OnDestroy()
    {
        if (prop != null && prop.grid != null) prop.grid.RulesChanged -= ReadRules;
    }

    /// <summary>Raised after a live mod gave this item new numbers (its light, zoom, how it breaks).</summary>
    public event System.Action DefChanged;

    // A live mod changed the rules: read this item's row again. The behaviours added at Start stay
    // as they are (a mod that gives a ball a "use" it didn't have needs a restart to add it).
    void ReadRules()
    {
        var def = prop.grid.World.Rules.Item(prop.kind);
        if (def == null) return;
        Def = def;
        DefChanged?.Invoke();
    }

    void AddBehaviours()
    {
        if (Def.Light != null) gameObject.AddComponent<ItemLight>();
        if (Def.Breaks != null) gameObject.AddComponent<Fragile>();
        switch (Def.Use)
        {
            case ItemUses.Zoom: gameObject.AddComponent<Spyglass>(); break;
            case ItemUses.Send: gameObject.AddComponent<SendingStone>(); break;
            case ItemUses.Point: gameObject.AddComponent<DancingLights>(); break;
        }
    }

    /// <summary>Into <paramref name="holder"/>'s hand: kinematic, and part of the holder, so its spells and aim pass it by.</summary>
    public void TakeInto(ItemHolder holder)
    {
        Holder = holder;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.None;
        IgnoreHolder(holder, true);
        transform.SetParent(holder.transform, true);
    }

    /// <summary>Out of the hand, moving at <paramref name="velocity"/> (a throw, or the wizard's own pace for a drop).</summary>
    public void Release(Vector3 velocity)
    {
        var from = Holder;
        Holder = null;
        transform.SetParent(restParent, true);
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.linearVelocity = velocity;
        body.angularVelocity = Random.insideUnitSphere * 2f;
        foreach (var use in Uses) use.Released();
        if (from != null) StartCoroutine(StopIgnoring(from));
    }

    IEnumerator StopIgnoring(ItemHolder from)
    {
        yield return new WaitForSeconds(ClearHolderSeconds);
        if (from != null && Holder != from) IgnoreHolder(from, false);
    }

    void IgnoreHolder(ItemHolder holder, bool ignore)
    {
        foreach (var theirs in holder.Colliders)
            foreach (var mine in shapes) Physics.IgnoreCollision(mine, theirs, ignore);
    }
}
