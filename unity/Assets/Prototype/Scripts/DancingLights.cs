using Chem;
using UnityEngine;

// The Dancing Lights cantrip held ready (items.json "point"): the "laser
// pointer". While use is held, the item's light leaves as a glowing mote and
// flies to where the crosshair points, up to its reach (60 ft, about 18 m)
// from the hand; let go and it comes back.
//
// Online the holder shares where the mote is (NetItem), and everyone else's
// copy eases it there, so all see the same pointer.
//
// A held item rather than a ninth spell: the spell bar's spells are cell
// effects cast through World.Cast, and a moving light touches no cells, so it
// would need a new spell effect in both rule checkers and a ninth key.
[RequireComponent(typeof(ItemLight))]
public class DancingLights : ItemUse
{
    /// <summary>How fast the mote flies, in m/s.</summary>
    public const float Speed = 25f;
    // How far off a surface the mote hovers, in metres.
    const float Hover = 0.5f;

    ItemLight itemLight;
    GameObject mote;
    // Online, someone else's mote: where their screen last had it.
    Vector3? shared;

    /// <summary>True while the mote is out of the hand.</summary>
    public bool Out { get; private set; }
    /// <summary>Where the mote is; the item's own position when it is in hand.</summary>
    public Vector3 MotePosition => Out ? itemLight.Lamp.transform.position : transform.position;

    protected override void Awake()
    {
        base.Awake();
        itemLight = GetComponent<ItemLight>();
    }

    public override void WhileHeld(ItemHolder holder, bool useDown)
    {
        var lamp = itemLight.Lamp;
        if (lamp == null) return;
        if (useDown && !Out) SendOut(lamp);
        if (!Out) return;
        var goal = useDown ? Target(holder) : transform.position;
        lamp.transform.position = Vector3.MoveTowards(lamp.transform.position, goal, Speed * Time.deltaTime);
        bool back = !useDown && (lamp.transform.position - goal).sqrMagnitude < 0.01f;
        if (Item.Online) Item.Net.ShareMote(!back, lamp.transform.position, now: back);
        if (back) CallBack();
    }

    /// <summary>Online: someone else's mote is out at <paramref name="at"/>, or back in their hand.</summary>
    public void Follow(bool isOut, Vector3 at)
    {
        var lamp = itemLight.Lamp;
        if (lamp == null) return;
        if (!isOut)
        {
            shared = null;
            CallBack();
            return;
        }
        if (!Out) SendOut(lamp);
        shared = at;
    }

    void Update()
    {
        if (!Out || shared == null) return;
        var lamp = itemLight.Lamp.transform;
        lamp.position = Vector3.MoveTowards(lamp.position, shared.Value, Speed * Time.deltaTime);
    }

    public override void Released()
    {
        shared = null;
        CallBack();
    }

    /// <summary>Where the crosshair points, no further than the reach from the hand.</summary>
    Vector3 Target(ItemHolder holder)
    {
        float reach = (float)(Item.Def.Reach * ItemLightDef.FeetToMetres);
        var ray = Aiming.Ray(holder.controls);
        var point = Aiming.PastSelf(ray, holder.transform, out var hit) ? hit.point + hit.normal * Hover : ray.GetPoint(200f);
        var from = holder.HandPosition;
        return from + Vector3.ClampMagnitude(point - from, reach);
    }

    void SendOut(Light lamp)
    {
        Out = true;
        // Out on its own at full size (in hand it is scaled with the little item).
        lamp.transform.SetParent(null, true);
        lamp.transform.localScale = Vector3.one;
        if (mote == null) mote = MakeMote(lamp);
        mote.SetActive(true);
    }

    void CallBack()
    {
        if (!Out) return;
        Out = false;
        itemLight.Lamp.transform.SetParent(transform, false);
        itemLight.Lamp.transform.localPosition = Vector3.zero;
        mote.SetActive(false);
    }

    void OnDestroy()
    {
        if (mote != null) Destroy(mote.GetComponent<Renderer>().sharedMaterial);
        if (Out && itemLight != null && itemLight.Lamp != null) Destroy(itemLight.Lamp.gameObject);
    }

    static GameObject MakeMote(Light lamp)
    {
        var m = Primitives.Visual(PrimitiveType.Sphere, "Mote");
        m.transform.SetParent(lamp.transform, false);
        m.transform.localScale = Vector3.one * 0.15f;
        var look = m.GetComponent<Renderer>();
        look.sharedMaterial = UrpMaterials.NewLit("Mote", lamp.color, emission: lamp.color * 6f);
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return m;
    }
}
