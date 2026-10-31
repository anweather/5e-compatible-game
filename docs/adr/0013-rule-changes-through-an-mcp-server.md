# D13. Rule changes go through a checked MCP server (2026-10-31)

**Status:** Accepted (Andrew, 2026-10-31)

- **Context:** Players are meant to customize the game while it runs. Mods (JSON patches over
  `rules/`, `docs/MODDING.md`) already switch on in a room at a tick, but writing them means
  hand-editing or "vibecoding" JSON, which misses things: dangling ids, runaway reactions,
  rules for one spell. Every player can have their own Claude.
- **Decision:** We provide an MCP server whose typed tools (read, draft, validate, simulate,
  save, later apply to a room) are the way rules change, for players and our own agents. It only
  ever produces mod patches. Local first (stdio, .NET, linking the core), hosted later. No asset
  uploads for now. Everything the view draws is procedural, so a rule that scales something up
  shows in the game with no code change or rebuild. Design: `docs/RULES_MCP.md`.
- **Why:** One checked path catches mistakes before players see them; the headless C# core
  makes simulating a change cheap; players' own Claude carries the cost of the conversation.
- **Reopen if:** the checks can't keep up with what players want to change, or the hosted
  server costs more than the lean budget allows.

**Amended 2026-10-31 (Andrew):** no hosted server. The MCP ships with the game and each player's
Claude talks to their own copy; changes cross the network only as mod patches in the existing
rule-switch intents, and every peer validates them. A room chooses a change mode when it's made:
Open (anyone's changes just appear, for fun), Host only, or Locked.
