# Brief: u2's reports never reach the PM's mailbox

Branch `u2-outbox` off dev; you own `tools/workers/**`.

Your reports for `hold-points-lockstep` (00:31) and `self-landing` (about 09:00, 2026-11-06) never arrived:
`~/.agent-bridge/bridge.log` on the PC host has only pc-worker messages all day. The host's watcher
(`tools/workers/bridge-watch.sh`, its pid in `~/.agent-bridge/watch/pid`) is alive. Since D15 a refused
landing reaches the PM only this way, so this must work.

1. Find where it breaks: does the runner write to `~/.worker/outbox` on u2, does `bridge-watch.sh` look at
   Linux VMs (SSH, the IP lookup, the user), and does it move what it finds into `pm`? You can't run the host's
   side; read it and test what you can from u2. Say in your report which link fails, with the evidence.
2. Fix it. Add a test if the fault is in logic a test can reach.
3. Run `tools/workers/gates.sh`, then land it yourself with the new `land.sh` (D15). If it refuses, say why.
   Report through the outbox, and say in the report that this report itself is the test.
