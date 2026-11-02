# Chat model and context comparison, 2026-11-02

Changes 1 and 2 of the chat prompt work (#59 item 5, J-064): which model the chat should ask for, and what Andrew's own skills add to a prompt. Measured only: nothing a player gets has changed.

**The answer.** Keep the account default and pass no `--model`. Haiku 5.5 and Sonnet 5.5 are faster and far cheaper, but neither did "make all fire blue": both left all 7 fire spells orange in all 6 runs of the two colour prompts. Starting claude without Andrew's skills changes nothing, because they were never in the prompt. The next change to try is making the colour prompts work on Haiku, not choosing a model.

**Setup.** `chat-models` off 12e4f0d, same set as the base (`chat-prompts.json`, whose prompts didn't change). claude 2.1.316, Max account, hidden batchmode Editor, one player, the game's own arguments plus only what's named. Opus is the base's three runs (6e46f1d, `chat-base-2026-11-02.md`), with no `--model`. Sonnet is `claude-sonnet-5-5`, Haiku `claude-haiku-5-5`, each run in the game's own context, which step 2 shows is the same prompt as bare. Every record's model field names the model asked for, and `modelUsage` shows that model alone on all 63 prompts: no subagent, no second model.

**Spent.** 63 real prompts, $2.15 at list price: 8 bare, 24 Sonnet, 24 Haiku, and 7 debugging (one bare question, one single prompt per model, and 4 from a Haiku run stopped by the guard, below). Weekly 64% before and after. Five-hour 21% to 26%, shared with other sessions.

**Run it.** `tools/probe.sh chatprompts --real-claude true --model <id> --out <folder>`, then `--replay <folders>` to cast the recorded spells for nothing, then `node tools/chat-strict.mjs <folders>`. `--context bare` and `--only <kind>` are there too. With none of them, claude starts exactly as a player's does: the stand-in run checks that from claude's own arguments.

## Side by side

Whole set, 24 prompts a model. Medians are nearest rank, as in the base. Strict pass is below.

| | To the world, median | p90 | Done, median | List cost, 24 prompts | Replies | Tool calls | Strict pass |
|---|---:|---:|---:|---:|---:|---:|---:|
| Opus 5.5 | 14.3 s | 22.8 s | 13.4 s | $3.26 | 133 | 273 | **24 of 24** |
| Sonnet 5.5 | 10.3 s | 23.2 s | 10.6 s | $0.77 | 102 | 115 | **16 of 24** |
| Haiku 5.5 | 7.6 s | 10.4 s | 9.1 s | $0.06 | 114 | 146 | **18 of 24** |

Per prompt: median to the world with each run in brackets, median done, all tokens, list cost, tool calls, strict pass.

| Prompt | Model | To the world | Done | Tokens | Cost | Tool calls | Strict |
|---|---|---:|---:|---:|---:|---:|---:|
| make all fire blue | Opus | 22.6 (22.6, 24.0, 22.4) | 25.7 | 210,930 | $0.3087 | 26 | 3/3 |
| | Sonnet | 12.9 (12.9, 4.9, 23.2) | 15.6 | 86,160 | $0.0368 | 5 | **0/3** |
| | Haiku | 5.1 (5.1, 3.7, 6.1) | 9.1 | 95,948 | $0.0031 | 7 | **0/3** |
| actually, make it green instead | Opus | 19.0 (19.0, 19.4, 17.7) | 22.3 | 234,791 | $0.2270 | 17 | 3/3 |
| | Sonnet | 11.2 (6.1, 13.5, 11.2) | 14.9 | 86,876 | $0.0334 | 6 | **0/3** |
| | Haiku | 9.8 (6.1, 9.8, 9.9) | 13.8 | 112,175 | $0.0028 | 7 | **0/3** |
| make grass burn twice as fast | Opus | 20.1 (20.1, 12.6, 22.8) | 23.2 | 144,453 | $0.1495 | 13 | 3/3 |
| | Sonnet | 10.3 (6.1, 13.7, 10.3) | 12.4 | 88,014 | $0.0346 | 6 | **1/3** |
| | Haiku | 9.4 (10.9, 9.4, 7.6) | 10.7 | 117,068 | $0.0031 | 8 | 3/3 |
| add a cantrip that throws a small ball of frost | Opus | 14.3 (14.3, 13.9, 21.2) | 17.5 | 164,846 | $0.1697 | 14 | 3/3 |
| | Sonnet | 16.6 (16.6, 31.7, 11.4) | 19.3 | 113,492 | $0.0469 | 7 | 3/3 |
| | Haiku | 9.7 (10.4, 8.7, 9.7) | 12.8 | 150,954 | $0.0042 | 11 | 3/3 |
| build a small house near hollowmere | Opus | 8.9 (8.1, 8.9, 9.9) | 12.2 | 131,199 | $0.0946 | 9 | 3/3 |
| | Sonnet | 6.6 (6.4, 17.9, 6.6) | 9.8 | 107,172 | $0.0376 | 7 | 3/3 |
| | Haiku | 8.9 (6.8, 9.8, 8.9) | 13.6 | 118,076 | $0.0031 | 9 | 3/3 |
| make the player twice as tall | Opus | - | 6.1 (6.5, 6.1, 5.5) | 52,505 | $0.0538 | 4 | 3/3 |
| | Sonnet | - | 7.5 (9.3, 7.5, 6.1) | 33,911 | $0.0199 | 2 | 3/3 |
| | Haiku | - | 6.5 (5.2, 6.8, 6.5) | 54,787 | $0.0017 | 3 | 3/3 |
| what's the weather doing right now? | Opus | - | 3.2 (3.0, 3.6, 3.2) | 33,672 | $0.0355 | 2 | 3/3 |
| | Sonnet | - | 5.4 (12.4, 5.4, 4.4) | 33,476 | $0.0178 | 2 | 3/3 |
| | Haiku | - | 3.6 (3.2, 3.6, 4.1) | 34,883 | $0.0010 | 2 | 3/3 |
| make it rain | Opus | 2.4 (2.4, 2.4, 2.4) | 5.0 | 51,198 | $0.0458 | 3 | 3/3 |
| | Sonnet | 3.1 (3.1, 2.8, 3.2) | 9.3 | 50,946 | $0.0233 | 3 | 3/3 |
| | Haiku | 2.4 (2.9, 2.4, 2.3) | 6.0 | 54,075 | $0.0016 | 3 | 3/3 |

Where the fast time is real, it's only two prompts. On the six prompts both pass, Haiku beats Opus on the rule (9.4 s to 20.1 s) and the spell (9.7 s to 14.3 s). The house and the rain tie, and the refusal and the question are a touch slower. Most of the headline drop, from 14.3 s to 7.6 s, is the colour prompts getting cheaper by skipping the work.

Sonnet is slower and less steady than its median says. Its p90 equals Opus's, and single prompts ran 23.2 s (blue) and 31.7 s (spell) with no more tools than the others. Why isn't known. Its first reply comes at 3.1 s, where Opus and Haiku take 2.1 s.

## What was given up for the speed

**"All fire" stopped at the element, on both cheaper models, every time.** Fire's colour is the flames', and each of the 7 fire spells has its own for its cast (`rules/looks.json`, J-052). Opus reads that, then sets the element, all 7 spells and burning rock: 26 tool calls. Sonnet and Haiku set the element alone: `Read set_look validate apply Write`.

| | What it did | What it told the player |
|---|---|---|
| Opus, all 3 | Element, 7 spells, embers | "All fire is blue now: everything that burns, burning rock and all seven fire spells. Lava is still its usual colour." |
| Sonnet, run 1 | Element only | "it covers every spell and reaction that makes fire, unless a spell has its own colour." |
| Sonnet, run 2 | Element only | "All fire in the world is now blue." |
| Haiku, single prompt | Element only | "fire from every spell and reaction shows in the new colour." |

The third column is the worse half. Every fire spell has a colour of its own, so a Fireball still casts orange while its flames burn blue, and Haiku says otherwise. The follow-up inherits it: green on the element, all 7 spells unchanged, and on Sonnet the blue mod left on under it in all 3 runs. These are read from the rules in force, not filmed.

**Grass: Sonnet's commonest answer puts the fire out.** In 2 of 3 runs it halved `fuel` and left `catch` alone, said "Grass now burns twice as fast", and said it hadn't simulated. The game's own simulator, cells burnt from a Fire Bolt in the `fire-on-grass` meadow:

| Grass | 10 s | 20 s | 40 s |
|---|---:|---:|---:|
| As shipped: catch 0.35, fuel 8 | 209 | 1,411 | 3,445 |
| Opus and Haiku: catch 0.7, fuel 8 | 1,507 | 3,740 | 3,742 |
| Opus (bare run) and Sonnet run 2: catch 0.7, fuel 4 | 1,401 | 3,587 | 3,594 |
| **Sonnet, runs 1 and 3: catch 0.35, fuel 4** | **81** | **186** | **186** |

That fire dies at 186 cells. Sonnet never called `simulate` (0 calls in 24 prompts), and Opus did 13 times. Haiku did on this prompt (4 calls), which is why it got it right 3 times of 3.

**The cantrip is plainer.** All 3 models' spells really cast: on the lake each froze every cell it touched, and each is on the wheel and in the spell list. But Opus's is a thrown ball: a burst of radius 1, 9 cells, 1d6, a blast look. Sonnet and Haiku made a one-cell `point` spell with a bolt look, which is closer to Fire Bolt than a small ball. It passes the check, as "small ball" doesn't fix a shape, but it's less of what was asked.

**Haiku once saved a mod nobody asked for.** On the spell prompt of its first run it called `save`, which wrote `frost-ball-cantrip.json` into `rules/mods`, and it told the player so. 1 of its 4 spell prompts; never on Opus or Sonnet. The probe's guard stopped that run. The tool is allowed, so a player's game would do the same, into wherever its rules folder is: not tested.

**Nothing else was lost.** House, refusal, question and rain pass 9 of 9 on both models, and the house came from `find_site` every time.

## Bare context (change 2)

**Andrew's skills add nothing to a prompt. The worry was wrong, and bare isn't worth doing.** The base doc's "that base isn't a stranger's" was a guess; it's corrected there.

- **Bare can be done without touching the login:** `--setting-sources project,local --disable-slash-commands`. `--bare` itself can't: it never reads the signed-in account. No config folder was moved and no credential copied.
- **The flags really worked.** From the two streams' init lines: skills 68 to 0, slash commands 104 to 0, plugins 4 to 3 (unity gone, the three built-ins stay). Tools 35 and 35, agents 5 and 5.
- **The prompt didn't change.** First reply 16,531 to 16,543 tokens bare, 16,534 in the base. Bare also read the same 12,044 tokens from the cache the base wrote, which it could only do if those bytes were identical.
- **So why?** The game passes `--tools Read,Edit,Write`, which leaves out the Skill tool, so most likely nothing lists the skills. That's an inference: the stream doesn't show the prompt.

| | To the world, median | Cost, 8 prompts | Tokens read | Written | Strict |
|---|---:|---:|---:|---:|---:|
| Base, 3 runs | 14.3 s over all 3 | $1.03, $1.08, $1.15 | 849,230 to 960,346 | 74,808 to 87,013 | 24 of 24 |
| Bare, 1 run | 12.1 s | $1.17 | 1,037,097 | 87,653 | 8 of 8 |

One run, as it showed no difference: its cost sits just over the base's range because Opus chose more steps on the rule and the question (4 tool calls for the weather to the base's 2), not because the context grew. On that basis the three Sonnet and Haiku runs used the game's own context: it's what a player's claude is started with, and the same size. It also means the base's token counts hold for a player with a bare install, so far as this account can show. A different account wasn't tried.

## Limits and what a player pays

**Dollars are list price, and a Max player pays none of them:** it comes off the limits. The fit over each model's records: Opus $8 written, $0.20 read, $20 out a million; Sonnet $4, $0.10, $10; Haiku $0.20, $0.01, $0.50, each a 1-hour write at twice input. Worst miss $0.00002, `costBasis: list`. So Haiku's $0.06 for 24 prompts isn't a slip: it's 1/40 of Opus's price a token, and a bit more tokens.

**The limits do seem to differ by model, roughly.** The weekly stayed at 64% throughout. The five-hour went 21% to 23% over 9 Opus prompts, 23% to 25% over 25 Sonnet and 25% to 26% over 29 Haiku. That's whole percents on a window other sessions share, this one included, so it's a direction and no more: Haiku spends far less of a five-hour window a prompt than Opus.

## The stricter check

`tools/chat-strict.mjs` reads a run's folder and each prompt's new `must` in `chat-prompts.json`. The prompts themselves are unchanged.

| Prompt | What has to be true |
|---|---|
| Blue, then green | The fire element and all 7 fire spells are that hue, in the rules in force, with the hue 0.2 ahead of the others. Embers are noted, not required |
| Twice as fast | `catch` is 2× within 10%, `fuel` stays or halves, nothing in `sim` moved |
| Cantrip | A new spell at level 0, cold, thrown not from self, radius 2 at most, with a look. It selects, sits on the wheel and in the list, logs a cast, and freezes water when cast |
| House | At most 8 cells a side, within 12 of Hollowmere, walls that close, a door, a roof over every join, and a row appeared in the world |
| Refused | Refused, checked `outside_rules`, offered Shapeshift, no world tool, no change, no mods on |
| Question | The reply says what the `weather` tool said, and nothing changed |
| Rain | `set_weather` worked, for rain, and the sky moved |

- **Opus's real pass rate is 24 of 24**, and bare's 8 of 8. The base's "as expected" wasn't hiding a failure.
- **A cast can't be read from a record,** so `chatprompts --replay` does it for nothing, on all 8 new runs and the 3 base runs. The base's other checks are scored on a world composed from `rules/` and the recorded patches; new runs keep the world the probe saw (`after` in `prompts.jsonl`), and the two agreed on every prompt of all 8 new runs.
- **One check changed after seeing the data.** It first required `fuel` to stay at 8, and failed bare's rule prompt. The simulator shows catch 0.7 with fuel 4 does burn twice as fast, so halving is allowed with `catch` doubled. Halving alone still fails. Nothing else moved, and that change gave bare its rule pass and Sonnet its one.
- **Not checked:** that a house looks right, or that a reply's other claims are true.

## Recommendation

1. **Stay on the account default for now; pass no `--model`.** Neither cheaper model does the commonest ask, and both say it worked.
2. **Don't add the bare flags.** They save nothing.
3. **Sonnet has no case.** Haiku is faster, steadier, about a thirteenth of the list cost, and it passes the rule Sonnet fails.
4. **Next change: fix "all fire", then test Haiku again.** The gap is what a tool tells a model, not skill: `set_look` could name the spells that keep their own colour, or one call could set every fire spell. It's the base doc's point 3. If Haiku then passes the two colour prompts, it's the likely choice: 7.6 s median, 10.4 s p90, and little off the five-hour window. It would need `save` looked at too, and the cantrip's shape.
5. **The default may not be Opus for everyone.** The chat passes no model, so a player whose account defaults to Sonnet already gets the results above. How accounts default wasn't measured, so this is a risk to check, and a reason to do 4 whichever model is chosen.

**By prompt, if the chat could choose:** Haiku is fine for the house, rain, question, refusal and the rule; Opus for colours; the spell is a matter of taste. But the chat picks a model before it reads the prompt, so this would need something to sort prompts first, and that isn't worth building before 4.

## What else changed

- `ClaudeChat.Extra` holds arguments for the probe alone; null in play.
- The probe now stops past 60% of the five-hour limit as well as 85% of the weekly, and on a model other than the one asked for.
- A mod saved unasked is moved beside the record, and the run goes on. Any change to an existing mod still stops it, which the old check of names alone wouldn't have seen.
- The Claude menu's token list now says dollars are a rough list price, not money spent.
- `chatprompts` is in `docs/FAST_LOOP.md`.

## Not tested

- Effort. `claude` has `--effort`, and Opus at a lower effort may keep the quality at more of the speed: that's worth a run, one change at a time.
- Any account but Andrew's, a player build, two peers, voice, and the cache gone cold across models. Each model's first prompt was cold; the runs after it were warm.
- Time of day: Opus ran hours before the others, and a model's speed can move with load.
- Colour prompts in the frames. A spell's orange cast beside blue flames is read from the rules, not filmed.
- Rain as it looks, as in the base.
