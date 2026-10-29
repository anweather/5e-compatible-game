---
name: judgment-call-log
description: Keep a running log of every judgment call you ask the human to make (JUDGMENT_CALLS.md), check it before asking anything new, and ask new questions with the AskUserQuestion tool. Each entry records the question, who raised it, other situations or sessions likely to hit the same question, the answer, why, and how many times it has come up again. Use this whenever you coordinate other agents or run a long project and need the human to decide something (design choices, risky git operations, security settings, quality-versus-speed trade-offs), whenever the user says "remember what I decided", "don't ask me twice", "decision log" or "judgment calls", or before you escalate any decision to the user.
---

# Judgment-call log

People hate answering the same question twice. Agents tend to re-ask things
that were already decided, in slightly different words, by different
sessions. A small log fixes that, and it also gives you precedent: an
earlier answer usually tells you what the human would say to a new,
similar question.

## The file

Keep `JUDGMENT_CALLS.md` at the repo root, committed, so every session can
read it. Start from `assets/JUDGMENT_CALLS.template.md`. If the repo already
has a `DECISIONS.md` for architecture decisions (ADRs), keep the two apart
and say so at the top of yours. They answer different questions.

Each entry, numbered `J-001`, `J-002` and so on:

- **Asked:** the question, the date, and who raised it (which agent, or why).
- **Also comes up when:** the other sessions, lanes or situations likely to
  meet the same question. This is what makes the log reusable: it's the
  index you search later.
- **Answer:** what the human decided, in a sentence a newcomer understands.
- **Why:** the human's own reason, quoted if they gave one. If they just
  accepted your recommendation, give your reason and mark it
  "(recommendation)". Don't invent a reason for them.
- **Revisited:** how many times the question came up again after the answer,
  with dates. A high count means the answer is unclear or the rule isn't
  reaching the agents.

Keep "Open" (asked, not yet answered) above "Decided", and move entries
across when they're answered. Partial answers are fine: "test A only, no
download" is worth recording before the full decision.

## At every decision point

1. **Search the log first** (grep for the topic, and read "Also comes up
   when"). If an entry covers it, follow it, add 1 to *Revisited*, and
   don't ask. Tell the agent which entry applies.
2. **Ask only when nothing covers it,** or something material has changed;
   if so, say what changed.
3. **Ask with the AskUserQuestion tool,** not in prose. Questions buried in
   a long message get skipped and pile up. Put each J-number in its
   question, give 2–4 options with your recommendation first and marked
   "(Recommended)", and say the trade-off of each option in its
   description. Batch everything pending into one call (up to 4 questions).
4. **Record the answer straight away,** commit, and tell every agent it
   affects, citing the J-number.
5. **Turn standing answers into rules.** If an answer is a rule ("merged
   branches may be deleted without asking", "force-push only your own
   branch, with --force-with-lease"), also add it to COORDINATION.md or
   CLAUDE.md, where agents read it before working.

## What counts as a judgment call

Anything that's the human's to decide, not yours:
- design and feel (a countdown, who has authority online, whether a reset
  keeps mods);
- quality-versus-speed trade-offs (graphics settings);
- risky or irreversible git operations (deleting branches, force-pushing);
- security settings, sandbox or execution policies (never decide these
  yourself, and never route around them);
- downloads and third-party binaries;
- changes to the plan, lanes or priorities.

Routine engineering choices inside an approved plan aren't judgment calls.
Make them, and note them in the work log.

## Example entry

```markdown
### J-016: May agents force-push their own unmerged branches after a rebase?

- **Asked:** 2026-10-27, by the coordinator, after one agent force-pushed
  four rebased branches. The human had asked to be consulted on force-pushes.
- **Also comes up when:** every rebase of a pushed feature branch, by any agent.
- **Answer (2026-10-27):** allowed, with limits: only the agent's own
  unmerged branch, only with `--force-with-lease`. Never `dev`, `main` or
  another agent's branch.
- **Why:** the human picked the recommendation. Rebasing your own pushed
  branch is routine, and the lease stops it overwriting work.
- **Revisited:** 0.
```
