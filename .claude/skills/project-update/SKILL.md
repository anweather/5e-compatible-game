---
name: project-update
description: Write the human a short, one-screen project update (wins, fails, blocked, what each worker is doing, compute use, and the decisions they need to make) instead of long messages, and measure how idle each agent and the bottleneck machine actually are. It comes with a small bash sampler that logs, once a minute, which Claude sessions are active and whether Unity (or another heavy tool) is running, plus a report script and a "nothing has run for 15 minutes" alert. Use this whenever the user asks for a status, update, standup, summary or "what's everyone doing", says they don't want to read everything, asks about idle time, utilisation or throughput of agents, or when you coordinate several agents and should report back. Also use it when the user worries that an expensive resource (a Unity Editor, a GPU, a build box) sits unused.
---

# Project update

The human shouldn't have to read every agent's report. Give them one screen,
in the same order every time, so they can find what changed and what they
must do in seconds.

## The format

```
PROJECT UPDATE · <date time> · since <last update's time>

WINS     · <merged or verified, one line each, with its commit>
FAILS    · <what broke, or a test that failed, and what happens next>
BLOCKED  · <what's stuck, and on whom>

WORKERS  (active % since the last update)
  <Lane>  <task>  <branch>  <busy / waiting on X / idle>  <n>%

COMPUTE  (the bottleneck machine)
  Unity running <n>% · working (build, probe, player) <n>% · two Editors <n>%
  No-Unity gaps: <time for n min: why> · Slot: <who has it, who's next>

NEEDS YOU
  1. <decision, with the options and my pick first>
```

- Lead with outcomes, not activity. "Quit crash fixed: quits take 0.3 s, no
  dump (4a61ee3)" beats "Gameplay worked on QuickExit".
- Put numbers wherever there are numbers: frame times, sizes, test counts.
- Write "none" for an empty section, so the human sees you checked.
- **NEEDS YOU** lists the decisions only. Then ask them with the
  AskUserQuestion tool (see the **judgment-call-log** skill), so the human
  can answer with a click instead of typing.
- Send it when asked, after a milestone (a merge, a finished task), or when
  something needs the human. Not after every agent message: between updates,
  one or two sentences are enough.

## Measuring idle time

Time you can't see, you can't fix. The bundled scripts measure the workers
and the bottleneck on one machine:

- `scripts/sample.sh` appends one CSV row a minute. Each row records, for each
  local worker, whether its Claude transcript was written in the last 3
  minutes, or a Unity batch job is running on its checkout. It also counts
  Unity Editors, players (your game's executable) and batch jobs, and records
  free memory and CPU.
- `scripts/report.sh <hours>` summarises it: active % per worker; Unity
  running %; Unity "working" % (builds, probes, players, not just an Editor
  left open); two Editors at once; minimum free memory; and gaps of 5 minutes
  or more with no Unity at all.
- `scripts/report.sh --idle` prints the minutes since any Unity last ran.

Set-up:

1. Copy `scripts/workers.conf.example` to `workers.conf` next to the
   scripts. Give one line per local worker: its name, its transcript file,
   and its Unity project folder. Transcripts live in
   `~/.claude/projects/<project-folder-slug>/<session-id>.jsonl`. To find
   which file is which, look at the newest files and grep their tails for
   the branch or worktree each session uses (`"gitBranch"`, `"cwd"`).
2. Set `GAME_EXE` to your built game's process name, so test players count.
3. Start the sampler in the background (`run_in_background: true`). It runs
   until the session ends, so restart it in a new session.
4. Arm the alert in the background. It exits, and so notifies you, once
   nothing has run for 15 minutes:

   ```bash
   until [ "$(scripts/report.sh --idle)" -ge 15 ]; do sleep 60; done; echo "No Unity for 15 min"
   ```

   When it fires, find out why, then re-arm it. Usual causes: an agent's
   script waiting for an Editor that never opened; an agent idle with
   nothing queued; the slot handed back with nobody told. The fix is
   usually to give the slot to whoever has Unity work queued, or to move
   Unity work earlier.

Remote workers (on another machine) can't be sampled. Ask them to report
their own `ACTIVE` time (working versus idle, and why) in each report.

## Gotchas

- Run only one sampler. Check with `ps -ef | grep sample.sh` after a restart:
  two samplers interleave rows and skew every number.
- Clearing the log makes `--idle` report 0, not huge, because the script
  treats an empty log as "not idle".
- If PowerShell scripts (`.ps1`) are blocked by policy on Windows, that's why
  this is bash with inline PowerShell commands. Don't bypass the policy.
- "Unity running" counts an Editor that's open but idle. For real use, look
  at "working", and at whether the agent holding the slot is active.
