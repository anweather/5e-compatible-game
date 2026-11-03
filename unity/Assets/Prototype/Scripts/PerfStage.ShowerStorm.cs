using System.Collections;
using Chem;
using UnityEngine;

// The showerstorm scenario (issue #70): Andrew's evening, over Hollowmere's square with the wizard
// standing in it and the camera still. His meteor mod's shower for a minute and a quarter of an hour's
// quiet after it, all before measuring starts, then the storm that followed (as the showerstorm probe
// has it), from the start of the measured seconds; -perf-no-rain leaves the sky clear after the shower,
// for what the shower alone leaves behind.
public partial class PerfStage
{
    const float ShowerSeconds = 60f, AfterShowerSeconds = 15f;

    IEnumerator ShowerStorm()
    {
        Hollowmere();
        var switcher = grid.GetComponent<RuleSwitcher>();
        switcher.Ask(RoomCommand.Apply(switcher.Me, "meteors", WaterLookProbe.Meteors, "Constant meteor showers"));
        Debug.Log("PERF SHOWERSTORM shower on");
        yield return new WaitForSecondsRealtime(ShowerSeconds);
        switcher.Ask(RoomCommand.Revert(switcher.Me, "meteors"));
        yield return new WaitForSecondsRealtime(AfterShowerSeconds);
        if (!Asked("-perf-no-rain")) switcher.Ask(RoomCommand.Apply(switcher.Me, "storm", ShowerStormProbe.Storm, "Storm"));
        Debug.Log($"PERF SHOWERSTORM shower off, storm={!Asked("-perf-no-rain")} active_chunks={World.ActiveChunks.Count}");
    }
}
