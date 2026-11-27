using UnityEngine;

// Cheap fire look: jitter the flame block's height and turn it slowly.
public class Flicker : MonoBehaviour
{
    Vector3 baseScale;
    float seed;

    void Start()
    {
        baseScale = transform.localScale;
        seed = Random.value * 100f;
    }

    void Update()
    {
        float t = Time.time * 9f + seed;
        float h = 0.75f + 0.35f * Mathf.PerlinNoise(t, seed);
        transform.localScale = new Vector3(baseScale.x, baseScale.y * h, baseScale.z);
        transform.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
    }
}
