using Unity.Cinemachine;
using UnityEngine;

// Looking through a spyglass (items.json "zoom"): while it is in hand and use
// is held, things look Zoom times closer. Twice as close means half the
// angle's tangent, not half the field of view: 40° becomes about 20.6°.
// The over-the-shoulder camera also moves up to the shoulder, so the wizard
// isn't in the way of the view.
public class Spyglass : ItemUse
{
    // How quickly the view eases in and out, per second.
    const float Ease = 10f;

    // The camera it zoomed, and its field of view and distance before, to put back.
    CinemachineCamera zoomed;
    CinemachineThirdPersonFollow follow;
    float baseFov, baseDistance;
    Texture2D mask;
    GUIStyle style;

    /// <summary>True while the holder is looking through it.</summary>
    public bool Looking { get; private set; }

    /// <summary>The field of view that makes things look <paramref name="zoom"/> times closer than <paramref name="fov"/> does.</summary>
    public static float Magnified(float fov, float zoom) =>
        2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, zoom)) * Mathf.Rad2Deg;

    public override void WhileHeld(ItemHolder holder, bool useDown)
    {
        if (useDown && zoomed == null && holder.view != null)
        {
            zoomed = holder.view;
            baseFov = zoomed.Lens.FieldOfView;
            if (zoomed.TryGetComponent(out follow)) baseDistance = follow.CameraDistance;
        }
        Looking = useDown && zoomed != null;
        if (zoomed == null) return;
        float ease = 1f - Mathf.Exp(-Ease * Time.deltaTime);
        float target = Looking ? Magnified(baseFov, (float)Item.Def.Zoom) : baseFov;
        zoomed.Lens.FieldOfView = Mathf.Lerp(zoomed.Lens.FieldOfView, target, ease);
        if (follow != null) follow.CameraDistance = Mathf.Lerp(follow.CameraDistance, Looking ? 0f : baseDistance, ease);
        if (!Looking && Mathf.Abs(zoomed.Lens.FieldOfView - baseFov) < 0.05f) Restore();
    }

    public override void Released() => Restore();

    void Restore()
    {
        Looking = false;
        if (zoomed != null) zoomed.Lens.FieldOfView = baseFov;
        if (follow != null) follow.CameraDistance = baseDistance;
        zoomed = null;
        follow = null;
    }

    void OnDestroy()
    {
        Restore();
        if (mask != null) Destroy(mask);
    }

    // A round eyepiece: dark beyond a circle the height of the screen.
    void OnGUI()
    {
        if (!Looking || !EventLog.HudVisible) return;
        if (mask == null) mask = EyepieceMask(128);
        float size = Screen.height;
        float left = (Screen.width - size) / 2f;
        GUI.DrawTexture(new Rect(left, 0, size, size), mask);
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, left + 1, size), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(left + size - 1, 0, left + 1, size), Texture2D.whiteTexture);
        GUI.color = Color.white;
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, alignment = TextAnchor.UpperCenter };
        GUI.Label(new Rect(0, 72, Screen.width, 30), $"<b>{Item.Name}</b>   ×{Item.Def.Zoom:0.#}", style);
    }

    static Texture2D EyepieceMask(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Eyepiece", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float r = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude / (size / 2f);
                pixels[y * size + x] = new Color32(0, 0, 0, (byte)(255f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 0.98f, r))));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }
}
