using System.Collections;
using UnityEngine;

// A one-take showcase: a camera flight into the valley that ends behind the
// wizard, then the scripted spell demo with the gameplay camera. Used for
// trailer recordings (DemoRecorder.RecordUntil).
public class Showcase : MonoBehaviour
{
    public bool Done { get; private set; }

    void OnDestroy() => EventLog.HudVisible = true;

    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    static FlythroughCamera.Waypoint W(Vector3 position, Vector3 lookAt, float seconds) =>
        new FlythroughCamera.Waypoint { position = position, lookAt = lookAt, seconds = seconds };

    IEnumerator Start()
    {
        EventLog.HudVisible = false;
        var grid = FindFirstObjectByType<ChemGrid>();
        var wizard = WizardRig.FindLocal().transform;
        var yard = grid.YardCellCenter(13, 7);
        var behindWizard = wizard.position + V(0.9f, 2.3f, -4.2f);

        // High over the lake with the mountains ahead, down across the water,
        // past the cabin, and into the yard behind the wizard.
        var flight = new GameObject("Flythrough").AddComponent<FlythroughCamera>();
        flight.waypoints = new[]
        {
            W(V(30f, 60f, -330f), V(20f, 90f, 400f), 0f),
            W(V(35f, 40f, -200f), V(25f, 10f, -40f), 6f),
            W(V(-40f, 18f, -20f), V(-16f, 3f, 15f), 5f),
            W(V(-8f, 10f, 0f), yard, 4f),
            W(behindWizard, wizard.position + V(0f, 1.4f, 8f), 3.5f),
        };
        yield return new WaitUntil(() => flight.Done);

        // Hand over to the gameplay camera, then play the spell demo.
        if (Camera.main.TryGetComponent<Unity.Cinemachine.CinemachineBrain>(out var brain)) brain.enabled = true;
        Destroy(flight.gameObject);
        var demo = new GameObject("DemoDirector").AddComponent<DemoDirector>();
        yield return new WaitUntil(() => demo.Done);
        EventLog.HudVisible = true;
        Done = true;
    }
}
