---
name: release
description: Cut an alpha release of Spell Chemistry Valley and ship it to GitHub Releases as a pre-release. Use when Andrew says "cut a release", "ship 0.1.x", "release it", "publish the alpha", or asks to build the zip for a friend.
---

# Cut and ship an alpha

`docs/RELEASING.md` is the source of truth for what a release contains and
the exact commands; read it first. This skill is the order of work across
the two machines, and the stops where Andrew decides.

**Publishing is public and players' clients see it** (the in-game update
check reads published releases), so never publish without Andrew's explicit
go-ahead in this conversation. Drafts are invisible to players.

## 0. Agree the scope

- Name the version (`X.Y.Z`, next patch unless Andrew says otherwise) and
  the commit. Main moves fast (the PC pushes often), so pin the release to
  a SHA, don't say "latest main".
- Tell the other machine's session (SendMessage) that a release is being
  cut at that SHA, so it doesn't push in the middle of it, or pushes to a
  branch until the tag exists.

## 1. Main is ready (whichever machine is cutting it)

```sh
git branch --show-current && git status --short   # main; only _Recovery* untracked
git fetch origin && git merge --ff-only origin/main
gh run list --branch main --limit 5               # CI green on the commit
```

In the Editor: refresh first (`AssetDatabase.Refresh()`), wait for the
compile to finish, **then** Prototype > Build Chemistry Yard. A build run
before the new code loads rewrites tracked assets with stale content. After
it, `git status --short` must be clean again.

Probes, all must pass: `tools/probe.sh smoke`, `online`, and every scenario
the release touches (`menu`, `radial`, `sky`, ...; the known list is in the
probe's error message for an unknown name).

## 2. Bump the version (on main)

Set `bundleVersion` in `unity/ProjectSettings/ProjectSettings.asset` to
`X.Y.Z`. The running Editor holds project settings in memory, so also set
`PlayerSettings.bundleVersion = "X.Y.Z"` through `script eval` (or edit it
in Player Settings) so it can't write the old value back. Commit
`chore: version X.Y.Z` on main and push. That commit is the release commit;
record its **full 40-character SHA**.

The other machine: pull, then restart its Editor (a ProjectSettings change).

## 3. Build

- **Windows zip (the PC):** Prototype > Build > Release (Windows zip), or
  `PrototypeBuild.Release()`. It refuses a dirty tree. Check its log line
  `Release: OK, alpha X.Y.Z (<sha>)` names the release commit.
- **macOS app (the Mac), same commit,** for the two-machine online test:
  `PrototypeBuild.Build(BuildTarget.StandaloneOSX, "Builds/macOS/" + Application.productName + ".app")`.
  A first build after a target switch can fail on a SpeedTree shadergraph
  import; retry once.
- Both builds log `NET CONFIG: <hash>` at startup; the two hashes must
  match or joins are refused.

## 4. Draft on GitHub

Notes: `git log --no-merges --pretty='- %s' alpha-<previous>..HEAD`, then
rewrite them as a short player-facing list (what's new, what's fixed,
known issues, controls changes). Plain words, no commit-speak.

```sh
gh release create alpha-X.Y.Z unity/Builds/Release/SpellChemistryValley-alpha-X.Y.Z-windows.zip \
  --target <full 40-char SHA> --prerelease --draft \
  --title "alpha X.Y.Z" --notes-file <scratchpad>/notes.md
```

A short SHA in `--target` gives HTTP 422.

## 5. Test the draft (stop for Andrew)

- On the PC: download the zip **from the draft**, unzip into a fresh
  folder, run it. Windows warnings for the unsigned exe are expected (see
  RELEASING.md). Esc menu shows `alpha X.Y.Z (<sha>)`.
- Mac app hosts or joins the PC over Relay: `players=2`, `desync=False`,
  no exceptions in either `Player.log`.
- Andrew checks anything that needs real hardware or eyes (the DualSense,
  new UI).

Report what was and wasn't checked, then **ask Andrew to publish**.

## 6. Publish (only on Andrew's go-ahead)

```sh
gh release edit alpha-X.Y.Z --draft=false
curl -sIL <asset download URL> | grep -i -E '^HTTP|content-length'   # 200, right size
```

Then give Andrew the release link, update the alpha-release memory, and
tell the other machine's session it can push freely again. Older clients
see "alpha X.Y.Z is out" on their next launch.

## Don'ts

- Don't publish, delete a release or move a tag without Andrew saying so.
- Don't delete old drafts (the 0.1.0 draft stays unless Andrew says).
- Don't build from a dirty tree or from a commit other than the tagged one.
