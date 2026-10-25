using UnityEngine;

// A short-lived "over here" marker: a pulsing column of light at a spot in
// the world, and on screen a label over it, or an arrow at the screen's edge
// pointing the way when it is out of view. Sending stones use it.
public class PingMarker : MonoBehaviour
{
    const float ColumnHeight = 4f;

    string label;
    Color color;
    float until;
    Light glow;
    Material material;
    GUIStyle style, shadow;

    /// <summary>The marker most recently shown, or null.</summary>
    public static PingMarker Latest { get; private set; }

    public static PingMarker Show(Vector3 at, string label, Color color, float seconds)
    {
        var column = Primitives.Visual(PrimitiveType.Cylinder, "Ping");
        column.transform.position = at + Vector3.up * (ColumnHeight / 2f);
        column.transform.localScale = new Vector3(0.12f, ColumnHeight / 2f, 0.12f);
        var look = column.GetComponent<Renderer>();
        var material = UrpMaterials.NewLit("Ping", color, emission: color * 3f);
        look.sharedMaterial = material;
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var marker = column.AddComponent<PingMarker>();
        marker.material = material;
        marker.label = label;
        marker.color = color;
        marker.until = Time.time + seconds;
        marker.glow = new GameObject("Glow").AddComponent<Light>();
        marker.glow.transform.SetParent(column.transform, false);
        marker.glow.color = color;
        marker.glow.range = 6f;
        Latest = marker;
        return marker;
    }

    /// <summary>The spot it marks (the foot of the column).</summary>
    public Vector3 Spot => transform.position - Vector3.up * (ColumnHeight / 2f);

    void Update()
    {
        if (Time.time >= until)
        {
            Destroy(gameObject);
            return;
        }
        glow.intensity = 2f + 1.5f * Mathf.Sin(Time.time * 6f);
    }

    void OnDestroy()
    {
        Destroy(material);
        if (Latest == this) Latest = null;
    }

    void OnGUI()
    {
        var cam = Camera.main;
        if (!EventLog.HudVisible || cam == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = color;
            shadow = new GUIStyle(style);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
        }
        var p = cam.WorldToScreenPoint(Spot + Vector3.up * ColumnHeight);
        var centre = new Vector2(Screen.width / 2f, Screen.height / 2f);
        // GUI y runs down; behind the camera the projection mirrors, so flip it back.
        var screen = new Vector2(p.x, Screen.height - p.y);
        if (p.z < 0f) screen = centre - (screen - centre);
        string text = $"<b>{label}</b>  {Mathf.RoundToInt(Vector3.Distance(cam.transform.position, Spot))} m";
        // Clear of the help lines at the top and the log at the bottom.
        var inset = new Rect(60, 100, Screen.width - 120, Screen.height - 240);
        if (p.z > 0f && inset.Contains(screen))
        {
            Label(new Rect(screen.x - 200, screen.y - 30, 400, 30), text);
            return;
        }
        // Off screen: an arrow on the edge pointing the way, the message just inside it. The edge is
        // kept below the Play Together panel (NetSession, top right, to about y 190) and centred.
        inset = new Rect(60, 200, Screen.width - 120, Screen.height - 400);
        var dir = (screen - centre).normalized;
        float scale = Mathf.Min(inset.width / 2f / Mathf.Max(1e-3f, Mathf.Abs(dir.x)), inset.height / 2f / Mathf.Max(1e-3f, Mathf.Abs(dir.y)));
        var edge = centre + dir * scale;
        var matrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, edge);
        Label(new Rect(edge.x - 30, edge.y - 25, 60, 50), "<size=44><b>→</b></size>");
        GUI.matrix = matrix;
        var at = edge - dir * 70f;
        Label(new Rect(at.x - 200, at.y - 15, 400, 30), text);
    }

    // With a dark shadow, so it reads against the sky and the grass.
    void Label(Rect r, string text)
    {
        GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, shadow);
        GUI.Label(r, text, style);
    }
}
