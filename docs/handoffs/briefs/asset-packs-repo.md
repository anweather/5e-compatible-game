# Brief for the PC: the Asset Store packs in a private repo, for CI releases (2026-11-23)

For a Claude Code session on the PC (Remote Control or local), with Andrew there for the steps marked **Andrew**.
Read `docs/CI_BUILDS.md` ("Revisited 2026-11-23") and J-049 first. Work on `dev`: `git fetch origin`, `git switch dev`,
`git merge --ff-only origin/dev`.

**Goal:** GitHub's own Ubuntu runners can build the Windows and Linux players (GameCI) once they can get the five
Asset Store packs in `unity/AssetPacks.json`. The packs aren't in git (not redistributable) and come from Andrew's
Unity account, so they go into a **private** repo the release workflow reads with a read-only token. This brief
makes that repo, proves the packs import in batch mode, and reports back. The release workflow (`release.yml`)
is written after, by the cloud session that wrote this brief, so don't write it here.

**Why the PC:** it has the packs already, in the Editor's Asset Store cache, `%APPDATA%\Unity\Asset Store-5.x\`
(`AssetPackRestore.CacheRoot`). Each pack's file is that folder plus its `cache` path in `AssetPacks.json`:

| Pack (`AssetPacks.json` `id`) | File under `Asset Store-5.x\` |
|---|---|
| 196526 Starter Assets (character controllers) | `Unity Technologies/3D ModelsCharacters/First Person Third Person Character Controllers.unitypackage` |
| 127325 Particle Pack | `Unity Technologies/Particle Systems/Particle Pack Starter Assets.unitypackage` |
| 145808 Terrain Asset Pack | `Unity Technologies/3D ModelsEnvironmentsLandscapes/Terrain Asset Pack Terrain Sample.unitypackage` |
| 213197 URP Terrain sample | `Unity Technologies/3D ModelsEnvironmentsLandscapes/Unity URP Terrain Terrain Sample Project.unitypackage` |
| 155776 Free Sound Effects Pack | `Olivier Girardot/AudioSound FX/Free Sound Effects Pack Starter - All categories.unitypackage` |

If a file is missing, run Prototype > Restore Asset Packs in the Editor (it downloads into the cache) and look again.
Never edit the packs.

## Steps

1. **Licences.** For each pack, read the licence that came with it (a `license.txt`, `LICENSE.md` or
   `Third-Party Notice.txt` in its folder under `unity/Assets/`, or its Asset Store page) and note which it is:
   the standard Asset Store EULA, the Unity Companion License, or other. Some Starter Assets listings say Unity
   Companion License and the 196526 page says "Non standard EULA". Report each one; Andrew decides. Don't put
   any pack in a public place, whatever it says.
2. **Andrew: create the repo.** github.com > New repository: owner `anweather`, name `5e-asset-packs`,
   **Private**, empty (no README). If `gh` is signed in as Andrew, the session may run
   `gh repo create anweather/5e-asset-packs --private` instead, but only once Andrew says yes in this session.
3. **Fill it.** Clone it beside the game's repo (not inside it). Keep the cache's layout, so CI can lay the
   folder out as the Editor's cache and `AssetPackRestore` finds each file by its `cache` path:
   ```sh
   git lfs install
   git lfs track "*.unitypackage"
   # copy each file above to the same relative path in the clone, e.g.
   #   "Unity Technologies/Particle Systems/Particle Pack Starter Assets.unitypackage"
   ```
   Add a `README.md`: what this is, "private: Asset Store packs licensed to Andrew; never make this public",
   and the table above with each pack's version from `AssetPacks.json` and its licence from step 1. Commit
   (`.gitattributes`, the packs, the README) and push to `main`. Check the files went up through LFS
   (`git lfs ls-files` lists all five) and each one's size matches its copy in the cache.
4. **A batch-mode restore** (a branch off `dev`, `asset-packs-batch`). The Windows and Linux command-line builds
   (`PrototypeBuild.BuildWindowsFromCommandLine`, `LinuxBuild.FromCommandLine`) stop if a pack is missing, and
   the only way to restore today is the menu item, which runs over Editor updates. Add
   `AssetPackRestore.FromCommandLine` (`-executeMethod`): it imports every missing pack from the cache
   (honouring each pack's `include`), touches no network, logs `ASSET PACKS: OK` or `ASSET PACKS: FAILED <what>`,
   and exits 0 or 1. An optional `-asset-cache <folder>` reads the packs from there instead of `CacheRoot`, so
   CI can point it at its checkout of `5e-asset-packs`. Test it in a **fresh clone of the game's repo** (not the
   project the Editor has open), with no Editor open on that clone:
   ```sh
   "C:/Program Files/Unity/Hub/Editor/6000.3.27f1/Editor/Unity.exe" -batchmode -nographics -quit \
     -projectPath <fresh clone>/unity -executeMethod AssetPackRestore.FromCommandLine \
     -asset-cache <clone of 5e-asset-packs> -logFile restore.log
   ```
   Then, in that same clone, `PrototypeBuild.BuildWindowsFromCommandLine -rebuild-yard` (`docs/RELEASING.md`,
   "The Windows player with no Editor window") must end `HEADLESS BUILD: OK`. Note how long each took and the
   clone's size with `Library/`. Gates: `dotnet build core -c Release -warnaserror`, `dotnet test core
   --filter TestCategory=Unit`, the EditMode tests for what you changed. Merge into `dev` and push when green.
5. **Linux Build Support.** Releases have no Mac now (J-049), and the Mac built Linux. Say whether the PC's
   6000.3.27f1 has Linux Build Support (Mono) (Unity Hub > Installs > the gear > Add modules). Don't install it
   without Andrew's yes.
6. **Andrew: the secrets.** Nothing for the session to type: tokens and passwords are Andrew's alone. In the
   game repo's Settings > Secrets and variables > Actions, Andrew adds:
   - `ASSET_PACKS_TOKEN`: a fine-grained personal access token, repository access only
     `anweather/5e-asset-packs`, permission Contents: read-only.
   - The Unity licence for GameCI: on Personal, `UNITY_LICENSE` (the `.ulf` file's contents, from a one-time
     activation in Unity Hub), `UNITY_EMAIL`, `UNITY_PASSWORD`; on Pro, `UNITY_SERIAL` in place of
     `UNITY_LICENSE`. GameCI's docs: https://game.ci/docs/github/activation. A password with special characters
     has caused trouble there.
   The session can check the secrets' names exist (`gh secret list -R anweather/5e-compatible-game`), never
   their values.

## Report

One short message, back to the cloud session or to Andrew:
- each pack's licence (step 1);
- the private repo's URL and `git lfs ls-files` (step 3);
- `asset-packs-batch`'s merge commit on `dev`, the restore and build results with their times and sizes (step 4);
- whether the PC has Linux Build Support (step 5);
- which secret names exist (step 6).

Ask before doing anything much larger than this brief. Stop and say so if a pack's licence forbids even a private copy.
