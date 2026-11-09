Needs: core
Area: unity/Assets/Prototype/Chemistry/ (StandKeeper, strike history, World.Golf, the replay and the checksum parts)
and their tests; no view code
Branch: late-join-core

# A late joiner's replay equals the host's past (u2; lockstep, D4)

Four open issues share one cause: a late joiner replays a compacted history, so its past can differ from the host's
even though no desync is flagged now.

- #173: StandKeeper keeps only a player's newest stand since the last strike (outside step triggers); golf reads
  stands (World.Golf.cs:168) and isn't in the keep rules, so a joiner arriving mid-turn could replay a golf turn
  differently. Not seen failing.
- #164: 5 of 32 checksum records differed, ticks 270-390, in the casts part only.
- #55: one or two checksums disagree right after a Fireball, then agree.
- #83: while a step trigger is on, the strike history keeps every stand, growing by one per player step.
The Mac found on #171 (e39d9a2f) that `NetChem.ReplayedUpTo` marks where a joiner's replay ends, and roomopen now
compares from there.

1. **A core test harness:** a host world runs N ticks of scripted commands (casts, stands, a Fireball, a golf turn,
   a step trigger), a joiner starts from the host's snapshot plus the kept history, and both are compared per tick and
   per checksum part. Make #173, #164 and #55 fail in it first, or show they can't.
2. **Fix by keeping what the replay needs, and only that:** the keep rules know every reader of stands (golf, step
   triggers, anything else that reads them; find them all), so the replay matches the host for every tick after the
   snapshot, and the history stays bounded (#83: compact stands that no rule can read any more).
3. **Every golden byte-identical.** If one must move, stop and report why.
Layer 1 of `docs/TESTING_LAYERS.md`. The PC compiles it and runs `online`, `roomopen`, `roomruin`; it lands then.
