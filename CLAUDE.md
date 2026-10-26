# Working in this repo

## Before starting any work

Get up to date with main first. More than one machine works on this repo
(the Windows PC is the main one; a Mac too):

```
git fetch origin
git switch main
git pull --ff-only
```

If the pull won't fast-forward or the tree has local changes, stop and say
so rather than discarding anything. In particular, never revert a scene or
prefab while the Unity Editor has it open (the "modified externally" alert
blocks the Editor); rebuild it with its builder menu instead.

## Committing

Nothing is live yet, so commit and push straight to `main`; no branch or
PR is needed. Pull again just before pushing.

## Where things are

- `docs/ARCHITECTURE.md`: how the code is put together, and where new
  things go.
- `docs/DECISIONS.md`: the decision index; one ADR per decision in
  `docs/adr/`.
- `docs/BACKLOG.md`: features to build next.
- `docs/DEV_SETUP_WINDOWS.md`: setting up the PC, and Mac/Windows
  differences for agents.
