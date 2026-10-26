# The Unity project

Unity **6000.3.27f1** (6.3 LTS, URP), the version in
`ProjectSettings/ProjectVersion.txt`. Never open the project with another
Editor version: it upgrades the project. Setup, and the Mac/Windows
differences for agents, are in `docs/DEV_SETUP_WINDOWS.md`.

## Driving the open Editor

Use the `unity` CLI against the running Editor (the `com.unity.pipeline`
package is in the manifest). Pass `--project-path` (or set
`UNITY_PROJECT_PATH`) to this folder's full path.

```sh
unity status --until-ready                      # wait out imports and domain reloads
unity command editor invoke_menu --path "Prototype/Build Chemistry Yard"
unity command test run --mode EditMode --result-only
unity command test run --mode EditMode --filter <TestName> --result-only
unity command console status --result-only      # compile failure flag, error and warning counts
unity command console get --level error --result-only
unity command script eval -- --code '<C#>'      # read or poke live state
```

Menu items worth knowing (all under **Prototype**): Restore Asset Packs,
Build Chemistry Yard (also builds the valley), Build > Windows / macOS
(player builds into `Builds/`; slow, run only when asked), Record Valley
Showcase.

## The probe

`tools/probe.sh <scenario>` (smoke, fire, perf, online, ...) runs a scripted
scenario in Play mode and prints JSON with checks, metrics, frame times and
console errors (`docs/FAST_LOOP.md`). The Editor must be on
`Assets/Prototype/Scenes/ChemistryYard.unity` with no compile errors. On
Windows run it from Git Bash.

## Rules for the Editor

- **Never kill Unity, never disable audio**, and never accept a licence or
  legal dialog for the user. If a modal appears, read its text before
  clicking anything; only dismiss harmless info.
- Never revert, checkout or stash a scene or prefab the Editor has open.
  Switch the Editor to a new empty scene first, or rebuild with the menu.
- Driving Play mode while the Editor is unfocused: call
  `editor set_auto_tick` and `editor.playmode set_run_in_background`
  first, and put the latter back afterwards.
- `runtime simulate_key` and `simulate_pointer` work for held keys (WASD),
  but press-type actions (`WasPressedThisFrame`: number keys, click, Tab,
  M, Esc) don't fire from them. Call the game's own methods through
  `script eval` instead (`SpellCaster.Select` / `Cast`, `ModsPanel.SetOpen`,
  `PauseMenu.SetOpen`), or use the probe.
- `capture game_view --save_path` writes under `Assets/`; move the files
  out and delete the folder with `AssetDatabase.DeleteAsset` afterwards.

## What's in git and what isn't

- The yard scene isn't: `ChemistryYard.unity` and its `.meta` are ignored.
  `YardAutoBuild` builds it whenever it's missing (once the packs are
  there), and `ChemYardBuilder` writes the `.meta` with a fixed GUID so the
  build settings never change. Netcode's `DefaultNetworkPrefabs.asset`
  generation is off for the same reason (and the file is ignored); the
  `NetworkManager` uses its own prefab lists.
- **After a pull that changes `ProjectSettings/`, restart the Editor.** A
  running Editor keeps project settings in memory and can write its old
  values back over the pulled ones the next time it saves them.

- Asset Store packs aren't: `AssetPacks.json` lists them and **Restore
  Asset Packs** fetches them (the signed-in account must own them). Never
  edit pack folders (`Starter Assets`, `UnityTechnologies`,
  `TerrainSampleAssets`, `TerrainDemoScene_URP`) or anything under
  `Generated/`, `Builds/` or `Recordings/`.
- Known pack noise: importing the URP terrain pack logs a
  `NullReferenceException` for `SpeedTreeURP_Branch.shadergraph`; ignore it.
- Our code is all under `Assets/Prototype/`; analyzer warnings show in the
  Unity console with the same rule severities CI uses
  (`Assets/Default.globalconfig`, `docs/QUALITY.md`).
