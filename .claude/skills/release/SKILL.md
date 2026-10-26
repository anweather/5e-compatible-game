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

## 3. Build, pack and draft (one script, either machine)

`tools/ship-update.sh X.Y.Z` (no `--test`), run in the background: it builds
the macOS app and the Windows folder at the release commit, packs both with
Velopack (installers, portable zips, full packages, a delta from the last
published release) and uploads them to the **draft** `alpha-X.Y.Z`. Both
platforms build on the Mac, so the PC can keep working. Details:
`docs/RELEASING.md`, "Updates in place", and the `ship-update` skill.

Then the notes: `git log --no-merges --pretty='- %s' alpha-<previous>..HEAD`,
rewritten as a short player-facing list (what's new, what's fixed, known
issues, controls changes; plain words, no commit-speak), plus how to
install: Windows `SpellChemistryValley-win-Setup.exe`; macOS the `.pkg` or
the portable zip into Applications, "Open Anyway" in Privacy & Security.
`gh release edit alpha-X.Y.Z --notes-file <scratchpad>/notes.md`.

Both builds log `NET CONFIG: <hash>` at startup; the two must match or joins
are refused.

## 4. Test the draft (stop for Andrew)

- On the PC: download `Setup.exe` **from the draft** and install. Windows
  warnings for the unsigned exe are expected (see RELEASING.md). Setup
  finishes without a 30 s wait or an "Install Partially Succeeded" dialog,
  and the Esc menu shows `alpha X.Y.Z (<sha>)`.
- On the Mac: the portable zip into `~/Applications` (not Documents).
- Mac app hosts or joins the PC over Relay: `players=2`, `desync=False`,
  no exceptions in either `Player.log`.
- Andrew checks anything that needs real hardware or eyes (the DualSense,
  new UI).

Report what was and wasn't checked, then **ask Andrew to publish**.

## 5. Publish (only on Andrew's go-ahead)

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
