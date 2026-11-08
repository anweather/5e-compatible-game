Needs: unity
Area: start-up and chat-apply paths (whatever the profile points at), the play log; rules/ only if a rule's cost is
the cause
Branch: hitch-hunt

# The hitches in Andrew's 2026-11-08 play log (the lead, overnight; given to the PC)

From `docs/playtests/2026-11-08-human-results.md` L3 and his play log
(`%USERPROFILE%\AppData\LocalLow\anweather\Spell Chemistry Valley\play-log-2026-11-07-233828.csv`, 3024 rows, hitch
rows marked): a 5.7 s freeze at start-up (23:38:29), 2.9 s at 23:38:59 and 2.2 s at 23:40:01 (not matched to anything
yet), about 2 s at 23:43:43, 00:05:43 and 00:25:51, and 100-300 ms spikes on each chat apply (23:48:16, 23:49:39,
00:02:34, a burst 00:24:36-00:25:18). Two players, 4K, RTX 4080 SUPER, otherwise 60-90 fps.

1. Match each hitch to what happened at that time (Player.log lines, chat records' applies, the play log's own
   columns). Say what each one is.
2. Reproduce the worst two in a development player or the Editor with the profiler (deep profile only where needed),
   and find the cause.
3. Fix what has a clear fix (warm-up, spreading work over frames, caching), with a check that measures it before and
   after (a probe or a perf test with a budget). Leave the rest written up with numbers.

Read the "Read play logs first" and "Locked PC" memories before you start. Before landing: `dotnet test core`,
EditMode N/N in full, your new check, `smoke` and `online`. No clip needed; a before/after table in the report. Push
the branch only, remove this claim on the branch in the last commit. The lead lands it.
