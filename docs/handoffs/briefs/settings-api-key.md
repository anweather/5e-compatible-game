Brief from the PM: a player's own Anthropic API key in the game's Settings, for 0.1.11 (J-071). Frugal mode: no sub-agents, no clips, a report of a few lines.

Work in `host-ux` on a new branch `settings-api-key` off `origin/dev`. Push that branch only, never `dev`. Design: `docs/BACKLOG.md`, "Two modes" and "Model list". Today `ApiWire` reads the key only from an environment variable, and `ApiChat` runs the loop.

**The player's side**
- A masked key field in Settings, with Check and Remove. A set key never shows again in full; show only that one is saved.
- Check calls `GET /v1/models` and says in plain words whether the key works. A good key fills the model list, so the player picks only from models that key can use. Check the key here, not at the first prompt.
- One plain line: the key is yours, it stays on this PC, and what you use is billed to your account.
- With a saved key, chat works in a release player on the API path, with no environment variable. Without one, the chat behaves as it does today. A saved key wins over the environment variable.

**The key's safety (each needs a test or probe check)**
- It goes only to `api.anthropic.com`, in the `x-api-key` header.
- Windows: stored encrypted for the user with DPAPI, in the player's data folder. Not in PlayerPrefs, which is plain text in the registry. Keep storage behind a small seam, so Mac or Linux can add theirs; until then, those players use the environment variable.
- Never in saves, mods, the play log, chat records, telemetry, crash output, error text or anything sent to peers. `ClaudeRun.Without` already scrubs errors: use it on every new path.
- Never handed to a child process: `claude_is_handed_no_key` must still pass.
- Key checks and API calls never block the main thread.

**Added by Andrew: a Claude setup section in Settings**
The player picks how the game reaches Claude:
- **API key:** the above; the game calls the API itself.
- **Claude Code:** the game runs the player's install. The path defaults to `claude`, or they give a full path, plus environment variables for that run (such as `CLAUDE_CONFIG_DIR`), because they may keep a separate Claude Code instance and account.

Add a Check here too: run the chosen install with `--version` and say plainly whether it's found. Claude only for now, but keep it a connection profile (`docs/BACKLOG.md`: picker of profiles), so Codex or others can be added later without rework. Don't build them.

- The path and environment variables are set only from the local Settings screen, never from mods, rules, saves or peers: that path runs a program on the player's PC. Test it.
- Environment values may hold secrets: store them like the key, and keep them out of logs and records. Add only what the player listed; the game's own saved key is still never handed to a child process.

**Added by Andrew: limiting the damage**
- Beside the key field: one line and a link telling the player to make a separate key for this game with a low spend limit in the Anthropic Console, so they can revoke it any time.
- A spend cap per session for API mode, with a sensible default the player can change. At the cap, the chat stops and says why, in plain words.

**Tests:** use `ApiStandIn`, with no real key and no real call. Cover good key, bad key (401), no network, remove, restart keeps the key, the cap stopping a run, and a Claude Code path found and not found. Compile, EditMode, the chat probes and `smoke`.

**Report:** `bash tools/workers/bridge.sh send pm "settings-api-key" < report.md`: commit, test counts, what isn't proven. Say whether a release player actually reaches `ApiChat`; if a build flag or setting hides it, fix that and say so. Add a line for the notes in player words.
