using UnityEngine;

// Slowly scrolls a material's texture offset (all of URP Lit's maps follow
// _BaseMap's), so a static water surface ripples. Used by the valley's lake.
[RequireComponent(typeof(Renderer))]
public class ScrollingTexture : MonoBehaviour
{
    public Vector2 speed = new Vector2(0.01f, 0.01f);
    Material material;

    void Awake() => material = GetComponent<Renderer>().material;

    void Update() => material.SetTextureOffset("_BaseMap", speed * Time.time);

    void OnDestroy() => Destroy(material);
}
