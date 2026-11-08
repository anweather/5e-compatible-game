# punchy-sync

Brings the newest creatures from Punchy (Zachary Franklin's game, <https://github.com/Skeleman/Punchy>,
credited in `Scripts/Credits.cs`) into `rules/fauna.json` and `rules/parts.json`. Punchy has no model files:
a creature is a species `.asset` and the limb `.asset`s it hangs together (`Assets/Game/Fauna/**`, YAML
linked by GUID through the `.meta` files). This reads those and writes them as our rows, the same way the
one-off `CreatureRowExport` did at Punchy `c52d9ff` (it reproduces every row of that export exactly).

## Run it

Needs the .NET 8 SDK and `git`, on Linux, Mac or Windows. From the repo root:

```sh
dotnet run --project tools/punchy-sync              # report only: writes nothing to rules/
dotnet run --project tools/punchy-sync -- --apply   # write the rows, and move synced.json on
```

The first run clones Punchy (about 1 GB of history, no checkout) into your local app-data folder
(`~/.local/share/punchy-sync/Punchy`, `%LOCALAPPDATA%\punchy-sync\Punchy`); later runs fetch. It reads
files straight from git, at two commits:

- **base**, the commit we last synced (`synced.json`), and
- **theirs**, Punchy's `origin/HEAD` now.

Options: `--punchy <dir>` an existing clone, `--rev <commit>` instead of `origin/HEAD`, `--base <commit>`,
`--out <dir>` where the candidate rows and the report go (default: your temp folder's `punchy-sync/`),
`--repo <dir>` our repo if not run inside it.

## What it does to our rows

Each row is merged three ways, field by field (a slot, a segment, a number), with base, ours and theirs:

- a field upstream changed and we didn't: **takes theirs** (listed under "Changed upstream");
- a field we changed and upstream didn't: **keeps ours**;
- both changed it differently: **keeps ours**, listed under "Conflicts" for a person to decide;
- `spawn` is always ours (where and how many come out is the valley's), and rows that were never Punchy's
  (lily pads, reeds) are left alone;
- a new species comes in as a new row at the end, with no `spawn`, so it isn't placed anywhere until
  someone gives it one;
- a new part comes in only if a species we have wears it;
- a species whose row upstream has gone stays, listed.

A new species is **left out** if it needs code we haven't ported: flight and wings, tunnelling, rooted
plants, ghosts, lava, breaching, passing for a plant, cave-light moths, hawking, or a variant that sets its
own head, thickness or legs. Code stays hand-ported (`unity/Assets/Prototype/ThirdParty/Punchy/README.md`);
once it's in, take the field off the list in `Export.cs` and run again. Fields a species works without
(calls, idle habits, gallop styles, climbing, webs, burrows, guards' kit...) aren't carried; the report
counts them.

Read the report before committing: then build and test the core (`dotnet test core`), since the rule checks
refuse a row the view can't read, and look at the new creatures in the Editor.

After `--apply`, `synced.json` names the commit you synced to; commit it with the rows, so the next run
starts from there. Anything left out or in conflict is reported again each run, while it still differs.
