using UnityEngine;

// Grows a thing in from one scale to another over a few seconds, eased, then
// removes itself: a sapling springing up, a sapling's tree filling out
// (CellDressing.Growth.cs). Only the look: the cell has already grown.
public class GrowIn : MonoBehaviour
{
    Vector3 from, to;
    float seconds, elapsed;

    /// <summary>Grows <paramref name="go"/> from <paramref name="start"/> to <paramref name="end"/> (local scales) over <paramref name="duration"/> seconds.</summary>
    public static void Play(GameObject go, Vector3 start, Vector3 end, float duration)
    {
        var grow = go.AddComponent<GrowIn>();
        grow.from = start;
        grow.to = end;
        grow.seconds = Mathf.Max(0.01f, duration);
        go.transform.localScale = start;
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float k = Mathf.SmoothStep(0f, 1f, elapsed / seconds);
        transform.localScale = Vector3.LerpUnclamped(from, to, k);
        if (elapsed < seconds) return;
        transform.localScale = to;
        Destroy(this);
    }
}
