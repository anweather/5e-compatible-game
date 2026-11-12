using UnityEngine;

// Keeps the local wizard in sight. While something stands between the camera
// and the wizard (a cabin, a rock, a prop, a raised wall, a house, a tree),
// what's in the way is cut to a screen-door dither round the wizard, easing in
// and out over a quarter of a second, and where it still hides the wizard the
// wizard shows through it as a dithered see-through figure.
//
// The dither is one global shader setting (OccluderDither.hlsl): a soft circle
// on screen round the wizard, cutting a pattern of pixels out of whatever is
// nearer the camera than the wizard. It stays opaque (depth and sorting are
// untouched) and needs no per-object fade, so it costs one compare per pixel
// while off. Our own shaders take part: VillageLit, and DitherLit, URP's Lit
// with the dither, which a Lit occluder is switched onto while in the way
// (Occluders). The terrain's trees and the packs' shaders can't be cut, so
// for them the wizard is drawn over them instead (WizardSilhouette).
//
// Every wizard carries this, but only the local player's does anything (its
// CharacterController is on; other players' wizards are puppets), solo or online.
[DefaultExecutionOrder(300)]
[RequireComponent(typeof(CharacterController))]
public class OccluderFade : MonoBehaviour
{
    [Tooltip("URP Lit occluders are switched onto this while in the way (the builder sets it, which puts it in the build).")]
    public Shader ditherLit;
    [Tooltip("The wizard's see-through figure over occluders that can't be dithered.")]
    public Shader silhouette;

    // Seconds to fade in or out; the most of the pattern cut; the wizard's chest above its feet (m).
    const float FadeSeconds = 0.25f, MaxCut = 0.75f, ChestHeight = 1.1f;
    // The circle's radius against the wizard's height on screen, and its least (fraction of the screen's height).
    const float RadiusPerHeight = 0.75f, MinRadius = 0.08f, WizardHeight = 1.8f;
    // Something has to stand this far in front of the wizard (m) to be cut, so its own feet's ground isn't.
    const float InFront = 0.6f;

    static readonly int DitherId = Shader.PropertyToID("_OccluderDither"), ShapeId = Shader.PropertyToID("_OccluderDitherShape");

    CharacterController body;
    ItemHolder hands;
    Occluders occluders;
    WizardSilhouette figure;
    float fade;
    bool publishing;

    /// <summary>The local wizard's, while it is the one keeping itself in sight.</summary>
    public static OccluderFade Local { get; private set; }

    /// <summary>Whether something stands between the camera and the wizard now.</summary>
    public bool Occluded { get; private set; }

    /// <summary>How far faded in, 0 (nothing in the way) to 1.</summary>
    public float Fade => fade;

    /// <summary>How many renderers are on their dithered copies (DitherLit) now.</summary>
    public int Swapped => occluders != null ? occluders.Swapped : 0;

    /// <summary>Whether the see-through figure is drawn now.</summary>
    public bool SilhouetteShown => figure != null && figure.Shown;

    void Awake()
    {
        body = GetComponent<CharacterController>();
        TryGetComponent(out hands);
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (!body.enabled || cam == null)
        {
            Stop();
            return;
        }
        Begin();
        var eye = cam.transform.position;
        var held = hands != null && hands.Held != null ? hands.Held.transform : null;
        Occluded = occluders.Look(eye, held);
        fade = Mathf.MoveTowards(fade, Occluded ? 1f : 0f, Time.deltaTime / FadeSeconds);
        float eased = Mathf.SmoothStep(0f, 1f, fade);
        Publish(cam, eased * MaxCut);
        figure.Show(eased > 0f);
        if (eased <= 0f && occluders.Swapped > 0) occluders.Restore();
    }

    void Begin()
    {
        Local = this;
        if (occluders != null) return;
        if (ditherLit == null) ditherLit = Shader.Find("Prototype/DitherLit");
        if (silhouette == null) silhouette = Shader.Find("Prototype/WizardSilhouette");
        occluders = new Occluders(transform, ditherLit);
        figure = new WizardSilhouette(gameObject, silhouette);
    }

    // The circle round the wizard's chest on screen, its distance, and how much is cut.
    void Publish(Camera cam, float cut)
    {
        publishing = true;
        var chest = transform.position + Vector3.up * ChestHeight;
        var onScreen = cam.WorldToViewportPoint(chest);
        float depth = Mathf.Max(onScreen.z, 0.01f);
        float tall = WizardHeight / (2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
        Shader.SetGlobalVector(DitherId, new Vector4(onScreen.x, onScreen.y, depth, onScreen.z > 0f ? cut : 0f));
        Shader.SetGlobalVector(ShapeId, new Vector4(Mathf.Max(MinRadius, tall * RadiusPerHeight), cam.aspect, InFront, 0f));
    }

    // Not the local wizard (or no camera): nothing faded, nothing switched.
    void Stop()
    {
        fade = 0f;
        Occluded = false;
        if (figure != null) figure.Show(false);
        if (occluders != null) occluders.Restore();
        if (publishing)
        {
            publishing = false;
            Shader.SetGlobalVector(DitherId, Vector4.zero);
        }
        if (Local == this) Local = null;
    }

    void OnDisable() => Stop();

    void OnDestroy()
    {
        Stop();
        occluders?.Destroy();
        figure?.Destroy();
    }
}
