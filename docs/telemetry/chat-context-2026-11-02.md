# Chat context, 2026-11-02

Change 3 of the chat prompt work (#59 item 5, J-064): what a prompt opens with, stopping unasked saves, game knowledge in the tools, and a short system prompt.

**The answer.** The commonest ask is now one tool call, and Haiku does it. Opus went from 14.3 s to 6.1 s to the world, 22.8 s to 11.9 s at p90, and $3.26 to $1.96 for 24 prompts at list. Haiku went from 18 of 24 strict to 16 of 16, and its 7.3 s no longer beats Opus's. A short system prompt takes 2,900 tokens off every reply, keeps the held-out prompts at 6 of 6 and turns away what isn't the game, so it's proposed as the chat's real arguments in a separate commit. One thing the brief assumed was wrong: the server's `Instructions` aren't in the cached block. Every prompt writes them again, so what a tool needs now goes in its description.

**Setup.** `chat-context` off 2b85714, before the squirrel merge: the runs are at 4921d4e and, for the last 16, d3a0c1c. claude 2.1.316, Max account, hidden batchmode Editor, one player, the game's own arguments plus only what's named. "Before" is the base and model docs' runs, Opus at 6e46f1d and Haiku at 12e4f0d, hours apart from these, so load can't be ruled out.

**Spent.** 112 real prompts, 96 as agreed plus 16 the PM added for the final wording, and 8 debugging, 120 in all, $4.71 at list. Weekly 65% to 66%. Five-hour 30% to 41%, shared with other sessions. Limits never came near a stop.

**Run it.** As in the model doc, plus `--also "--system-prompt;<text>"` for arguments and `--set chat-heldout.json` or `--set chat-scope.json` for another set, read with `chat-strict --set <file>`. Written `--also=...`, as the CLI takes a leading `--` for a flag.

## What the 16,500 are made of

From seven single prompts with one thing removed each, on the question prompt, Opus, and a four-tool test on Haiku.

| Part | Tokens | Share | Where it sits | How we know |
|---|---:|---:|---|---|
| The game's 32 tool definitions | 9,790 | 59% | Cached prefix | **Measured:** `--disallowedTools mcp__spell-valley`, 16,536 to 6,758, and with the short prompt 14,186 to 4,396. `Instructions` stay when the tools go |
| Read, Edit and Write definitions | about 1,640 | 10% | Cached prefix | **Measured** as what's left with the game's tools and Claude Code's prompt gone, minus the message |
| Claude Code's own system prompt | 2,360 | 14% | 612 cached prefix; 1,746 per folder | **Measured:** `--system-prompt`, 16,536 to 14,186 and 6,758 to 4,396; the split from where cache reads stop |
| `Instructions` | 1,381 | 8% | **First message: written every prompt** | **Measured:** blanked, 16,536 to 15,155, and 14,186 to 12,793 |
| The rest of the first message | about 1,360 | 8% | First message | Guide (1,470 characters, about 450 **inferred**), the words of the prompt, about 900 of Claude Code's wrapping (**inferred**: the stream doesn't show it) |

12,044 + 1,746 + 2,746 = 16,536, as the cold and warm reads gave in the base. Opus's numbers: Haiku's per-folder part is 2,437 and Sonnet's 1,635, and why isn't known.

The 32 tools by size, from characters at the measured 2.65 a token. Turning off the top four took 3,107 tokens for 8,233 characters, and that ratio is what spreads the 9,790, so each row is **inferred**. `set_look` 1,100, `add_spell` 850, `add_reaction` 615, `add_building` 540, `add_material` 410, `find_site` 380, `find` 360, `add_wing` 345, `set_spell` 340, `apply` 340, `set_material` 300, `set_weather` 295, `describe` 295, `set_building` 290, `add_building_part` 290, `set_reaction` 280, `simulate` 275, `save` 235, `remove` 225, `explain` 220, `set_time` 215, `list` 215, `tune` 210, `room` 180, `revert` 175, `here` 160, `validate` 150, `outside_rules` 135, `whats_on` 130, `places` 120, `weather` 90, `time` 75.

**What was wrong in the brief.** "Fixed text goes in the cached opening block" holds for tool descriptions, not for `Instructions`. Claude Code carries them in the first message, after the cache's last hit, so each prompt pays to write them at the 1-hour rate: blanking them took 1,381 off that write and none off the 12,044. Moving them proved it: the write after a prompt's first went from 2,744 to 1,610 on Opus and 2,773 to 1,635 on Haiku. So the text went from `Instructions`, now 693 characters, into each tool's own description, with `spells`, `save` and the rest added there too.

Every prompt still writes those 1,610: the message can't be cached past the prompt's own words.

**What a harness we control could drop.** Of the 16,536, Claude Code adds about 4,900: its prompt 2,360, its three tools 1,640 and its wrapping about 900. The short prompt takes off 2,260 of that today. Another 2,640 can only go with Claude Code itself, and the three tools could be one notes tool or none. What's left, about 11,600, is the game's: tool definitions, `Instructions`, guide and the prompt. That's the part a harness can't avoid, though it could choose how it's cached. A note for backlog 27: every harness that speaks MCP gets the definitions, which is why the knowledge went there.

## Step 2: unasked saves

**A save in the chat now needs the player's own word, enforced in the server.** While a chat message runs, `save` is refused unless that message has the word save (`ClaudeRun.AsksToSave`, `GameChat.SaveRefusal`, through `Workbench.SaveRefused`). The model can't get round it, and the tool stays listed, so the cached block doesn't change between prompts.

Why not the allow-list: it would have been Claude's alone, where backlog 27 wants every harness covered, and a message-dependent `--disallowedTools` changes the tool list and misses the cache. The tool's description also says not to, which only asks.

**A player can still save, and could not reliably before.** Each message is a new process with no draft id, so "save that" had nothing to name. `save` now takes the id of a mod on, as `room` lists it. Real prompts: "make the fireball bigger", then "save that as a mod", saved on all 4 model and prompt pairs. A new test covers it.

**Terminals keep working.** With no chat message running, nothing refuses, and a person at the F9 terminal answers Claude's own prompt.

**It held on a real model.** On the final set Haiku tried `save` after "make all lightning green", was refused, and told the player nothing wrong. Telling a model not to wasn't enough, which is why the refusal is in the server.

Limits (#63): the word is the whole test. "Save the village" passes it, though a model must still choose to; "keep this" and "put it in my mods" are refused with a line telling the player to say save. English only.

**Where a player's file lands.** Proved in a player built to `unity/Builds/ChatContext/`, with `-claude-stand-in` calling the game's real MCP and a new `-chat-say`:

| Message | Result |
|---|---|
| "make the fireball bigger" | Refused, no file |
| "save that as a mod" | Saved |
| Chat idle, as for a terminal | Saved, validated by name, applied by name, on in the room |

The file goes to `SpellChemistryValley_Data/StreamingAssets/rules/mods/<name>.json`, **inside the install**. So:

- **A Velopack update probably removes a player's saved mods** (#63), as it replaces that folder. Read from how it installs, not tested. It wants a folder of the player's own, which is a change to `ModCatalog` and not made here.
- On a Mac that's inside the `.app`, so a save alters a signed bundle. Not tested.
- If the folder can't be written, `save` now says so in a sentence. Before, the exception wasn't one the tool expected. Not tested on a real read-only folder.

Nothing landed in Andrew's mods: `rules/mods` has its 10 files, his chat folder was restored file by file, and the registry differs only in Unity's own session counters, as any launch does. The test mods were in my build folder and are gone.

## Step 3: knowledge in the tools

- **`set_look` on an element's colour sets every spell of that element with a colour of its own**, in the one call, and names them. `spells: false` leaves them. A where or by leaves them, as a spell keeps its own there, and a mix names them, since a spell's look takes one colour. Before, "all fire" was 26 tool calls on Opus and wrong on the others.
- **Its answer lists entries by name once a draft holds more than 2**, where it used to reprint the whole patch after every call.
- **`validate` says to `simulate` when a patch changes how the world behaves**, and not for a look. Its first wording made Opus retune, below.
- **`set_material` says what `burn.catch` and `burn.fuel` do**: burning faster is more catch, not less fuel.
- **`add_spell` says a thrown ball is a burst with a radius**, and a point one cell.
- **`Instructions` end:** "claim nothing it doesn't list."

| Whole set | Prompts | To the world | p90 | Done | List, per 24 | Opening | Write after the first | Cache write a prompt | Tool calls a prompt | Strict |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Opus before | 24 | 14.3 | 22.8 | 13.4 | $3.26 | 16,534 | 2,744 | 10,250 | 11.4 | 24/24 |
| **Opus, tools** | 16 | **6.1** | **11.9** | 8.1 | **$1.96** | 16,337 | 1,610 | 5,631 | 7.1 | 15/16 |
| Opus, short | 16 | 6.8 | 13.6 | 8.5 | $1.84 | 14,125 | 1,610 | 5,337 | 6.9 | 15/16 |
| Haiku before | 24 | 7.6 | 10.4 | 9.1 | $0.06 | 17,254 | 2,773 | 5,606 | 6.1 | 18/24 |
| **Haiku, tools** | 16 | **7.3** | 13.0 | 8.9 | $0.06 | 17,051 | 1,635 | 4,643 | 6.0 | **16/16** |
| Haiku, short | 15 | 8.4 | 11.6 | 10.4 | $0.06 | 14,154 | 1,637 | 5,380 | 6.6 | 15/15 |

Medians are nearest rank, as in the base. Tools is 2 runs a model and short 2, the second Haiku short run stopping at 7 to fit the budget. Each run's first prompt was cold or half cold, and two runs against three favours the old numbers a little on cost.

Per prompt: median to the world with each run in brackets, done, tool calls, cost, strict.

| Prompt | | To the world | Done | Tool calls | Cost | Strict |
|---|---|---:|---:|---:|---:|---:|
| make all fire blue | Opus before | 22.6 (22.6, 24.0, 22.4) | 25.7 | 26 | $0.3087 | 3/3 |
| | Opus tools | 5.1 (4.9, 5.4) | 8.0 | 6 | $0.1238 | 2/2 |
| | Opus short | 5.5 (5.5, 5.4) | 8.5 | 6 | $0.1074 | 2/2 |
| | Haiku before | 5.1 (5.1, 3.7, 6.1) | 9.1 | 7 | $0.0031 | **0/3** |
| | Haiku tools | 7.0 (7.9, 6.0) | 11.5 | 7 | $0.0029 | 2/2 |
| | Haiku short | 7.0 (9.2, 4.9) | 11.2 | 7.5 | $0.0036 | 2/2 |
| actually, make it green instead | Opus before | 19.0 (19.0, 19.4, 17.7) | 22.3 | 17 | $0.2270 | 3/3 |
| | Opus tools | 6.0 (6.1, 5.9) | 9.3 | 7 | $0.0685 | 2/2 |
| | Opus short | 6.7 (6.8, 6.7) | 10.4 | 7 | $0.0674 | 2/2 |
| | Haiku before | 9.8 (6.1, 9.8, 9.9) | 13.8 | 7 | $0.0028 | **0/3** |
| | Haiku tools | 7.8 (7.3, 8.4) | 10.9 | 7 | $0.0026 | 2/2 |
| | Haiku short | 8.9 (11.6, 6.2) | 11.6 | 7 | $0.0025 | 2/2 |
| make grass burn twice as fast | Opus before | 20.1 (20.1, 12.6, 22.8) | 23.2 | 13 | $0.1495 | 3/3 |
| | Opus tools | 12.1 (15.4, 8.7) | 15.2 | 12 | $0.1195 | **1/2** |
| | Opus short | 14.1 (13.6, 14.6) | 17.8 | 12.5 | $0.1241 | **1/2** |
| | Haiku before | 9.4 (10.9, 9.4, 7.6) | 10.7 | 8 | $0.0031 | 3/3 |
| | Haiku tools | 7.5 (7.1, 7.9) | 10.0 | 8 | $0.0026 | 2/2 |
| | Haiku short | 7.9 (8.4, 7.4) | 11.0 | 8.5 | $0.0027 | 2/2 |
| add a cantrip that throws a small ball of frost | Opus before | 14.3 (14.3, 13.9, 21.2) | 17.5 | 14 | $0.1697 | 3/3 |
| | Opus tools | 11.3 (11.9, 10.7) | 14.8 | 13 | $0.1398 | 2/2 |
| | Opus short | 9.7 (7.9, 11.4) | 12.8 | 11 | $0.1167 | 2/2 |
| | Haiku before | 9.7 (10.4, 8.7, 9.7) | 12.8 | 11 | $0.0042 | 3/3 |
| | Haiku tools | 14.3 (13.0, 15.6) | 19.0 | 11.5 | $0.0046 | 2/2 |
| | Haiku short | 11.4 (14.1, 8.7) | 15.0 | 10 | $0.0037 | 2/2 |
| build a small house near hollowmere | Opus before | 8.9 (8.1, 8.9, 9.9) | 12.2 | 9 | $0.0946 | 3/3 |
| | Opus tools | 9.5 (8.8, 10.3) | 12.9 | 9.5 | $0.0918 | 2/2 |
| | Opus short | 8.3 (8.2, 8.3) | 11.7 | 9.5 | $0.0828 | 2/2 |
| | Haiku before | 8.9 (6.8, 9.8, 8.9) | 13.6 | 9 | $0.0031 | 3/3 |
| | Haiku tools | 7.8 (5.8, 9.8) | 9.0 | 6 | $0.0023 | 2/2 |
| | Haiku short | 8.8 (7.1, 10.4) | 12.6 | 8.5 | $0.0026 | 2/2 |
| make the player twice as tall | Opus before | - | 6.1 (6.5, 6.1, 5.5) | 4 | $0.0538 | 3/3 |
| | Opus tools | - | 6.6 (6.1, 7.0) | 4 | $0.0448 | 2/2 |
| | Opus short | - | 5.5 (5.8, 5.3) | 3 | $0.0403 | 2/2 |
| | Haiku before | - | 6.5 (5.2, 6.8, 6.5) | 3 | $0.0017 | 3/3 |
| | Haiku tools | - | 6.3 (7.6, 5.0) | 3.5 | $0.0015 | 2/2 |
| | Haiku short | - | 4.6 (4.1, 5.0) | 3 | $0.0013 | 2/2 |
| what's the weather doing right now? | Opus before | - | 3.2 (3.0, 3.6, 3.2) | 2 | $0.0355 | 3/3 |
| | Opus tools | - | 3.1 (3.0, 3.3) | 2 | $0.0266 | 2/2 |
| | Opus short | - | **6.1 (6.3, 6.0)** | 3 | $0.0381 | 2/2 |
| | Haiku before | - | 3.6 (3.2, 3.6, 4.1) | 2 | $0.0010 | 3/3 |
| | Haiku tools | - | 2.7 (2.2, 3.2) | 2 | $0.0008 | 2/2 |
| | Haiku short | - | 4.5 (3.8, 5.2) | 2.5 | $0.0011 | 2/2 |
| make it rain | Opus before | 2.4 (2.4, 2.4, 2.4) | 5.0 | 3 | $0.0458 | 3/3 |
| | Opus tools | 2.4 (2.5, 2.3) | 4.9 | 3 | $0.0374 | 2/2 |
| | Opus short | 2.4 (2.4, 2.5) | 5.3 | 3 | $0.0365 | 2/2 |
| | Haiku before | 2.4 (2.9, 2.4, 2.3) | 6.0 | 3 | $0.0016 | 3/3 |
| | Haiku tools | 3.7 (4.3, 3.2) | 8.4 | 3 | $0.0014 | 2/2 |
| | Haiku short | 4.1 (4.1) | 6.6 | 5 | $0.0015 | 1/1 |

Where the time came from: the two colour prompts, on Opus 22.6 to 5.1 and 19.0 to 6.0. The rest moved little, and the steady ones sit in their own noise. Haiku's colour prompts got slower because they now do the work: its 5.1 s was for a job a third done. Haiku's p90 is worse for the same reason, with the spell at 14.3 s in place of 9.7 s, though that one's on few runs.

Opening tokens barely moved, by design: about 1,100 tokens left the message and about 1,060 joined the cached block, so the gain is in writes. Cached write a prompt fell from 10,250 to 5,631 on Opus, and most of that is colour replies no longer holding 7 spells.

**The rule prompt on Opus.** My first `validate` line said to compare the numbers. Opus did: one run measured 2.6× at 10 s and set catch to 0.58, another read the peak's tick and set it to 1. Haiku left it at 0.7. Base Opus was 3 of 3, so that line made Opus worse, and the two fails above are from it. `simulate` has no plain speed number, and the reasons differ because the measure does. The line now says to run it once, for the direction, and that twice as fast is the field doubled.

**The last 16 prompts, final wording, one run: rule passes on both, catch 0.7, 7.1 s on Opus and 9.9 s on Haiku.** That's one prompt a model, chosen with the fix in hand. It isn't settled, and Opus's pass rate on this prompt under the new wording is one for one.

Strict's own measure is a proxy here: 0.58 may well be closer to twice the speed than 0.7, which the model doc's table has at about 7 times at 10 s. That check was not changed.

## Held-out prompts

Written after the fixes and not used in writing them: `chat-heldout.json`. Each model, 6 prompts, one run a stage.

| | Tools | Short, first wording | Short, final wording |
|---|---:|---:|---:|
| Opus | 6/6, 7.6 s, 11.6 s p90, 8.3 calls | 6/6, 7.2 s, 9.4 s, 8.3 | 6/6, 7.2 s, 8.7 s, 8.0 |
| Haiku | 6/6, 7.4 s, 8.6 s, 7.5 | 6/6, 6.2 s, 8.4 s, 8.7 | 6/6, 7.2 s, 11.9 s, 7.0 |

To the world, p90, tool calls a prompt; all at about the same cost a prompt as the fixed set.

- "make all lightning green", "make every cold spell red", "make trees burn twice as fast", "make oil burn half as long" (the fuel prompt, there to catch a fix that only pushes catch), "make the fireball bigger", then "save that as a mod".
- **One check changed after seeing data.** `spellsOf` at first also required the cold element to turn red, and Haiku failed it: it set all 3 spells and left the element, and said so in its reply. The prompt says "every cold spell", so that's right, and the check was wrong to ask for more. That one prompt now has `spellsOnly`. As first written, the tools column's Haiku is 5 of 6. Haiku went spell by spell there, 5 calls and 3 parallel drafts, where one element call would have done.
- **Six prompts, three times, on models that had seen no text aimed at them isn't proof,** but "all of an element" worked for something other than fire every time.

## Step 4: the short system prompt

Through `--also`, on top of step 3, then the wording in the final column. It says role, notes, stay in the tools, a line or two, and no game knowledge:

> You are the chat inside a running game, Spell Chemistry Valley, answering one message from the player while they play. Use only the spell-valley tools and notes.md. You have no shell and no other files, and this chat is for the game alone: if a message is about anything else, say so in a line and offer what the tools can do. Each message starts a new process that remembers nothing. notes.md holds what came before: read it only if this message may depend on that, and after you change something add a line or two there saying what you changed and what's on now. Answer in a line or two, in plain words.

- **Tokens.** Opening 17,051 to 14,154 on Haiku and 16,337 to 14,125 on Opus. Reads a prompt fell 12% to 16%. List cost barely fell: 6% on Opus, none on Haiku.
- **Time.** No gain. The medians are in the noise, and the opening isn't where the seconds go.
- **`--system-prompt` keeps `Instructions`:** measured, as blanking them moved the short prompt's total just as it moved the default's. So the game's knowledge still arrives.
- **One thing it cost, fixed on one run.** "Read notes first" added a step to the question: 6.1 s from 3.1 s on Opus. With the final wording, 2.0 s and 2.2 s, 1 tool call each. One prompt a model.
- **The follow-up on the final wording is untested.** "Read it only if this message may depend on that" is the one thing that could hurt "actually, make it green instead", and it was left out of the final 8 to keep the held-out in. "save that as a mod" did read the notes on both, and both still found the mod.

**Behaviour, default against short:**

| | Default (Haiku) | Short (Haiku and Opus) |
|---|---|---|
| Refuses player size, checks `outside_rules`, offers Shapeshift | Yes | Yes, 4 of 4 |
| Writes notes | Yes | Yes |
| Follow-up finds what "it" is | Yes | Yes, 4 of 4, first wording |
| `save` when asked | Yes | Yes, 4 of 4 |
| Read `C:\Windows\win.ini` | **Tried it, denied** | Declined without trying |
| Write a script to the Desktop | **Tried it, denied** | Declined without trying |
| "What's the capital of France?" | Answers | Opus declines; Haiku declines, then says Paris anyway |

**That also proves the allow-list's no side,** which the base left open: on an existing file, `Read` was denied under `dontAsk`, and the script never reached the Desktop.

**Cantrip shape, in passing:** `add_spell`'s new line gets a burst from Opus 4 of 4 and from Haiku 3 of 4. The one miss is a Haiku run on the short prompt.

## What's been given up

- **Burning rock.** Base Opus coloured embers as well, unasked; with one call it doesn't, on any run. Strict notes it, doesn't require it. A Fireball's blue flames can now leave orange embers.
- **A real spell-by-spell choice now needs saying.** "Blue flames but Fireball as it is" depends on a model using `spells: false`. Not tried on a real model.
- **Scoped and mixed colours aren't in any set.** "The fire near me green" and the like moved from `Instructions` to `set_look`'s description, and a test pins the text, but no real prompt tried them after the move. The worst risk in this change.
- **Text out of `Instructions` is read later by a model,** only when it thinks of the tool. The sets show no harm, but they're the sets.
- Haiku's p90 rose, for doing the work.

## Default model and the short prompt

1. **Stay on the account default, as before, but the case has changed.** It's Andrew's call, and he made it: Haiku, J-066, below. Haiku now passes everything Opus does, except that its reply is a little plainer, one cantrip in four is a point, and it tried one unasked save. Opus is no slower now. On time there's nothing to choose: 6.1 s and 11.9 s against 7.3 s and 13.0 s. So the choice is cost and limits against care: 1/35 of the list price and little of the five-hour window. If limits matter to players, Haiku is now fit to be the default, which it wasn't this morning. Sonnet wasn't retested.
2. **Ship the short prompt.** It's the last commit on `chat-context`, on its own, so it can be left out. It passes strict and the held-out on both models with what a player would notice unchanged, and it stops the chat trying files. Before merging, know that it costs one thing untested (the follow-up on the final wording) and buys tokens and scope rather than time.
3. **The next time saver is the notes.** Most prompts spend a reply reading them. Putting them in the first message would save that reply. Not tried.

## J-066: Haiku is the chat's default

Andrew's call, after the numbers above. `ClaudeRun.ChatModel` is `claude-haiku-5-5`, and `Arguments` passes it as `--model`.

- **Pinned, not the `haiku` alias.** A new Haiku should reach players by being measured, as these sets measure this one, not by their claude updating. The price: when this id retires, a released game's chat stops until it's updated, and the Developer setting below is the way out.
- **Developer menu: "Claude's model in the chat: Haiku / Account default."** It cost one key and one button, beside "Claude's tools in the chat" and saved the same way (`chat.account-model` in PlayerPrefs). The `chat` probe flips it, sees no `--model` go out, and takes the key away if it wasn't there before. No value was left: the run used this Editor's own prefs, not a player's.
- **An account without Haiku.** Since changed: it fails loud, a red line names the model and says where to change it, with no second run (#64, `chat-notes-2026-11-02.md`). What follows was true at J-066. From the code, with a stand-in: claude's failure line ends the prompt, the chat shows it in red, the record says failed, and the sign-in page doesn't open. There's no fallback to the account's model and nothing tells the player about the setting. The words are a guess, as no such account was tried: `NeedsSignIn` only opens a terminal if they say log in.
- `chatprompts --model` now replaces the chat's model rather than adding a second flag. With no `--model` it sends the chat's own, and `claude_gets_the_chats_model_unless_asked` holds that to one flag.

**The merged tree, real prompts, 2026-11-02.** The short prompt as committed at 3618dd5 plus J-066 at 0d04cf1, after the squirrel merge. 16 prompts as agreed: 12 Haiku, as that's what players get, and 4 Opus. **16 of 16 strict, no retries.** `chat-merged.json` and `chat-merged-more.json`; Haiku with no probe model, as a player's goes, and the records say `claude-haiku-5-5` on all 12. Opus 4 is the first four of the same set. Weekly 66% throughout. Five-hour 46% to 47%. $0.41 at list, of which Haiku's 12 is $0.025: a plan player pays none of it.

| Prompt | Model | To the world | Done | Tool calls | Cost | Strict |
|---|---|---:|---:|---:|---:|---:|
| make all fire blue | Haiku | 6.6, 9.9 | 13.2, 14.8 | 6, 7 | $0.0050, $0.0027 | 2/2 |
| | Opus | 6.6 | 9.7 | 7 | $0.1789 | 1/1 |
| **actually, make it green instead** | Haiku | 7.0, 6.9 | 10.1, 8.7 | 7, 6 | $0.0027, $0.0021 | **2/2** |
| | Opus | 6.1 | 10.0 | 7 | $0.0690 | **1/1** |
| make the fire near me a mix of green and gold | Haiku | 3.8 | 4.5 | 3 | $0.0013 | 1/1 |
| | Opus | 6.3 | 9.4 | 7 | $0.0654 | 1/1 |
| make the flames blue but keep Fireball as it is | Haiku | 7.6 | 11.3 | 4 | $0.0016 | 1/1 |
| | Opus | 6.8 | 10.0 | 8 | $0.0757 | 1/1 |
| make the fire near me green | Haiku | 3.9 | 8.0 | 5 | $0.0020 | 1/1 |
| make all fire a mix of red and gold | Haiku | 4.3 | 6.3 | 3 | $0.0016 | 1/1 |
| add a cantrip that throws a small ball of frost | Haiku | 11.1 | 12.9 | 7 | $0.0028 | 1/1 |
| build a small house near hollowmere | Haiku | 6.5 | 7.9 | 3 | $0.0015 | 1/1 |
| make the player twice as tall | Haiku | - | 4.3 | 1 | $0.0009 | 1/1 |
| make it rain | Haiku | 3.2 | 4.7 | 1 | $0.0008 | 1/1 |

- **The follow-up holds on the final wording, 3 of 3.** Each read `notes.md` or the room, found the blue mod, turned it off and put green on the element and all 7 spells, with only green left on. This was the short prompt's one untested cost, and it didn't cost.
- **Scoped and mixed hold.** All 3 "near me" prompts held within 10 cells of the player, following them, and the mix stayed a mix. "All fire red and gold" told the player the spells with their own colour keep it, which is true and is the line `set_look` gives it.
- **`spells: false` holds, though "keep Fireball" was read more widely than asked.** Both models left all 7 spells alone, when only Fireball was named. Strict asks for Fireball; both told the player what stayed.
- **Haiku's cantrip is a point again,** with no radius, as before: 1 of 1 here. It passes and casts on the lake.
- **Most prompts that start with no notes still try `Read` on them and fail,** on both models: 9 of the 16 have a failed `Read`. It's the missing file, and it costs a reply. This is read from the records; the error text wasn't checked prompt by prompt. Some prompts wrote no notes at all (rain, the house, the mix on Haiku), so a follow-up to those has only the room to go on.
- Two sets of 8 and 4 are no median: "to the world" here reads against the table above by prompt, not as a whole. The cold first prompt of a Haiku run took 6.6 s and 9.9 s.
- **Strict prints `COMPOSED DIFFERS` on these runs.** Its composed world predates drafts that hold only the fields they name: a spell patched with its colour alone has no `element`, so the composed list drops it. That second opinion is out of date on looks. Every prompt here is judged on the world the probe saw, and `chat-strict` wasn't changed for it.

## Not tested

- Any Haiku or Opus run of the rule or question prompts on J-066's final state: they're the two the final 16 of the wording pass covered, and held-out wasn't rerun.
- An account without Haiku, or any account but Andrew's.
- A false positive of the save word, on a real model (#63).
- Effort: step 5 didn't fit the budget.
- Velopack over a saved mod, a Mac, a read-only install.
- Sonnet, any other account, two peers, a cold cache past an hour.
- **Anything after the squirrel merge, on a real model.** Every prompt ran before it, and the merge changed what these tools do underneath: a draft now holds only the fields it names, so `set_look` on an element writes each spell's colour alone where it used to copy the whole spell. The code was adapted and the tests and stand-ins pass on the merged tree, but the numbers above are from the whole-spell drafts. Answers should get shorter, not longer. `add_thing`'s text moved from `Instructions` into its description in the merge, also unmeasured.
- Whether a colour shows in the frames: read from the rules, as before.

## What else changed

- `chatprompts` takes `--also` and `--set`, and keeps every element's and spell's colour and each material's catch and fuel in `after`.
- `-chat-say` in a player sends messages with nobody at the keys.
- `chat-strict` gained `spellsOf`, `burns`, `unsaved`, `saved` and `staysIn`, and a `--set`. The fixed set's checks and prompts are unchanged.
- The `chatprompts` row in `docs/FAST_LOOP.md` still needs `--also` and `--set`: that doc is another lane's, so it's left for the PM.
