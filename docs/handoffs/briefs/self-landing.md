# Brief: workers land their own work and pull briefs from a queue (D15)

Read `docs/adr/0015-workers-land-and-pull-their-own-work.md` and `docs/handoffs/briefs/queue/README.md` first.
Branch `self-landing` off dev. You own `tools/workers/**` only (plus the two worker docs below).

1. **`land.sh` from a worker.** Today the PM runs it on the PC (`tools/workers/land.sh`, worktree-based). Make it
   work from a worker's own checkout on Linux and Windows VMs and the PC/Mac hosts: a mode (or flag) that lands
   the current branch; refusals print the reason and exit non-zero. Keep every existing refusal. A Unity C# change
   from a machine without Unity must still stop (it goes to the mailbox for the PC to compile).
2. **On refusal, tell the PM.** `bridge.sh` (or the outbox the runner already writes) gets the refusal text, so a
   refusal is the only kind of landing the PM hears about.
3. **The queue in `runner.mjs`.** When `~/.worker/inbox` is empty after a run, claim the next brief from
   `docs/handoffs/briefs/queue/` whose `Needs:` this machine meets (core: any; unity: a machine with an Editor;
   mac: the Mac), skip one whose `Area:` overlaps a brief in `queue/taken/`, claim by `git mv` + commit + push on
   dev, retry on a lost push race, then run it as a fresh session. Nothing queued: stay idle (no compute).
   Make the queue opt-in per machine (an env var or a file under `~/.worker`), on by default for u2 only.
4. **Worker instructions.** `tools/workers/VM_BRIEF.md` and the worker part of `tools/workers/README.md`: after the
   gates pass, land with the new mode; delete your `queue/taken/` file in the landing; report a refusal, not a success.
5. **Tests.** Extend `gates.test.mjs` or add a node test for the queue picker (needs, overlapping areas, empty
   queue). Run `tools/workers/gates.sh`.

Push `self-landing` only, and report to the mailbox. Don't land it yourself: this one the PM lands, since it changes landing.
