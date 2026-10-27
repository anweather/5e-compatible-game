# Decisions

What this project has *decided*, as distinct from what is *true*
(`RESEARCH.md`). Each decision is an ADR (architecture decision record) in
[`adr/`](adr/), one file per decision, saying why and what would reopen
it. Other docs refer to them by number ("D4"); the numbers don't change.

| # | Decision | Status |
|---|---|---|
| D1 | [Engine: Unity](adr/0001-engine-unity.md) | Accepted (2026-10-13) |
| D2 | [Targets: PC first; mobile and web secondary](adr/0002-targets-pc-first-mobile-and-web-secondary.md) | Accepted (2026-10-13) |
| D3 | [Rules live in an engine-free C# core](adr/0003-rules-live-in-an-engine-free-csharp-core.md) | Accepted (2026-10-13, amended 2026-10-25) |
| D4 | [Physics is authoritative only where nobody has to agree](adr/0004-physics-is-authoritative-only-where-nobody-has-to-agree.md) | Accepted (2026-10-13) |
| D5 | [No grid tactics; turn-based only in physical form](adr/0005-no-grid-tactics-turn-based-only-in-physical-form.md) | Accepted (2026-10-13, amended same day) |
| D6 | [The sandbox is the shared lab](adr/0006-the-sandbox-is-the-shared-lab.md) | Accepted (2026-10-16, amended 2026-10-25) |
| D7 | [Netcode: NGO, client-hosted, lockstep chemistry](adr/0007-netcode-ngo-client-hosted-lockstep-chemistry.md) | Accepted (2026-10-25) |
| D8 | [Voice: Vivox, with spell effects applied by each listener](adr/0008-voice-vivox-with-spell-effects-applied-by-each-listener.md) | Proposed (2026-10-24) |
| D9 | [ECS as a pattern in the core, not Unity Entities](adr/0009-ecs-as-a-pattern-in-the-core-not-unity-entities.md) | Accepted (2026-10-25) |
| D10 | [Time and weather are a function of the tick](adr/0010-time-and-weather-are-a-function-of-the-tick.md) | Accepted (2026-10-25) |
| D11 | [World layout from themes, in the core](adr/0011-world-layout-from-themes-in-the-core.md) | Accepted (2026-10-27) |

How to add one: see [`adr/README.md`](adr/README.md).

## Still open

- Paradigm: view, what spells are for, and progression structure
  (`PROTOTYPES.md`), to be decided in an ADR after the prototypes.
- Whether, and how deeply, to integrate with web-boardgame-platform
  (`PLATFORM_FIT.md`).
- Locked vs. flexible abilities (class-locked, learnable, or a hybrid).
  The data model supports all three (`TRAVERSAL.md`).
- Critter acquisition: befriend or bond, not capture-device, pending
  a patent check (`TRAVERSAL.md`).
- Name. `5e-compatible-game` is the repo name, not a title.
