# Measuring Claude in the running game

Every prompt a player sends to change the game leaves one record, so we can say how long it took, what it
cost and whether it worked. Asked for by Andrew, 2026-11-02, after a play left nothing to read (#59): the chat
runs with `--no-session-persistence`, `ClaudeRun.Read` drops the stream's usage, and nobody measures prompt to world.

Local only: records stay in the player's data folder and nothing is uploaded. Sharing one is the player's choice.

## The record

One JSON line per prompt in `chat-log-<date>.jsonl`, beside the play logs, kept for the last 30 files.
A run the game didn't start (Claude Code in the terminal) has only the game-side half.

| Group | Fields |
|---|---|
| Who | record id, game version and commit, room mode, players, mods on before, model, claude version |
| Asked | prompt, source (typed or voice), reply |
| Time, ms from send | process started, Claude ready, first word, first tool, first `apply`, **in the world**, done |
| Time, split | starting up, waiting on the model (`duration_api_ms`), in tools, total. They should add up: say what's left over |
| Tokens | input, cache write, cache read, output, cost, per model, turns |
| Tools | each call: name, ms, ok or refused, reason |
| Outcome | applied, refused, failed, timed out, stopped; for a refusal, what it couldn't do |
| What changed | the whole mod patch of each `apply`, so any result can be rebuilt (the castle couldn't be), and its title |
| Afterwards | reverted or still on at the end of the session, and after how long; whether the next prompt fixes this one |
| Game cost | worst frame and hitches from send to in the world, ms to compose rules, ms drawing it |

**In the world** means every peer's rules switched and the view finished drawing, on this peer. It's the number
the player feels. The headline is prompt to world.

Built so far (core): `ChatRecorder` and `ChatLog` in `Chemistry/Mcp/ChatRecord.cs`. One more outcome than the table: **answered**, a question with
nothing to change, so it doesn't land in the refused list. An outcome is refused when it changed nothing and either a game tool refused or
Claude read `outside_rules`. `applied` is added to the times: when the room's answer came back. `why` holds a refusal's or failure's reason.

What the stream really says, from claude 2.1.312 (fixtures in `core/Game.Core.Tests/RulesMcp/chat-fixtures/`):

- **Tokens and cost:** `result` has `usage`, `total_cost_usd`, `num_turns`, `duration_ms`, `duration_api_ms` and `modelUsage` (per model,
  `costUSD`). Assistant lines mustn't be added up: one reply comes on several lines with the same `message.id` and `usage`, taken before its
  output is counted. They're used only for a run cut off before its `result`, marked `cutOff`, and its output is a floor.
- **Failure:** an API failure says `subtype: "success"` with `is_error: true`; `terminal_reason` says why.
- **Refusals:** a tool refused is a `tool_result` with `is_error`. A room's refusal isn't: it's plain text with " refused:" in it.
- **The patch is never in the stream:** `apply`'s input is `d1`. `Workbench.Applied` gives it, and the record joins them by that key.
- **Time:** the record's clock is when a line arrived. Lines carry no time that's safe across peers, so nothing else is read.

## The view's half

`ChatTelemetry` (`Scripts/Net/`) keeps the clock and the current `ChatRecorder`. `ClaudeChat` feeds it each line and how the process ended; `GameChat.Send`
says typed or voice. A room decision of this peer's starts the stamp: **in the world** is the second frame after it with no land recolouring queued, so a
row house has drawn. The record goes to `ChatLog.Append` off the main thread once the process has ended and the world has it, or 5 s after. `Workbench.Applied`
gives each patch. `afterwards` lines come on a revert, at the session's end for what stayed on, and when a later prompt's patch touches the same entries, the
plain rule for `fixedBy`. `McpRpc.Called` and `McpLog` write `mcp-log-<date>.jsonl`, for terminal sessions too. `PlayLog.Note` adds a `note` line for send,
apply and in the world, which doesn't count as a mark. `ChatBar` turns `ChatRecorder.Progress` into the bar; the Developer menu's "Claude's tools in the chat"
brings the tool lines back; the Claude menu lists the last prompts. `ClaudeTerminal.Guide` comes from `ClaudeRun.Guide`, so the bench reads what the game writes.

## The game's side

`RulesMcpServer` logs each tool call: name, ms waiting for the main thread, ms running, result size, ok or
refused. This is the only source for a terminal session, and it shows whether time goes to the game or the model.
The play log's `what` column marks send, apply and in the world, so a hitch can be tied to a prompt.

## Reading it

- `tools/chat-report.mjs <folder>`: prompts, outcome counts, median and p90 prompt to world, each split's share,
  tokens and cost per prompt, by kind (look, rule, building, weather, refused), slowest and dearest ten, and
  what players asked for that Claude refused. That last list feeds the backlog.
- **`chatbench`:** a fixed set of prompts on a fresh world through the real chat path: a colour, a rule, a spell,
  a house, one that must be refused. Run it before and after anything meant to speed the chat up, three times each,
  because a single run varies. Results in `docs/telemetry/`. It spends real tokens, so run it by hand:
  `dotnet run --project core/Chat.Bench -c Release -- --model sonnet --runs 3 --out <folder>`. It uses the chat's arguments, stdin and folder
  against `Rules.Mcp` over stdio on a copy of `rules/` and `world/`, so it has no view and its nearest mark to "in the world" is `applied`.

## In the game

- A progress bar while Claude works, with tool detail behind the Developer menu.
- Tokens and rough cost per prompt in a short list, from the same record. The record is the one source; the menu
  never counts for itself.
