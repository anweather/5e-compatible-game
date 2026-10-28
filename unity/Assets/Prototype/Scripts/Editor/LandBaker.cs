using UnityEditor;
using UnityEngine;

// Prototype > Bake Valley Land: writes world/land.json, the valley's land under
// each cell in whole water units, from the terrain as the game draws it
// (ValleyChemistry.BakeLand), so water runs down the hills the same way on
// every peer. Needs Play mode (the valley's ground is bound there); commit the
// file afterwards. Rerun whenever the valley's shape changes.
static class LandBaker
{
    [MenuItem("Prototype/Bake Valley Land")]
    static void Bake()
    {
        var valley = Object.FindAnyObjectByType<ValleyChemistry>();
        if (!EditorApplication.isPlaying || valley == null)
        {
            Debug.LogWarning("Bake Valley Land: enter Play mode in the valley first");
            return;
        }
        Debug.Log("Bake Valley Land: " + valley.BakeLand());
    }
}
