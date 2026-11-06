Needs: core
Area: tools/workers/bridge-watch.sh, tools/workers/bridge-watch.test.mjs, tools/workers/linux.sh
Branch: bridge-outbox-repeats

# The watcher mails a Linux worker's reports again every minute

After u2-outbox landed (e341d133), the PC's watcher mailed u2's three reports to `pm` at 13:44, 13:45 and 13:46,
and would have kept on: `check_outbox` never removed them from u2's `~/.worker/outbox`. bridge.log showed no
error. On the host, `bash tools/workers/linux.sh outbox-done u2 <ip> <file>.md` by hand removed a report fine,
so `linux.sh` and SSH work; the fault is in `check_outbox` (bridge-watch.sh ~line 229-251): `mailed` empty,
`linux outbox-done` not reached or failing silently, or something in the read loop consuming stdin.

1. Find it, with evidence, and fix it. A failure to remove must be logged (once), not silent.
2. Add a case to `bridge-watch.test.mjs`: a report mailed once is gone from the worker, and is never mailed twice.
3. Gates, then land with `land.sh here`. Your own report is the live test: say so, and the PM checks it arrives once.
The host's watcher must be restarted to pick up the fix: say so in the report (the PM does it).
