Brief from the PM: cut 0.1.11, Windows only, on the PC. Andrew has said ship on a green candidate, and the PM publishes. Frugal mode: no sub-agents, no clips. Follow `.claude/skills/release/SKILL.md` steps 1 to 4 and `docs/RELEASING.md`, for the Windows half only, and stop before publishing.

`origin/main` already holds dev for this release. In `host-ux`, with the tree on `main`:
1. CI green on the commit; refresh, build the yard; `tools/probe.sh smoke`, `online`, `menu`, `chat`, `apikey`, `transcribe --only whisper`.
2. Version 0.1.11 per step 2 (`chore: version 0.1.11`), pushed to `main`. Give the full SHA.
3. `tools/ship-update.sh 0.1.11 --windows-only` to the draft, then `gh release edit alpha-0.1.11 --notes-file docs/handoffs/release-0.1.11-notes.md`.
4. Install `Setup.exe` from the draft, launched outside a Claude session (it inherits `CLAUDE_CONFIG_DIR` otherwise). Check it installs without the 30 s wait or a partial-success dialog, the Esc menu shows `alpha 0.1.11 (<sha>)`, and it updates from 0.1.10 as `docs/RELEASING.md` says. A release candidate holds the PC: nothing else alongside it.

Report once with SendMessage to `5e-compatible-game-c5` (the mailbox listener is off): SHA, what passed, what wasn't checked. Stop and report on any failure.
