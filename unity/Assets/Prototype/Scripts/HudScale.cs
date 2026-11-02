using UnityEngine;

// One scale for everything drawn on the screen, so nothing is tiny at a high
// resolution: IMGUI's version of a Canvas Scaler set to Scale With Screen Size
// (reference 1920x1080, match 0.5). An OnGUI calls Apply, then lays itself out
// in Width and Height instead of Screen.width and Screen.height.
public static class HudScale
{
    const float ReferenceWidth = 1920f, ReferenceHeight = 1080f;

    /// <summary>Screen pixels per HUD unit: the mean, in log space, of the two axes' scales.</summary>
    public static float Factor => Forced > 0f ? Forced : Mathf.Sqrt(Screen.width / ReferenceWidth * (Screen.height / ReferenceHeight));

    /// <summary>A factor to use whatever the screen, above 0; the probe's way to a big screen, since its Game view is 1080p.</summary>
    public static float Forced { get; set; }

    /// <summary>The screen's width in HUD units.</summary>
    public static float Width => Screen.width / Factor;

    /// <summary>The screen's height in HUD units.</summary>
    public static float Height => Screen.height / Factor;

    /// <summary>Scales this OnGUI's drawing.</summary>
    public static void Apply() => GUI.matrix = Matrix4x4.Scale(new Vector3(Factor, Factor, 1f));

    /// <summary>A matrix that turns what's drawn next about a point in HUD units. GUIUtility.RotateAroundPivot
    /// takes its pivot in screen pixels, so after Apply it turns about the wrong place everywhere but 1080p.</summary>
    public static Matrix4x4 TurnedAbout(Matrix4x4 matrix, float degrees, Vector2 pivot) =>
        matrix * Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, degrees)) * Matrix4x4.Translate(-pivot);
}
