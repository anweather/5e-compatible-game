# Unity builds in GitHub Actions: the plan

Status: shelved, 2026-10-31 (Andrew: builds stay by hand for now; revisit later). Goal: player builds and release candidates run from GitHub, not by hand on the
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

## The plan: a self-hosted runner on the Mac

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
