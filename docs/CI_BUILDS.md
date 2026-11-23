# Unity builds in GitHub Actions: the plan

Status: shelved 2026-10-31 (Andrew: builds by hand for now); revisited 2026-11-23, below. Goal: player builds and release candidates run from GitHub, not by hand on the
PC, so the PC stays free and a release is one button.

## What decides it

- **The repo is public.** GitHub-hosted runners are free for public repos, but any workflow that runs on a
  pull request runs a stranger's code. Builds must never trigger on `pull_request`.
- **The Asset Store packs aren't in git** (`unity/.gitignore`: "not redistributable"; restored with
  Prototype > Restore Asset Packs from Andrew's Unity account). A GitHub-hosted runner can't get them
  without putting them somewhere public or handing it Andrew's account. Without them the yard and players
  don't build.
- **A Unity licence** is needed for batch mode. On a machine where Andrew is already signed in to Unity, it's
  there. On GitHub's machines it's a secret only Andrew can enter.
- **The Windows player uses Mono**, so the Mac can build it (being proven now on `headless-build`).

## Revisited 2026-11-23: GitHub-hosted, no Mac

Andrew dropped macOS (J-049) and asked for a release cut by committing to `main`. With no macOS player, every
build is Windows (Mono) and Linux, and both build on GitHub's own Ubuntu runners with GameCI
(`game-ci/unity-builder`): a Linux runner makes a Windows Mono player, and GameCI has images for this exact
Editor (`unityci/editor:ubuntu-6000.3.27f1-windows-mono-4`, `...-base-4` for Linux Mono). No machine of ours
needs to be on.

What it takes, in order:

1. **The Asset Store packs** (the blocker): a private, licence-compliant copy the workflow can read, such as a
   private repo or private release asset of the `.unitypackage` files in `unity/AssetPacks.json`, fetched with a
   read-only token secret and imported before the build. Andrew's call: whether that copy meets the packs'
   licence.
2. **A Unity licence as secrets** (Andrew enters them): Personal is `UNITY_LICENSE` (the `.ulf` from a one-time
   activation in Unity Hub), `UNITY_EMAIL`, `UNITY_PASSWORD`; Pro is `UNITY_SERIAL` instead of the `.ulf`.
3. **Disk:** a hosted runner has about 14 GB free; free space first (`jlumbroso/free-disk-space`) and cache
   `Library/` per target (`actions/cache`, keyed on `Assets/`, `Packages/`, `ProjectSettings/`). A cold build
   compiles every shader (35 to 47 minutes on the Mac); a warm cache much less.
4. **`release.yml`:** on push to `main` (and a manual button), never on `pull_request` (the repo is public).
   Next version from the latest `alpha-*` tag, stamped with `-build-version` (no ProjectSettings commit), the
   core gates, Windows and Linux in parallel, `vpk "[win]" pack` for the Windows half (cross-platform), all
   onto one **draft** with `GITHUB_TOKEN`. Publish stays a click until the draft's checks are trusted.

What still needs a person: the RC's install and online checks (`docs/RELEASING.md` step 6).

**As built (2026-11-23): `.github/workflows/release.yml`.** Each push to `main` (not docs-only) or the Run workflow
button makes the draft `alpha-X.Y.Z`, the next patch after the newest `alpha-*` tag (or the button's version).
Skipped when a release already targets the commit, or the commit message says `[skip release]`.

- **plan** picks the version; **gates** runs the core build (warnings fail) and the Unit tests.
- **build**, Windows and Linux at once, on `ubuntu-latest`: free disk space, check out the game and
  `anweather/5e-asset-packs` (`ASSET_PACKS_TOKEN`; its LFS objects cached by their ids), lay the packs into the project
  with `tools/ci/unpack-asset-packs.py` (no Unity: `ImportPackage` doesn't finish under `-quit`), restore the cached
  `Library/`, then Unity in GameCI's image (`unityci/editor:ubuntu-<version>-windows-mono-4.0.0`, `-linux-il2cpp-`) runs
  `PrototypeBuild.BuildWindowsFromCommandLine` or `LinuxBuild.FromCommandLine` with `-build-version`, under xvfb
  (no `-nographics`). The log is an artifact either way.
- **draft**: `tools/ship-update.sh X.Y.Z --windows-only --skip-build` (Velopack 1.2.161, the delta from the last
  published release), then `--linux-only --skip-build` (the zip), into one draft. Then test and publish by hand.

**Not proven yet (the first runs will say):**
- **Tree impostors without a GPU.** On the Mac a `-nographics` build fails baking them (`RenderTexture.Create
  failed`). Here Unity has xvfb's software GL, which may bake them or may not. If not, bake them once on a machine
  with a GPU and commit `Assets/Prototype/Generated/Resources/TreeImpostors/` (with its `.meta` files) to the packs
  repo as `impostors/`: the workflow copies that folder in when it's there. They're made from pack trees, so they
  never go in this public repo. Bake again when the trees or `TreeImpostorBaker.BakeVersion` change.
- **The licence in a container:** the Personal `.ulf` is written where Unity looks
  (`/root/.local/share/unity3d/Unity/Unity_lic.ulf`), as GameCI's builder does; Pro activates with the serial
  and returns it after.
- **Time and space:** a cold build compiled shaders for 42 minutes on the Mac; a runner is slower. The job's limit
  is 3 hours. The repo's cache (10 GB) holds the packs' LFS objects (3.1 GB) and one `Library/` a platform; GitHub
  evicts the oldest when it's full.
- **LFS bandwidth:** a checkout of the packs without the cache is 3.1 GB of the account's LFS bandwidth.

## The earlier plan: a self-hosted runner on the Mac (superseded)

The Mac already has Unity, the packs, a warm `Library/` and a GPU. GitHub Actions just tells it when to build.

1. **Headless build entry point** (the Mac, in progress): `Unity -batchmode -nographics -executeMethod`
   builds Windows (and macOS) players with no Editor window.
2. **Register the runner** (Andrew, about 5 minutes): GitHub repo settings > Actions > Runners > New
   self-hosted runner > macOS; run the commands it shows on the Mac, with the label `unity-mac`, and install
   it as a service. The registration token is Andrew's to paste; no agent types it.
3. **Lock it down** (the PM, in the repo): builds run only on `workflow_dispatch` (a button) and on pushed
   tags `alpha-*`, never on pull requests; Settings > Actions: require approval for outside contributors.
4. **`build.yml`** (the Mac writes it, the PM reviews):
   - check out (with LFS);
   - the gates: core, EditMode tests, smoke;
   - build Windows and macOS headless;
   - pack with Velopack;
   - upload to a **draft** GitHub release with the built-in `GITHUB_TOKEN`;
   - attach the logs. One job at a time on the Mac (`concurrency`).
5. **What stays on the PC:** online probes (a second player), and the last check: install 0.1.x from the
   draft on Windows and update from the previous one. Short, with no Editor. Publishing stays the PM's,
   after that check (the release rule: publish if green).

**Cost:** no minutes billed (self-hosted). The Mac must be on and awake for a build. LFS bandwidth is small
(46 files, under 2 MB each).

## Later, if wanted

- **GitHub-hosted Windows builds** (GameCI `unity-builder`): needs the packs solved (a private,
  licence-compliant source) and Andrew's Unity licence as repo secrets. Runners have 7 GB of RAM and 14 GB of
  disk, tight for this project; larger runners cost per minute.
- **A Windows self-hosted runner on the PC** for the install check and online probes, run only when the PC is
  free.
