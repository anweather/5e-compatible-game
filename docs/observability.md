# Reports and observability

> **Placeholder.** Nothing here is built yet: the game has no report key, no bundle and no report tool. This page
> is the kit's contract (the `project-shape:observability` skill), kept so the first build of the report key has
> its target. **Undecided:** the key (the kit's Unity pattern uses F10), the snapshot's fields, where bundles are
> written, the budgets.

Every bug its evidence. The game's report key will make a bundle without leaving the game: a picture, the version
and commit, the time (and the world's tick and hour), a snapshot each module provides, the last seconds of a
structured ring log, slow frames with their cause labelled, and the session: solo, host or client, and how many
peers. `docs/report.schema.json` is the bundle's `report.json` schema (`kit-report/1`, copied from the kit).

## In Unity

How it is built in Unity is the kit's `unity-game-dev:unity-report` skill: one key that never stops the game,
providers each module registers (so the report names no module's class), a ring-buffered `log.jsonl`, cost scopes
so a spike names what took the time, the commit stamped into the build, and bundles in a `reports/` folder of
their own, beside and never inside the player's saves (`docs/PLAYER_SAVE.md`).

Snapshot fields worth having here, for a start: the map and chunk, the cell and height step under the wizard, its
reach, what it holds and casts, the tick, the weather and hour, the rule switches on, the peer's role and the net
config hash, the frame time now and its slowest in the window.

## Filing a bundle

The kit's report tool (in the observability skill) files a bundle as a bug issue through the bug form
(`.github/ISSUE_TEMPLATE/bug.yml`), with the player's own data stripped. It isn't copied here yet: it comes with
the key.

## The rule for agents

Debug from the bundle and the logs first (today: `Player.log`, the chat records, the playtest findings). If the
evidence isn't there, the first fix is the missing telemetry; a guessed cause is not a finding.
