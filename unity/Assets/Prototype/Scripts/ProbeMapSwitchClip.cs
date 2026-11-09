using System.Collections;
using UnityEngine;

// The probe's mapswitchclip scenario: films switching from the valley to the Drowned Chain in play (maps phase 8), for
// the map-switch clip. A few steps on the valley, the Esc menu's Play together page with the Chain picked as the map to
// host, the switch (ChemGrid.PlayMap, as hosting on another map does), then a walk along the Chain's beach and a look
// over its islands. Filmed with the HUD (ClipFilm reads the screen, which lives through the scene's reload).
// Needs --shots <folder>: the frames and sound.wav, for ffmpeg.
public class MapSwitchClipProbe : ProbeScenario
{
    const string Chain = "drowned-chain";

    protected override IEnumerator Run()
    {
        if (string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "mapswitchclip films into --shots <folder>");
            yield break;
        }
        DontDestroyOnLoad(gameObject);
        var film = new ClipFilm(this, ScreenshotDir, withHud: true);
        film.Roll();

        var start = MapLand.Current.Map.Spawn;
        yield return WalkFor(start.x + 12 - grid.yardCells.x, start.y - grid.yardCells.y, 3f);

        var menu = PauseMenu.Instance;
        menu.SetOpen(true);
        menu.ShowPage(PauseMenu.PageOf(PauseMenu.Submenu.Multiplayer));
        yield return Wait(1.2f);
        NetSession.HostMap = Chain;
        yield return Wait(2f);
        menu.SetOpen(false);

        var was = grid;
        ChemGrid.PlayMap(Chain);
        for (float waited = 0f; waited < 60f; waited += Time.unscaledDeltaTime)
        {
            yield return null;
            var now = ChemGrid.Current?.Grid;
            if (now != null && now != was && now.ChunksLeftToDraw == 0 && WizardRig.FindLocal() != null) break;
        }
        Rebind();
        Check("switched", ChemGrid.MapId == Chain, $"on {ChemGrid.MapId}");
        yield return Wait(1f);

        var spawn = MapLand.Current.Map.Spawn;
        yield return WalkFor(spawn.x + 24 - grid.yardCells.x, spawn.y + 2 - grid.yardCells.y, 9f);
        Look(spawn.x - 70, spawn.y + 60, 55f, spawn.x + 10, spawn.y, 0f);
        yield return Wait(5f);

        yield return film.Stop();
        Metric("frames", film.Frames);
        Metric("seconds", film.Seconds);
        Check("filmed", film.Saved >= film.Frames - 2 && film.Seconds > 15f, $"{film.Saved}/{film.Frames} frames, {film.Seconds:0.0} s");
    }
}
