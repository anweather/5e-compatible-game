using Chem;
using UnityEngine;

// The Dancing Lights cantrip held ready (items.json "point"): the "laser
// pointer". While use is held, the item's light leaves as a glowing mote and
// flies to where the crosshair points, up to its reach (60 ft, about 18 m)
// from the hand; let go and it comes back.
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
        if (!useDown && (lamp.transform.position - goal).sqrMagnitude < 0.01f) CallBack();
    }

    public override void Released() => CallBack();

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
