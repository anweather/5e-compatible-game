Brief from the PM (Linux worker, core only; no Unity): prove two peers with different Hold points stay in lockstep. Branch `hold-points-lockstep` off dev. Frugal: no sub-agents, a report of a few lines.

Phase 5 (`hold-progress`, landed) gives each player saved points that unlock hands, gestures and upgrades (`rules/hold.json`, `Hold.cs`, `World.Hold.cs`; design `docs/design/the-hold.md`). A peer's unlocks are its own save, but the world is shared lockstep (D4): every peer must run the same world from the same commands.

1. Read how a peer's unlocks reach the world. If a peer's local save can change shared state without going through a command, that's the bug: report it with the line, and fix it so unlocks enter as commands (D9).
2. Add a core test in the style of the existing lockstep and late-join tests: two peers, one with 0 points and one with everything unlocked, play a Hold run for several waves with upgrades picked on each side; checksums agree every tick. Then a third peer joins late and agrees too.
3. If the curve numbers in the Mac's report (`docs/handoffs/pm-2026-11-05.md`, "Phase 5 as reported") come from a bot run you can repeat headless, run the two-peer one once and report its minutes. Don't tune anything.

Run `tools/workers/gates.sh` before reporting. Push the branch only. Report: the commit, whether the unlocks were safe as found, the test names, the gates' result.
