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

**Tests:** use `ApiStandIn`, with no real key and no real call. Cover good key, bad key (401), no network, remove, and restart keeps the key. Compile, EditMode, the chat probes and `smoke`.

**Report:** `bash tools/workers/bridge.sh send pm "settings-api-key" < report.md`: commit, test counts, what isn't proven. Say whether a release player actually reaches `ApiChat`; if a build flag or setting hides it, fix that and say so. Add a line for the notes in player words.
