# F-0003: A flat white square by the waterfall

- **Category:** Visual
- **Severity:** Medium (0.1.6 paper cut)
- **Build:** alpha 0.1.5 (4a9af50), the shipped release; runs rc015-1 to rc015-6, seeds 101-106 (2026-10-27)
- **Reproduced:** Seen once. Not yet checked on dev: dev's seed-103 run didn't stand there.

**Steps:** Go to the waterfall's pool and look back past the wizard.

**What happens:** A flat white square, about a cell wide and 2 m tall, stands behind the wizard in the pool by the falls (RC run 6, frame 2702). Perhaps a spray or mist quad drawn without its texture. The two flat yellow squares on the water are the grease the bot had just cast, drawn as cell squares, and are expected.

**Replay:** `playtest run --replay playtest/runs/rc015-6/run.json --until-frame 2710 --player .claude/worktrees/rc-0.1.5/player`

![before](before.jpg)

## Seen again on alpha 0.1.8 (4378764), 2026-10-30

In all 3 runs that went to the waterfall's pool (rc018-811 frames 6457-6820,
rc018-812 frames 4238-4313, rc018-813 frames 2698-2819), a flat, pale grey band
fills the top third to half of the view. This is the same flat white quad,
much bigger than it was on 0.1.5, and it stays for as long as the wizard stands
in the pool, day or night. Pictures: `rc018-811-6578.jpg`, `rc018-812-4238.jpg`,
`rc018-813-2698.jpg`.

**Replay:** `playtest run --replay playtest/runs/rc018-813/run.json --until-frame 2710 --player %LOCALAPPDATA%/SpellChemistryValley/current`
