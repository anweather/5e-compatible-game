Brief from the PM: talk to Claude from inside the game, by typing and by voice (J-060). Andrew's top priority, on the PC.

"I wanted it to be as if I am talking to a terminal running Claude Code ... it just spawns a Claude Code process in headless mode with the input and the MCP config ... it doesn't need to be a long persistent Claude session. Maybe it writes out what it did to a markdown file so that the next one reads it ... we don't need a multiturn conversation." "It feels like you're playing a game still and you're not building a game."

Read `docs/playtests/2026-11-01-mcp.md`, J-056 and J-060, then check `claude --help` on this PC for what's real: don't take the flags here on trust.

1. **`game-chat`, branch off dev.** A chat box in the game with a key to open it, working by pad too. Text sent to the game's bot goes to one headless Claude Code process for that message (`claude -p` with stream-json output, `--mcp-config` for the game's MCP, and only the game's tools plus reading and writing the notes file allowed, so no permission prompt can hang it). Run in the folder F9 uses. Streamed progress and the reply show in the chat in plain words, not as tool logs. The game stays playable and never stalls: all of this is off the main thread. One message at a time, queued, with a way to cancel.
2. **Notes file.** After each ask, Claude appends what it did to `notes.md` there, and CLAUDE.md tells the next process to read it first. Keep it short and bounded.
3. **Finding Claude.** Look on PATH, then `~/.local/bin` (Andrew's `claude` is only there, so the F9 terminal failed: fix that too, for that window only). Not installed: a plain line in the chat with the install link. Not signed in: open a terminal for Anthropic's own sign-in. The game never touches a credential.
4. **What's outside the rules:** put the playtest's list in CLAUDE.md, so Claude says so instead of hunting for source.
5. **`game-voice`, a second branch.** Push to talk fills the chat box, and speaking sends it. The game already has a microphone path for voice chat. Prefer on-device. Try the smallest thing that works on Windows first, say what it would take on Linux, Steam Deck and Mac, and keep the recogniser behind a small seam so it can change.

Multiplayer: the asking player's Claude talks only to their own game; the room's mode decides what goes through, as now. Say nothing in the UI that suggests Anthropic built or endorses this. Use "Claude" in plain text only.

Andrew may be on this PC: keep memory light, tell the PM before heavy runs, and if he says he needs it, stop. Keep it small. Gates, a `chat` probe with a stand-in process, a real end-to-end run if you can do it without signing in for him: if a real run needs a sign-in, leave that to him and say so. A clip, and a new test player. Report to the PM; push, not merged.
