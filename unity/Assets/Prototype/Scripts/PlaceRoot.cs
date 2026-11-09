using System;
using System.IO;
using Chem;
using UnityEngine;

// One of a map's places that stays code (docs/design/maps-as-data.md, section 4: the court, the bridge, the falls, the
// coast...), built by the yard build into a root of its own. map.json's "dresses" names the places a map wants; a root
// whose place isn't named switches itself off as the scene starts, so nothing of one map stands on another. Early, so
// nothing under a root that goes starts first.
[DefaultExecutionOrder(-900)]
public sealed class PlaceRoot : MonoBehaviour
{
    [Tooltip("The place's name in map.json's dresses (lake, court, bridge, ...).")]
    public string place;

    void Awake()
    {
        // On a generated map the valley's own places stand where the valley lies in it (PinnedValley), once.
        if (PinnedValley.InGenerated && Array.IndexOf(PinnedValley.Dresses, place) >= 0)
        {
            if (!shifted) transform.position += PinnedValley.Shift;
            shifted = true;
            return;
        }
        if (Array.IndexOf(Dressed(), place) < 0) gameObject.SetActive(false);
    }

    // Whether this root has been moved to the valley's place on a generated map (Play mode only: the scene isn't saved).
    bool shifted;

    /// <summary>The places the current map dresses (none if its map.json can't be read).</summary>
    public static string[] Dressed()
    {
        try
        {
            return MapFile.Load(ChemGrid.Folders.World).Dresses;
        }
        catch (Exception e) when (e is IOException || e is Newtonsoft.Json.JsonException || e is NullReferenceException || e is InvalidCastException)
        {
            Debug.LogError($"The map's {MapFile.FileName} can't be read, so it dresses no places: {e.Message}");
            return Array.Empty<string>();
        }
    }
}
