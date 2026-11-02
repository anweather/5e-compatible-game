using System.Collections;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

// The probe's people scenario (docs/FAST_LOOP.md): the peoples from Punchy's individuals work. At
// Hollowmere at midday the folk come out and workers go to their posts, then a line of humans (each
// with its own hair, build and way of walking), a child, a dwarf, an elf, a goblin and an ogre walks by.
public class PeopleProbe : ClipProbe
{
    static readonly string[] Line = { "human", "human-child", "dwarf", "human", "elf", "goblin", "human", "ogre", "human" };
    const float Spacing = 1.5f, Pace = 0.9f, FilmSeconds = 8f;
    const string Clear = "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 }";

    protected override IEnumerator Run()
    {
        var village = FindObjectsByType<Village>(FindObjectsSortMode.None).FirstOrDefault(v => v.villageName == "Hollowmere");
        var fauna = FindAnyObjectByType<ValleyFauna>();
        Check("village_found", village != null && fauna != null, "Hollowmere");
        if (village == null || fauna == null || string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", ScreenshotDir != null, "people films into --shots <folder>");
            yield break;
        }
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Clear + " }");

        var square = village.transform.position + village.wellAt;
        var stand = square + new Vector3(3f, 0f, -3f);
        Teleport(new Vector3(stand.x, grid.GroundAt(stand) + 0.1f, stand.z));
        Cut(square + new Vector3(0f, 12f, -14f), square);
        int working = 0;
        for (float t = 0f; t < 25f && working < 2; t += 1f)
        {
            yield return Wait(1f);
            fauna.FolkKinds(village, out working);
        }
        var kinds = fauna.FolkKinds(village, out working);
        Metric("workers", working);
        Check("workers_at_posts", working >= 2, string.Join(", ", kinds));
        Shot("village");

        // A line across the open south of the square, walking east, with the wizard out of the way.
        var clear = square + new Vector3(9f, 0f, 5f);
        Teleport(new Vector3(clear.x, grid.GroundAt(clear) + 0.1f, clear.z));
        var start = square + new Vector3(6f, 0f, -2.5f);
        var heights = new float[Line.Length];
        var ids = new int[Line.Length];
        for (int i = 0; i < Line.Length; i++)
        {
            int kind = fauna.SpeciesIndex(Line[i]);
            Check("row_" + Line[i], kind >= 0, Line[i]);
            if (kind < 0) yield break;
            var p = start - new Vector3(i * Spacing, 0f, 0f);
            int index = fauna.Spawn(kind, new float3(p.x, grid.GroundAt(p), p.z), math.PI / 2f);
            fauna.Puppet(index, math.PI / 2f, Pace);
            ids[i] = fauna.Id(index);
            heights[i] = fauna.Body(kind).StandHeight * fauna.State(index).Scale;
        }
        Check("child_is_small", heights[1] < heights[0] * 0.8f, $"{heights[1]:0.00} m to {heights[0]:0.00} m");
        Check("ogre_is_big", heights[7] > heights[0], $"{heights[7]:0.00} m to {heights[0]:0.00} m");

        var mid = square + new Vector3(1f, 0.8f, -2.5f);
        Cut(mid + new Vector3(0f, 1.6f, -4.5f), mid);
        yield return Wait(0.5f);
        Shot("line");
        StartFilm();
        yield return Wait(FilmSeconds);
        yield return EndFilm(sounds: false);
    }
}
