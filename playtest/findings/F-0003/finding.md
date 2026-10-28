# F-0003: A flat white square by the waterfall

- **Category:** Visual
- **Severity:** Medium (0.1.6 paper cut)
- **Build:** alpha 0.1.5 (4a9af50), the shipped release; runs rc015-1 to rc015-6, seeds 101-106 (2026-10-27)
- **Reproduced:** Seen once. Not yet checked on dev: dev's seed-103 run didn't stand there.

**Steps:** Go to the waterfall's pool and look back past the wizard.

**What happens:** A flat white square, about a cell wide and 2 m tall, stands behind the wizard in the pool by the falls (RC run 6, frame 2702). Perhaps a spray or mist quad drawn without its texture. The two flat yellow squares on the water are the grease the bot had just cast, drawn as cell squares, and are expected.

**Replay:** `playtest run --replay playtest/runs/rc015-6/run.json --until-frame 2710 --player .claude/worktrees/rc-0.1.5/player`

![before](before.jpg)
