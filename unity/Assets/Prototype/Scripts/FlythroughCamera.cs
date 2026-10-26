using UnityEngine;

// A scripted camera flight for showing off the scene: glides the main camera
// through a list of waypoints (position plus a point to look at), easing
// between them, then reports Done. Takes the camera from Cinemachine while
// it flies.
public class FlythroughCamera : MonoBehaviour
{
    [System.Serializable]
    public struct Waypoint
    {
        public Vector3 position;
        public Vector3 lookAt;
        public float seconds;

        /// <summary>A waypoint at <paramref name="position"/> looking at <paramref name="lookAt"/>, reached after <paramref name="seconds"/>.</summary>
        public static Waypoint At(Vector3 position, Vector3 lookAt, float seconds) =>
            new Waypoint { position = position, lookAt = lookAt, seconds = seconds };
    }

    /// <summary>
    /// Starts a flight from wherever the main camera is now, looking at <paramref name="lookingAt"/>,
    /// through <paramref name="waypoints"/>, so one shot glides into the next.
    /// </summary>
    public static FlythroughCamera FromHere(Vector3 lookingAt, params Waypoint[] waypoints)
    {
        var all = new Waypoint[waypoints.Length + 1];
        all[0] = Waypoint.At(Camera.main.transform.position, lookingAt, 0f);
        waypoints.CopyTo(all, 1);
        var flight = new GameObject("Flythrough").AddComponent<FlythroughCamera>();
        flight.waypoints = all;
        return flight;
    }

    public Waypoint[] waypoints;
    public bool Done { get; private set; }

    Transform cam;
    int index;
    float t;

    void Start()
    {
        var main = Camera.main;
        cam = main.transform;
        if (main.TryGetComponent<Unity.Cinemachine.CinemachineBrain>(out var brain)) brain.enabled = false;
        Place(waypoints[0], waypoints[0], 0f);
    }

    void LateUpdate()
    {
        if (Done) return;
        if (index >= waypoints.Length - 1)
        {
            Done = true;
            return;
        }
        var from = waypoints[index];
        var to = waypoints[index + 1];
        t += Time.deltaTime / Mathf.Max(0.01f, to.seconds);
        Place(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
        if (t < 1f) return;
        index++;
        t = 0f;
    }

    void Place(Waypoint from, Waypoint to, float k)
    {
        cam.position = Vector3.Lerp(from.position, to.position, k);
        cam.rotation = Quaternion.LookRotation(Vector3.Lerp(from.lookAt, to.lookAt, k) - cam.position);
    }
}
