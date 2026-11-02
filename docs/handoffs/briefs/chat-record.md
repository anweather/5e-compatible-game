Brief from the PM: the core half of `docs/design/chat-telemetry.md` (#59). Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid` in your worktree. Setup: in ~/5e-compatible-game, `git fetch`, then `git worktree add ../chat-record origin/dev -b chat-record`.

1. **The record, engine-free, beside `ClaudeRun` in `Chemistry/Mcp`:** something fed each stream-json line with a clock value, which builds the design's record: times, tokens, cost, turns, per-tool calls with refusals, outcome, each `apply`'s patch. No Unity and no file writing that Unity can't call; keep `ClaudeRun.Read`'s behaviour. The PC wires it in. Use real stream-json from `claude -p ... --output-format stream-json --verbose </dev/null` as test fixtures, with anything private removed. Check what the `result` line really carries: don't trust the field names in the design.
2. **`tools/chat-report.mjs`**, as in the design, tested on your fixtures.
3. **The bench:** a headless `chatbench` runner using the path `HiddenThingTests`' fixture run used, printing the design's numbers. Keep it to one run of the five prompts for your proof, Haiku or Sonnet if the tool allows, as it's real spend.
4. State exactly what the PC must add: where in-the-world is stamped, the MCP server log, the play log marks, the Afterwards and Game cost fields.

Don't touch `mods-add-things`, which isn't on dev. Gates in CLAUDE.md, `git diff origin/dev...HEAD --stat` before pushing. Deliver: commit and push, not merged. Final message, 12 lines or fewer: the commit, what works, what the PC must do, what's left out, gates, time.
