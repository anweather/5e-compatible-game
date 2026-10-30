using UnityEngine;

// A part of a cell's dressing sliding once from one place to another, then done: a
// gate's bars rising into their lintel, or dropping shut. Only the show.
public class DressingSlide : MonoBehaviour
{
    const float Seconds = 0.7f;

    Vector3 from, to;
    float t;

    /// <summary>Slides <paramref name="part"/>, measured from where it is now, from one offset to the other.</summary>
    public static void Start(Transform part, Vector3 from, Vector3 to)
    {
        var slide = part.gameObject.AddComponent<DressingSlide>();
        slide.from = part.localPosition + from;
        slide.to = part.localPosition + to;
        part.localPosition = slide.from;
    }

    void Update()
    {
        t += Time.deltaTime / Seconds;
        transform.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
        if (t >= 1f) Destroy(this);
    }
}
