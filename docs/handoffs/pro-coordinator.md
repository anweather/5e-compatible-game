# Brief: coordinator on the Pro account (from the Project Manager session)

You coordinate this repo's agents while the main account is low on usage.
Usage is tight here too, so work frugally.

## Get context (read these, in order)
1. `.claude/skills/agent-coordinator/SKILL.md`, especially "Frugal mode" and
   "Accounts, machines and VMs". Also in `.claude/skills/`:
   judgment-call-log, project-update, demo-reel.
2. COORDINATION.md: the rules, lanes, the FOCUS entry, and the log's last
   entries.
3. JUDGMENT_CALLS.md: check it before asking Andrew anything.
4. docs/MATERIALS.md: the materials model; steps 1–4 are done.
5. tools/workers/README.md and REPORT.md (on branch `workers`): the Hyper-V
   worker VMs `w1` and `w2`, reachable by Remote Control under those names.

## The trial: one small feature across the two VMs
Materials step 6, its first combination: **soaked things burn poorly**
(`soak.dampens` in MATERIALS.md). A soaked cell or entity catches more slowly
and burns shorter; drying restores it.
- **w1:** the C# core (core/): the rule, data in materials.json, and core
  tests. Branch `dampens-core`.
- **w2:** the TypeScript sandbox mirror (sandbox/) and a shared golden that
  both engines must match. Branch `dampens-sandbox`, stacked on
  `dampens-core` once w1 pushes it.
- Both are headless: no Unity Editor needed. Tests must pass, and every
  existing golden stays unchanged, except where a soaked cell is burning
  (list those).
- You review and merge into dev, one branch at a time. The merge is
  pre-approved if all core and sandbox tests pass and the build is clean.
- Report to Andrew in under 10 lines: what each VM did, how long it took, and
  the RAM each VM used. Then stop.

## Rules that matter here
- The main account's sessions can't message you, and you can't message them.
  Talk to the PM through files: write to `docs/handoffs/pro-coordinator-report.md`.
- Never type credentials. Never force-push. Lanes as in COORDINATION.md.
