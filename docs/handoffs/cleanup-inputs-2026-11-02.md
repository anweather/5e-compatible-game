# Code base cleanup: inputs (2026-11-02)

Notes from the Mac's reports, as it sent them, for the cleanup review (STATE, "Next"). None are fixed
and none are re-checked here. The review also takes the 109 strict warnings, the 33 to 40 S3776,
`ClaudeRun.Pointed`.

## esc-menu-cuts

- `PauseMenu.Submenu.Developer` holds only GameChat's two chat switches: a thin page.
- `NetSession.Host()/Join()` are public but only probes call them; the "same network" status text is reachable only there.
- `NetSession`'s `field` style has a single user.
- ReleaseReadme says "Esc -> Play together" (now the Multiplayer sub menu) and "Esc menu under Credits" (now under Game).
- `WizardOutfit.NextHeadwear` has only a probe caller.

## voice-log-words (#53)

- The VoiceTraceTests call-site check is a deny-list of variable names (`text`, `message`, `said`, `guess`...), so a new name slips past.
- The `chat` probe's `a_revert_adds_a_later_line` showed the same record id across runs, so it seems to read a stale line.
- `docs/FAST_LOOP.md` doesn't say `voice` and `chat` can't pass on a Mac.
- `ProbeDictation` puts heard words in its check details and compiles into players. No release path found; not fully traced.

## play-log-switch

- With `-no-play-log` the menu still says On though nothing writes; two flips start a log anyway, and flipping stores the choice.
- Turning the log off just after F8 could make `Shoot`'s readback call `AddPicture` on a closed file, which would throw. A few frames' window; quitting has the same risk.
- With the log off, F8, the playtest bot's marks and chat `Note`s silently do nothing.
- The `menu` probe's flip back on writes briefly in the Editor's log folder, and opening a log trims that folder.
- If Play mode stops mid-flip, the probe's `finally` may not run, so the pref could stay changed.

## player-mods-folder (#63)

Written before DesktopUnity finished the branch (709551c), so these may have moved.

- `RulesWatcher` only watches the player's folder if it exists at world start, so a hand edit is missed until the next start in a session where the first save created it.
- `RuleSwitcher` reaches `ClaudeTerminal.Folder` in Net/, and migration lives in `RuleSwitcher`.
- On a read-only install, a mod the player deleted stays listed through the install's leftover copy.
- During `chatprompts`, the catalog keeps the real folder while the tools use the test folder (harmless in a probe).
- On an id clash, a peer's Mods page shows its own file's title and idea, though the rules in force come from the room's JSON.
- `docs/LIVE_LAB.md`'s "against its own copy of the file" looks stale.
- `RulesTools.cs` descriptions and the refusal still say rules/mods (chat lane).
- The `.part` crash safety has no test.

## #65 bisect

- `Agree` (`ProbeRoom.cs:330`) has no tick wait; `room-agree-wait` is addressing it.
- The room probes depend on the client's tick lag.
- The rule-load slowdown (65 against 38 ms) was left in by choice.

## room-agree-wait

- `roomthing`'s `things_out` took up to 6.3 s against the 10 s bound, the tightest margin; its `InStep` is probably redundant now.
- `no_desync`'s detail always reads "the client saw none", even when it fails.
- `dotnet test` lists `ClearThenFog` and `EachStageUnderClearStormAndFog` as skipped while the summary says 0.
- `unity recompile` is needed on an unfocused Editor, or an edited script doesn't compile; 3 runs ended up on base code that way.
