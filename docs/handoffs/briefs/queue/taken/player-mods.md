Needs: core
Area: rules/ (the player's stats: speed, size, jump height, jump count) through the rules MCP (D13), the MCP's tools
and acts in core, core tests; no Unity view work beyond reading the new numbers (say what the view must read)
Branch: player-mods

# Player mods by chat, and the requests chat refused (Andrew, 2026-11-08; given to u2)

From `docs/playtests/2026-11-08-human-results.md` (Mods, L2). Six-legged dolphins worked and were funny. Now:

1. **Player mods.** By chat, a player can make themselves move faster, be bigger, jump higher, and change double jump
   into any number of jumps ("quadruple jump"). These are rules (the player's stats as data, per player), changed
   through the MCP like any other rule, deterministic and lockstep-safe (a rule switch, same on every peer). Bounded:
   pick sane limits and say them.
2. **What chat refused or got wrong**, from the chat records (`chat-log-2026-11-08.jsonl` excerpts are in the playtest
   doc): "steer the centaur to the lake" ("can't steer"), "make all rabbits 20 times faster" ("can't"), "make
   constant meteors fall" ("too heavy"), and a "100 m mountain" that came out about 21 m. For each, either make it
   possible through general MCP tools (a creature kind's speed, sending a creature to a place, a repeating shower
   with a cap) or make the reply say why and what it can do instead. Don't add one-offs.
3. Note, don't fix: voice requests that arrived empty ("I don't see a new request", about 30 empty records). Say in
   the report whether the records show the text reached the chat at all.

Before landing: `dotnet build core -c Release -warnaserror`, `dotnet test core` twice in full in the foreground (the
runner closes a session that only waits), `dotnet format`. Push the branch only, remove this claim file on the branch
in the last commit. The PC compiles it and the lead lands it.

After (2026-11-08, try 1): the watcher stopped u2 at 1.22 GB free on the PC; the work in progress is on
`wip/u2-player-mods` (463b38b6). Continue from it, on branch `player-mods`.
