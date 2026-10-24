using UnityEngine;

// Something that can catch fire, burn for a while, spread fire to its
// neighbours and then burn out. Hard-coded for now; the next step is to
// drive these numbers from rules/*.json.
public class Burnable : MonoBehaviour
{
    public string displayName = "grass";
    [Tooltip("Seconds it burns before burning out.")]
    public float fuel = 3f;
    [Tooltip("Chance per second that the fire jumps to each touching burnable.")]
    public float spreadChancePerSecond = 0.6f;
    public float spreadRadius = 0.6f;
    public bool destroyWhenBurnt;
    public Material fireMaterial;
    public Material burntMaterial;

    public bool IsBurning { get; private set; }
    public bool IsBurnt { get; private set; }

    const float SpreadInterval = 0.25f;
    GameObject flames;
    float spreadTimer;
    readonly Collider[] nearby = new Collider[32];

    public bool Ignite()
    {
        if (IsBurning || IsBurnt) return false;
        IsBurning = true;
        flames = MakeFlames();
        return true;
    }

    void Update()
    {
        if (!IsBurning) return;

        spreadTimer += Time.deltaTime;
        if (spreadTimer >= SpreadInterval)
        {
            spreadTimer -= SpreadInterval;
            Spread();
        }

        fuel -= Time.deltaTime;
        if (fuel <= 0f) BurnOut();
    }

    void Spread()
    {
        var bounds = GetComponent<Collider>().bounds;
        int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * (spreadRadius * 0.5f), nearby);
        for (int i = 0; i < count; i++)
        {
            var other = nearby[i].GetComponentInParent<Burnable>();
            if (other == null || other == this) continue;
            if (Random.value < spreadChancePerSecond * SpreadInterval) other.Ignite();
        }
    }

    void BurnOut()
    {
        IsBurning = false;
        IsBurnt = true;
        if (flames != null) Destroy(flames);
        if (destroyWhenBurnt)
        {
            EventLog.Add($"The {displayName} burns to cinders");
            Destroy(gameObject);
            return;
        }
        if (burntMaterial != null) GetComponent<Renderer>().sharedMaterial = burntMaterial;
    }

    GameObject MakeFlames()
    {
        var top = GetComponent<Collider>().bounds;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Flames";
        Destroy(go.GetComponent<Collider>());
        go.transform.position = new Vector3(top.center.x, top.max.y + 0.25f, top.center.z);
        go.transform.localScale = new Vector3(0.6f, 0.5f, 0.6f);
        go.transform.SetParent(transform, true);
        if (fireMaterial != null) go.GetComponent<Renderer>().sharedMaterial = fireMaterial;
        go.AddComponent<Flicker>();
        return go;
    }
}
