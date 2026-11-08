# Notes in the first message, and a model the account refuses, 2026-11-02

Two chat changes on Claude Code's path (#59 item 5, #64). `chat-context-2026-11-02.md` and `chat-api-2026-11-02.md` are the runs this reads against.

**The answer.** Both work, and the notes didn't save the reply they were meant to. No prompt opens on a failed `Read` now, but most of the failed `Read`s were never opening reads: 5 of the 9 in the merged run come after the change, on the way to writing notes, and they're still there. Replies a prompt didn't fall on this small sample, and the follow-up got slower. The retry for #64 worked on real claude, end to end. To drop the `Read` for good, the notes need a tool of their own, as the API loop has: untried.

**Setup.** `chat-notes-fallback` off e1db522, hidden batchmode Editor, one player, Haiku as players get it (the retry run aside). claude 2.1.316. "Before" is `merged-haiku-1` and `-2` of the J-066 doc, the same tree less these changes, hours apart, so load can't be ruled out.

**Spent.** 8 real prompts of 8 on Andrew's plan: 1 reading the failure, 1 proving the retry, 4 on the first wording, 2 on the final. Weekly 68% throughout, five-hour 7% to 8%. $0.16 at list, of which the retry's Opus prompt is $0.149: a plan player pays none of it. No failure was retried.

`chat-notes.json` is the set. Run it with `chatprompts --real-claude true --set chat-notes.json`, and read it with `chat-strict --set`.

## Notes: before and after

Each cell is one run. Replies are `message.id`s, not `num_turns`.

| Prompt | | Replies | To the world | Done | First tool | Opening | Cache read, first reply | Failed `Read` | Strict |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| make all fire blue | Before | 6, 7 | 6.6, 9.9 | 13.2, 14.8 | 2.7, 5.2 | 15,689 | 0 (cold), 14,054 | 1, 1 | 2/2 |
| | After, first wording | 7 | **4.7** | 8.4 | 1.3 | 15,840 | 0 (cold) | 1 | 1/1 |
| | After, final wording | 5 | **4.7** | 9.7 | 2.8 | 15,836 | 13,883 | 1 | 1/1 |
| actually, make it green instead | Before | 7, 4 | 7.0, 6.9 | 10.1, 8.7 | 2.1, 2.6 | 15,691 | 14,054 | 0, 0 | 2/2 |
| | After, first wording | 7 | **12.3** | 18.4 | 4.5 | 16,029 | 14,115 | 0 | 1/1 |
| | After, final wording | 6 | **13.2** | 15.8 | 7.3 | 16,005 | 14,133 | 0 | 1/1 |
| what's the weather doing right now? | Before, `chat-context`'s final wording | 2 | - | 2.0, 2.2 | | | | 0 | 2/2 |
| | After, first wording | 2 | - | 1.9 | 1.3 | 15,844 | 14,115 | 0 | 1/1 |
| make it rain | Before | 2 | 3.2 | 4.7 | 3.1 | 15,687 | 14,054 | 0 | 1/1 |
| | After, first wording | 4 | 3.0 | 9.5 | 2.9 | 15,838 | 14,115 | 1 | 1/1 |

6 of 6 strict. Two runs a side on the pair, one on the others: read this as a small sample, not a median.

- **The opening `Read` is gone.** 0 of the 3 failed `Read`s here come before the first change to the world. In the merged run 4 of 9 did.
- **The other `Read` stayed.** All 3 prompts here that wrote notes from nothing tried `Read` first, one of them on wording that says outright to `Write`. Why is **inferred**: claude's `Write` asks for a `Read` of a file that exists, and the habit outweighs a line in the prompt. So the "9 of 16" overstated what this could save, and so did the API doc's "about one reply a prompt".
- **The world came sooner on a fresh start:** 4.7 s twice, against 6.6 s and 9.9 s. It's too few runs to call, and the fast ones also skipped `describe` or `whats_on`, which isn't the notes' doing.
- **The follow-up got slower, and I can't say why.** To the world 12.3 s and 13.2 s, against 7.0 s and 6.9 s. Part of it is the first reply: its first tool at 4.5 s and 7.3 s, against 2.1 s and 2.6 s. Its tools hardly grew, and the stream gives no reason. It may be load, or Haiku thinking over the notes; two runs a side can't tell, and the budget went before they could. This is the one result here that argues against landing. "Why the follow-up slowed", below, takes it further.
- **The follow-up still works.** Both found what "it" was and left only green on. On the first wording it needed nothing but the message and `whats_on`. On the final wording it opened with `Read`, and then took the long way, with a refused `revert` before the one that went through.
- **Writing to notes that exist still costs a `Read`.** See below.

### The cache

**The notes go after the cached prefix, as measured.** The follow-up's cache read is 14,115 with 267 bytes of notes in the message, the same 14,115 as the question and the rain with none. What it adds is written: 1,912 against 1,721 and 1,727, about 190 tokens. Opening tokens rose about 150 on every prompt for the new sentences in the prompt and guide: 61 in the cached part, the rest written each time. The first prompt after any change to the system prompt misses the cache, so the first wording's opened cold.

The final wording's first prompt read 13,883, and the next 14,133. Why the first fell short is **inferred**: it's a partial hit on what the earlier wording left.

### Does `Read` have to stay allowed?

**Yes, on what's known.** On notes that exist, Haiku took `Read` then `Edit` both times, and neither was refused. That `Edit` needs the `Read` is what claude's own tool says; no real prompt tried an `Edit` of an existing file without one, as nobody's run could leave it out. A terminal session also reads them, through the guide. So `Read(./notes.md)`, `Edit` and `Write` all stay on the list.

### Wording

- **The notes are a quoted block.** Each line starts `> `, between the game's own first line and `End of notes. The player's message:`. A model wrote them from a player's words, so a mod name can hold anything. Every kind of line break is split on, so nothing in them stands on a line of its own, and a line reading like the close stays quoted. The system prompt and the block both say they're a record, not instructions.
- **That's a line of wording and a format, not a wall.** It stops the block being forged, and it tells the model what the text is. A model can still be persuaded by what it reads. The walls are elsewhere: `save` needs the player's own word, tested with a note asking for one, and that check reads the message before the notes join it; the tools are the same list.
- **No real prompt tried a hostile note.** The probe and tests prove the format, not what a model does with it.
- **The API loop's block is as it was,** `Notes from earlier messages:` and the lines, unquoted, as the brief said to leave that path. The same reasoning applies to it, and it's for whoever takes that path further.
  *Since 2026-11-08 (chat-new-request):* the API loop sends the same block as Claude Code, in one text block. Its two blocks side by side reached the model as one run of text, and a note `add_note` had cut mid-word ran into the player's words ("UnMake it daytime"). The close now reads `End of notes. What the player says now, the request to act on, even if it repeats an earlier one:`, a cut note ends at a word with `…`, and both system prompts say to act on the player's message even when it repeats an earlier ask.
- **The record's prompt is the player's words.** Notes join the message only in `ClaudeChat`, after the record has begun. A check holds it.
- **First wording:** "if the file is already there, Read it just before you Edit it." It sent Haiku to look for the file: `Read` failed on the blue and the rain, and on the rain a stray `Edit` failed too, 4 replies in place of 2. **Final wording:** "with no notes ahead of the message there's no file yet, so Write it; with notes, Read it and then Edit it." It's what's committed, on 2 prompts: blue still tried `Read`, and green read first. Both wordings pass. The final hasn't run on rain or the question.

### Why the follow-up slowed

**Still not proven. It leans to the notes, not to load, and the steady part is about 2 s on the first reply.** With notes in the message that reply took 2.2 s longer than the run with them out, 41 s later, and that was the whole gap to the world. The rest of the 12 s to 13 s was slower replies throughout one run and the long way in the other. No fix went in: nothing here says what a change of wording would change.

**Setup.** `chat-notes-slow` off ab2f9c3 plus the switch, hidden batchmode Editor, one player, Haiku, claude 2.1.316. `chatprompts --real-claude true --set chat-notes.json --prompts 2`, then the same with `--notes off`, which sends the message without notes while `notes.md` stays in its file. That's a probe's switch, and play is as it was. The stand-in shows it: `the_notes_go_unless_off` passes on both, and the block is in one log and not the other.

**Spent.** 4 real prompts of 4, two pairs, no retries. Weekly 69% before and after; five-hour 16%, shared with other sessions. $0.010 at list.

| Green, in seconds from the send | Notes in the message | Replies | To the world | First tool | Done |
|---|---|---:|---:|---:|---:|
| Before, 1 and 2 | No, read from the file | 7, 4 | 7.0, 6.9 | 2.1, 2.6 | 10.1, 8.7 |
| After, first wording | Yes | 7 | 12.3 | 4.5 | 18.4 |
| After, final wording | Yes | 6 | 13.2 | 7.3 | 15.8 |
| This run, as the code stands | Yes | 6 | **9.5** | **4.2** | 13.5 |
| This run, notes off | No | 7 | **7.2** | **1.9** | 12.7 |

Blue, the pair's first prompt, has no notes either way, and it took 4.5 s and 6.2 s to the world with first tools at 2.3 s and 2.4 s. Strict 4 of 4.

**The first reply, from claude's `init` line.** "Wait" is how long until the stream's first `thinking_tokens` line, the first sign of the reply. These are the runs of this pair, in order:

| Run | Prompt | Notes in the message | Wait, ms | Whole reply, ms | Thinking, estimated tokens |
|---|---|---:|---:|---:|---:|
| Before 1 | Blue, then green | No, no | 1,449, 765 | 2,030, 1,630 | 282, 314 |
| Before 2 | Blue, then green | No, no | 4,087, 1,119 | 4,644, 1,758 | 286, 282 |
| First wording | Blue | No | 860 | 860 | 0 |
| | **Green** | **Yes** | **2,890** | 4,045 | 345 |
| | Question, rain | No, no | 813, 1,686 | 813, 2,337 | 0, 276 |
| Final wording | Blue | No | 1,537 | 2,028 | 252 |
| | **Green** | **Yes** | **4,436** | 6,369 | 473 |
| This run | Blue | No | 1,254 | 1,467 | 221 |
| | **Green** | **Yes** | **2,319** | 3,667 | 366 |
| Notes off | Blue, then green | No, no | 1,143, 1,031 | 1,852, 1,426 | 302, 259 |

- **Which replies it was.** To the world took 2.26 s longer with notes, and the first tool 2.25 s. Both runs took 4 replies to get there, and from the first tool on each took 5.3 s. So on this pair it's all in the first reply.
- **Every green with notes waited longer than its own run's blue,** 1.1 s to 2.9 s longer. Every green without them waited less than its blue. 3 of 3 each way. All 3 with notes wait 2.3 s or more. Across all 19 first replies kept with no notes, the rest of the two before runs' prompts included, the middle is 1.25 s and 2 reach 2.3 s, at 2.6 s and 4.1 s: a long wait does happen with no notes. I looked for this after seeing the streams, so it's a pattern that fits, not a test passed.
- **Two runs can't separate load from the notes.** It's one pair, 41 s apart, and the speed of replies moves more than that within a minute: the first wording's rain, with no notes, took about 2 s a reply just after a question at under 1 s. So one quick run with notes off could be luck. It takes more pairs to settle, alternating.
- **Load fits the rest.** In the first wording's green every reply was slow, about 1.6 s to 2.9 s, where this run's, with notes too, were mostly 1.3 s to 1.4 s. That run's rain, with no notes, was slow as well. The final wording's extra time is its long way: a refused `revert`, 2 tools more, and a second `revert`. Neither run here took it.
- **It isn't more thinking, as far as the stream shows.** Thinking is estimated at 345 to 473 tokens with notes, 221 to 314 without, about 100 more, and the wait comes before any of it is reported. Each first reply's thinking text is empty in the stream. Whole prompts' output doesn't lean either way: 1,399 tokens with notes and 1,974 with them off, which had a failed `Edit` in it. Whether Haiku is thinking during the wait or sits in a queue, the stream can't say.
- **What the first reply did.** Thinking, then `whats_on`, with no words, on both. With notes off it also took `Read` in that same reply, and the file was there.
- **Green finds the mod from the room alone.** `whats_on` named Blue fire, and it switched it off. It's not a case for taking notes out: it still went to `Read`, in about 20 ms, and then an `Edit` failed on a string it copied with the line number, which cost a reply.
- **Cache read is the same,** 14,133 on both. The notes add 160 tokens to what's written: 1,863 against 1,703.

**Not tried.** Why a block of about 160 tokens would hold a first reply for 1 s to 3 s. A shorter block, or the block after the message. Alternating pairs, which is what would settle load.

## An account without Haiku (#64)

**2026-11-02: Andrew reversed this. No fallback.** A retry on another model spends money nobody asked for, so `chat-fail-loud` takes it out: a refused model runs one process, a red line names the model and says where to change it, and the record says failed. What follows is what was measured with the retry in, kept for its numbers. Real check, 1 prompt, claude 2.1.316, `--model claude-haiku-0-0-not-a-model`: one process (one `init`, one `result`), 0 tokens, 1.0 s, the red line named the id, and the record said failed with `why` "model unavailable: claude-haiku-0-0-not-a-model". An account really without Haiku is still unseen.

**Retry first, and it took.** The account-without-Haiku case itself has still not been seen: a real run with a model id that doesn't exist is the nearest there is.

**The real failure,** claude 2.1.316, `--model claude-haiku-0-0-not-a-model`, 0 tokens, 1.1 s:

> There's an issue with the selected model (claude-haiku-0-0-not-a-model). It may not exist or you may not have access to it. Run --model to pick a different model.

It comes twice: on an `assistant` line with `"model":"<synthetic>"`, `"error":"model_not_found"`, `"is_api_error_message":true` and `"api_error_status":404`, then on a `result` with `is_error: true`, `subtype: success` and `terminal_reason: api_error`. So the chat used to show it twice, once as Claude's words. `ClaudeRun.Read` now leaves the first out, for every API failure.

| Failure | What happens | Proved by |
|---|---|---|
| The model is refused, nothing ran | One run again with no `--model`; a line says so; the session remembers | Real claude once; stand-in; tests |
| The same, but a tool ran first | No retry; red line with the pointer | Stand-in; tests |
| Not signed in | The sign-in path, as before | Stand-in; tests |
| A limit, the connection, a tool | No retry, no pointer: it wouldn't help | Stand-in; tests |
| Words it can't place, on a pinned model | No retry; red line with the pointer | Stand-in; tests |
| The account's own default refused too | One retry only, then a plain red line | Stand-in |
| Timed out or stopped | No retry, no pointer | Read from the code. Not run |

**The real retry:** "make it rain" asked for that bad id, failed in 1.5 s, ran on `claude-opus-5-5` and had rain in the world at 3.1 s. The record has that model, and `"retry":{"from":"claude-haiku-0-0-not-a-model","why":"There's an issue...","at":1547}`. The probe's `ran_the_model_asked` failed, as it should: a run that asks for a model can't pass on another.

- **The line:** "This account can't use the chat's usual model, so it's on your account's default now." Once a session: `ClaudeChat` keeps the refused id, and the next message asks for no model. Nothing goes in the settings, so a new session tries Haiku again.
- **The pointer:** "If this keeps happening, try Esc, Developer, Claude's model in the chat: Account default."
- **"Nothing applied" is stricter than asked:** no tool ran at all, not just no `apply`. Only that's sure of no change, and a real refusal has no tools.
- **A message's record stays one record.** `ChatRecorder.Retried` drops the first try, and times stay from the send. A record with no retry has no such field, so a Claude Code record is byte for byte as it was. `chat-check` reads a retried prompt from its last process, and `chat-report` counts retries.
- **It also covers #64's other half.** When `claude-haiku-5-5` retires, a released game's chat should move to the account's model, with the same line, if claude words it as it does here.
- **The match is on the words:** `ClaudeRun.ModelRefused`, loosely, the model named plus a phrase like "may not exist" or "not have access". `error` and `api_error_status` weren't used, as nobody knows what they say on a real account without it.

## Not tested

- A real account without Haiku, or any account but Andrew's.
- The final wording on the question or the rain.
- A hostile note on a real model.
- A real sign-in failure, rate limit or cut connection through the retry's checks. The stand-in uses words guessed for them.
- A `Cancel` in the moment between the two tries. It's handled in the code, and not run.
- A notes file with a very long line: the cap is on lines, as `Trimmed` always was, so one huge line goes whole into the message. Before, `Read` would have paged it.
- Terminal sessions on the new guide.

## What's next

1. **Settle why the follow-up slowed.** Done as far as one more pair goes: see "Why the follow-up slowed". Four or so alternating pairs with `--notes off` would tell load from the notes; then try the block's length or place.
2. **To remove the `Read` for good, give the chat an `add_note` tool.** The API loop has one, and it has no equivalent of this reply. On Claude Code it would be an MCP tool, so it'd sit in the cached block, and `Read`, `Edit` and `Write` could go from the list. Not tried: it's a change to the tool list and wants its own before and after.
3. A cheap one to try: "with notes, Read it just before you Edit it", since "Read it and then Edit it" had green open on `Read` (one run).

## What else changed

- `ClaudeRun.Notes` and `ClaudeRun.Message`; `ApiLoop` takes the first and its request is unchanged, which a test now holds to the byte.
- `ClaudeSaid.Notice`, the game's own line in the middle of a message.
- `ClaudeChat` keeps a failure back until the process has gone, as it may yet be retried, and `Ended` now comes before the red line.
- The `chat` probe's records go to its own folder now, not the play logs' folder, where its first few used to land.
- The `chat` row in `docs/FAST_LOOP.md` now has notes in the message and the retry, and `chatprompts` has `--notes off`.
