Needs: core
Area: tools/punchy-sync/ (new), rules/fauna.json and rules/parts.json (new rows only, through the rules MCP where it can),
ThirdParty/Punchy/README.md, core tests
Branch: punchy-sync

# Pull Punchy's latest creatures, and a script to do it again (Andrew, 2026-11-08, W4; given to u2)

Andrew: "go get some more models of creatures from the Punchy game, pull the most recent versions of those creatures
and pour them in ... create a script to do that ... on demand ... I don't wanna spend a ton of time on this."
We may borrow from Punchy (Zachary Franklin's game, https://github.com/Skeleman/Punchy) with credit (already in
`Credits.cs`). Read `docs/design/world-nine-options.md` "W4" first: Punchy has no model files; its creatures are about
58 species and 75 limb `.asset` YAML files under `Assets/Game/Fauna/**`, linked by GUID through `.meta` files, drawn as
procedural bodies. Ours are rows in `rules/fauna.json` (68 species) and `rules/parts.json`, exported once at `c52d9ff`.

1. **The script**, `tools/punchy-sync` (a small .NET tool or a script that runs on Linux, Mac and Windows): fetch or clone
   Punchy at its newest commit, read the species and limb assets (resolving GUIDs), write candidate rows, and print a
   diff against ours (new species, changed numbers). It never overwrites our own rows or our edits (lily pads, reeds,
   raider sizes); it reports conflicts. A README says how to run it.
2. **Run it now** and bring in what's new or changed upstream: new species and limbs as new rows; changed numbers on
   rows we haven't edited. List each in the report. If a creature needs code we don't have (a new gait or animator
   feature), leave it out and list it; code stays hand-ported (`ThirdParty/Punchy/README.md`).
3. The valley's `MapCheck` and the rule checks must pass; new species don't have to be placed in the valley.

Before landing: `dotnet build core -c Release -warnaserror`, `dotnet test core` twice in full in the foreground (the
runner closes a session that only waits), `dotnet format`. Push the branch only, remove this claim on the branch in the
last commit. The PC compiles it and looks at the new creatures; the lead lands it.

After (2026-11-08, try 1): the watcher stopped u2 at 1.09 GB free on the PC (the lead's landing gates ran beside the
Editor). The work in progress is on `wip/u2-punchy-sync`. Continue from it, on branch `punchy-sync`.
