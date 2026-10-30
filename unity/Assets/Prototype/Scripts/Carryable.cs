using System.Collections;
using System.Collections.Generic;
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
// and its "use" (Spyglass, SendingStone, DancingLights, Pick).
//
// Online (NetItem) the host decides who holds it and flies it when thrown;
// a client's copy is a puppet that rides in whichever hand holds it and
// otherwise follows the host's.
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
    /// <summary>Its physics prop (the rules side, its kind and its stable id).</summary>
    public PhysicsProp Prop => prop;
    public ChemGrid Grid => prop.grid;
    /// <summary>Its network half, or null in a scene without networking.</summary>
    public NetItem Net { get; private set; }
    /// <summary>Online, whether picking up, letting go and using it go through the host.</summary>
    public bool Online => Net != null && Net.Online;
    /// <summary>Online on a client: the host's copy is the real one.</summary>
    public bool Puppet => prop.Puppet;
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
        Net = TryGetComponent<NetItem>(out var net) ? net : null;
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
            case ItemUses.Dig: gameObject.AddComponent<Pick>(); break;
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
        OutOfTheCamerasWay(true);
    }

    /// <summary>
    /// Out of the hand, moving at <paramref name="velocity"/> (a throw, or the wizard's own pace for a drop).
    /// A puppet stays kinematic: the host's copy flies, and this one follows it.
    /// </summary>
    public void Release(Vector3 velocity)
    {
        var from = Holder;
        Holder = null;
        transform.SetParent(restParent, true);
        OutOfTheCamerasWay(false);
        foreach (var use in Uses) use.Released();
        if (from != null) StartCoroutine(StopIgnoring(from));
        if (Puppet) return;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.linearVelocity = velocity;
        body.angularVelocity = Random.insideUnitSphere * 2f;
    }

    /// <summary>Host: lets go at <paramref name="from"/> (where the holder's own screen had the hand), moving at <paramref name="velocity"/>.</summary>
    public void ReleaseAt(Vector3 from, Vector3 velocity)
    {
        Release(velocity);
        transform.position = from;
        body.position = from;
    }

    // Held, on the Ignore Raycast layer: the follow camera's obstacle check (Default only) no longer
    // hits it and snaps in when the hand swings into its line (Andrew's camera snapping), and rays
    // pass it by. Its own layers back on letting go.
    void OutOfTheCamerasWay(bool held)
    {
        if (held)
        {
            layers.Clear();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                layers.Add((t.gameObject, t.gameObject.layer));
                t.gameObject.layer = HeldLayer;
            }
            return;
        }
        foreach (var (go, layer) in layers)
            if (go != null) go.layer = layer;
        layers.Clear();
    }

    const int HeldLayer = 2; // Ignore Raycast
    readonly List<(GameObject go, int layer)> layers = new();

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
