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
  because a single run varies. Results in `docs/telemetry/`. It spends real tokens, so run it by hand.

## In the game

- A progress bar while Claude works, with tool detail behind the Developer menu.
- Tokens and rough cost per prompt in a short list, from the same record. The record is the one source; the menu
  never counts for itself.
