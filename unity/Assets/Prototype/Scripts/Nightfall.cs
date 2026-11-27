using Chem;
using UnityEngine;

// The view's half of the night (backlog #8; the rules' half is World.Night): how lit the lamps are and how bright the
// stars are NightLamps', and this says once a night, when the wizard stands in the dark with no light of its own, how
// to see. AmbientCritters lights its fireflies by the hour.
public class Nightfall : MonoBehaviour
{
    const float OwnLightReach = 6f;

    ChemGrid grid;
    float nextLook;
    bool hinted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("Nightfall");
        DontDestroyOnLoad(go);
        go.AddComponent<Nightfall>();
    }

    void Update()
    {
        if (Time.unscaledTime < nextLook) return;
        nextLook = Time.unscaledTime + 0.5f;
        if (grid == null) grid = ChemGrid.Current?.Grid;
        var world = grid != null ? grid.World : null;
        var wizard = WizardRig.FindLocal();
        if (world == null || wizard == null) return;
        if (world.Daylight == LightLevel.Bright)
        {
            hinted = false;
            return;
        }
        if (hinted) return;
        var at = grid.CellAt(wizard.transform.position);
        if (world.LightAt(at.x, at.y) != LightLevel.Dark || OwnLight(wizard.transform.position)) return;
        hinted = true;
        EventLog.Add("It's dark: carry a lantern or a glowing ball, or cast Dancing Lights, to see by");
    }

    static bool OwnLight(Vector3 at)
    {
        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (light.enabled && light.type != LightType.Directional && Vector3.Distance(light.transform.position, at) < OwnLightReach)
                return true;
        return false;
    }
}
