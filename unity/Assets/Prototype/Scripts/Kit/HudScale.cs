using UnityEngine;

// One scale for everything drawn on the screen, so nothing is tiny at a high
// resolution: IMGUI's version of a Canvas Scaler set to Scale With Screen Size
// (reference 1920x1080, match 0.5). An OnGUI calls Apply, then lays itself out
// in Width and Height instead of Screen.width and Screen.height.
public static class HudScale
{
    const float ReferenceWidth = 1920f, ReferenceHeight = 1080f;

    public static float Factor => Forced > 0f ? Forced : Mathf.Sqrt(Screen.width / ReferenceWidth * (Screen.height / ReferenceHeight));

    public static float Forced { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Forced = 0f;
    }

    public static float Width => Screen.width / Factor;

    public static float Height => Screen.height / Factor;

    public static void Apply() => GUI.matrix = Matrix4x4.Scale(new Vector3(Factor, Factor, 1f));

    public static Matrix4x4 TurnedAbout(Matrix4x4 matrix, float degrees, Vector2 pivot) =>
        matrix * Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, degrees)) * Matrix4x4.Translate(-pivot);
}
