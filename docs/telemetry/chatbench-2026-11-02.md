# chatbench, 2026-11-02: the first run

One run of the five prompts on Haiku (`claude-haiku-5-5`, claude 2.1.312), on commit 2216be7 plus the recorder, on a Linux worker VM
with no game running. It proves the path; it's no baseline. A baseline is three runs before and three after, on the player's own model.
`applied` is when the room's answer came back: chatbench has no view, so it can't see "in the world" (seconds, tokens, dollars at list price).

| Prompt | Outcome | First tool | Applied | Done | Starting up | Model | Tools | Left over | Cache write | Cache read | Output | Cost | Turns | Tool calls |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| make all fire blue | applied | 3.0 | 7.0 | 10.9 | 0.9 | 9.7 | 0.5 | -0.2 | 7,516 | 87,410 | 923 | $0.0028 | 8 | 7 |
| make grass burn twice as fast | applied | 2.5 | 9.6 | 13.8 | 0.7 | 12.6 | 0.7 | -0.2 | 8,146 | 107,458 | 1,319 | $0.0034 | 10 | 9 |
| add a cantrip that throws a small ball of frost | applied | 2.6 | 13.6 | 17.9 | 0.8 | 16.9 | 0.5 | -0.2 | 13,218 | 170,888 | 1,841 | $0.0053 | 13 | 12 |
| build a small house near hollowmere | applied | 3.0 | 10.0 | 13.4 | 0.8 | 12.3 | 0.5 | -0.1 | 8,372 | 108,275 | 1,287 | $0.0034 | 9 | 8 |
| make the player twice as tall | refused | 2.1 | - | 5.5 | 0.7 | 4.7 | 0.0 | -0.0 | 6,294 | 48,088 | 721 | $0.0021 | 4 | 3 |

Read with care, as this is one run:

- **The model is the time.** 86 to 94% of each run, with starting up 0.7 to 0.9 s and tools 0.0 to 0.7 s. The server here is in memory, so
  the game's main-thread wait isn't in the tools' share: that's what the MCP server log is for.
- **The world changes well before Claude finishes.** Apply's answer comes 3.5 to 4.4 s before the end, spent writing `notes.md` and the reply.
- **First word is no progress sign.** In all five Claude's first words came in the last 20 ms, so the progress bar
  has to run on tools.
- **Left over runs slightly negative** (-0.0 to -0.2 s): a tool's line arrives as its block ends, a moment before the reply does, so tool and model time overlap a little.
- **Each first message tries to read a `notes.md` that isn't there**, one refused `Read` a run. It counts in the record as a call, but not as a game tool's refusal.
