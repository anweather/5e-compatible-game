# D15. Workers land and pull their own work (2026-11-06)

**Status:** Accepted (Andrew, 2026-11-06, J-077)

- **Context:** coordination was half of all agent spend (330M of 626M ite, `docs/telemetry/2026-11-06.md`).
  It follows the clock, not the work: one long PM session re-reads its whole context at every wake, and
  most wakes are routine (send a brief, run `land.sh`, publish the board).
- **Decision:**
  1. A worker lands its own branch with `tools/workers/land.sh auto` once its gates pass. A worker with Unity
     passes `--compiled` for its own compile. Whatever `land.sh` refuses goes to the `pm` mailbox.
  2. Briefs wait in `docs/handoffs/briefs/queue/`. A worker that finishes claims the next one it can do
     (moves it to `queue/taken/` and pushes that on dev; a push that loses the race picks again).
  3. Feature intake is the `backlog-groom` agent (Sonnet): Andrew's ideas become an issue and a brief in the queue.
  4. The PM is a short session started for planning, decisions or a release, from `docs/STATE.md`, then
     ended. The board is published only with a decision for Andrew; long reads go to cheaper sub-agents.
- **Why:** routine steps stop paying for a day of context. The target is PM spend from about 50% of the total to 10-15%.
- **Reopen if:** two workers' landings break dev or each other's work, or the queue sits with work nobody claims.
