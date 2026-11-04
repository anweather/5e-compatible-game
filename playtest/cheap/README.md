# The cheap playtester

A cheap model plays the core through text and looks for what's wrong. It's a measurement so far, not a schedule.

```sh
dotnet build core/Playtest.Text -c Release
playtest/cheap/run.sh haiku <folder> 20 20261104 10      # model, folder, minutes, seed, budget in USD
dotnet core/Playtest.Text/bin/Release/net8.0/Playtest.Text.dll replay <folder>/commands.jsonl --seed 20261104
```

**How it sees and acts.** `core/Playtest.Text` is an MCP server over the core on the stand-in valley: `map`, `cell`, `census`,
`rules`, `go`, `cast`, `wait`, `spawn`, `weather`, `fresh`, `note`. The model has those tools and nothing else: no shell, no repo
settings. It uses this machine's sign-in and never signs in.

Why not the Editor or a player: text was enough for what this can find, a session costs no screenshots, and each command's world
hash is logged, so `replay` re-runs a session exactly (a 1,191-command session is one file). On a Mac a hidden Editor can stall in
CoreAudio for minutes and the Editor runs one job at a time. No 480 px shots were needed.

**What it can't see:** anything of the view. The stand-in valley has the land, water and buildings, but no trees, no yard and no
things; there's no walking physics, sound or drawing, and no body to move. Of the human playtest list in `docs/PLAYTESTS.md` most
issues are of that kind, so this complements the scripted bot and doesn't replace it.

**A model stops when it feels done,** so `run.sh` starts a new conversation with the time left and the titles noted, on a fresh
world, until the time is up. A run's cost is the sum of its `result-N.json`.

## The first measurement (2026-11-04, seed 20261104, 20 minutes each)

| | Haiku (claude-haiku-5-5) | Sonnet (claude-sonnet-5-5) |
|---|---|---|
| Commands, conversations | 1,191, 1 | 896, 8 |
| Tokens in (fresh, cache written, cache read) | 652, 244,871, 41,290,652 | 606, 223,932, 17,703,810 |
| Tokens out | 119,765 | 100,999 |
| API-equivalent dollars | $2.20 | $3.68 |
| Minutes | 20.2 | 19.8 |
| Notes | 4 | 9 |
| Confirmed, new | 1 (a design question) | 1 |
| Unconfirmed | 1 | 0 |
| False alarms | 2 | 8 |
| Duplicates of open issues | 0 | 0 |
| Dollars per confirmed new finding | $2.20 | $3.68 |

Dollars are the CLI's `total_cost_usd`. One finding each is too few to rank the models. 9 of the 10 false alarms were the tool's
fault, and those are fixed since, but neither session ran on the fixed tool. Nothing in either session was severe: no tool
reported an exception, and the worst tick was 52 ms. Each note is judged in `playtest/findings/cheap-2026-11-04/README.md`.
