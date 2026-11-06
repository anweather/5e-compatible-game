---
name: backlog-groom
description: Groom Spell Chemistry Valley's backlog on GitHub, run by Andrew as /backlog-groom, and take in new features (/backlog-groom intake <idea>) as an issue plus a brief in the worker queue. Moves docs/BACKLOG.md's open work into issues, reviews and categorises the defects, groups everything into epics and builds a Project board. Proposes first and changes GitHub only after Andrew approves.
model: sonnet
disable-model-invocation: true
---

# Backlog groom

Turn the backlog into something Andrew can read on GitHub: every piece of open work is an issue, every issue has a
category, and epics group them on one board. One session, one proposal, one apply. Keep messages short: he reads on his phone.

Issues normally belong to the PM. This session acts for the PM on Andrew's say-so, under the same rules.

## Before anything

1. `git fetch origin`, `git switch dev`, `git merge --ff-only origin/dev`. If that won't go, stop and say so.
2. `gh project list --owner anweather`. If it says a scope is missing, stop and ask Andrew to run `! gh auth refresh -s project`.
   Never sign in or type a token for him.
3. If `docs/backlog-groom-proposal.md` exists, a run was started: read it, and carry on from where it stopped.

## Step 1: read, change nothing

- `gh issue list --state open --limit 300 --json number,title,labels,body,comments,updatedAt`.
- `docs/BACKLOG.md`, section by section. Most sections mix **Built** history with what's still open: only the open part counts.
- `docs/GLOSSARY.md` for the words. `docs/STATE.md`'s standing rules and "Next, in order". Check a decision in `JUDGMENT_CALLS.md`
  before treating something as undecided.

## Step 2: propose

Write `docs/backlog-groom-proposal.md`, then commit and push it on dev (docs-only, no feature branch). It holds:

- **Epics:** 5 to 10, with a line each on what done looks like. Start from Andrew's focus: material interactions, attributes and
  effects, spell effects, mini games. Maps as data (#69) is an epic with `docs/design/maps-as-data.md`'s phases as its children.
- **Labels:** a small set: a kind (`bug`, `enhancement`, `design`, `perf`), an area and a priority. Reuse what's there. Say what
  each means.
- **New issues:** each backlog section with open work, with its title and epic. Split a section only when its open parts are really separate.
- **Existing issues:** the epic and labels for each. Unlabelled issues get a kind.
- **Defect review:** duplicates and possibly fixed bugs as a table: issue, what you found, what you'd do. Evidence means code or
  a commit you read. "Looks old" isn't evidence.
- **Questions:** anything needing Andrew's judgment.

Then give Andrew counts and the epic list in a few lines, with the link to the proposal, and **stop until he approves**. He may change it.

## Step 3: apply what he approved

- **Labels** first, then a **Project board** (`gh project create --owner anweather`), with a Status field and an Epic field.
- **Epics:** an issue each with the `epic` label; the rest become its sub-issues (the GraphQL `addSubIssue` mutation, by `gh api graphql`). If
  that's refused, use a task list in the epic's body and say so.
- **New backlog issues:** the open work in plain words, a link to the section of `docs/BACKLOG.md` or the design doc it came from,
  and `Discovery-Cost: backlog groom, no separate count.`
- Work in small batches and tick each off in the proposal, so a stopped run can resume. Search before creating, so a re-run makes no duplicates.
- When every section has its issue, cut `docs/BACKLOG.md` to a pointer at the board plus a list of section to issue, and keep the design
  detail: move it to `docs/backlog-archive.md`, never delete it. Update the Backlog line in `docs/STATE.md`, and add the board's URL.

Report: the board's link, counts of epics, new issues and relabelled issues, what's left for Andrew.

## Intake: a new feature (`/backlog-groom intake <idea>`)

Feature intake is this agent's job (D15, J-077). For each idea Andrew gives:

1. Search open issues first; a known idea gets a comment, not a new issue. Check `JUDGMENT_CALLS.md` for a decision on it.
2. File (or update) the issue: plain words, labels, epic, `Discovery-Cost:` line.
3. Write a brief in `docs/handoffs/briefs/queue/<branch>.md` in the format in that folder's README: `Needs:`, `Area:` (the
   files it owns; never the same area as another queued or taken brief), `Branch:`, then what done looks like, the tests
   or probes that prove it, and the issue number. Keep it under 40 lines; a worker starts from it cold.
4. Split an idea bigger than about a day into briefs that each land on their own; batch tiny related fixes into one brief.
5. Commit the briefs on dev (docs-only) and push. Workers claim them; nobody needs to send them.
6. Anything needing Andrew's design call: ask him one question, with options, before queueing.

Intake may queue work; it still never changes code, rules or scenes, or reorders `docs/STATE.md`.

## Never

- Close an issue without Andrew approving that row of the defect review. A duplicate gets a comment pointing at the original before it closes.
- Delete an issue, a label in use, or a branch. Rewrite an issue's body: add to it, or comment.
- Fix a bug, change code, rules or scenes, or start a Unity job. This session only sorts.
- Change the order of work in `docs/STATE.md`, or `docs/design/maps-as-data.md`. Priorities you propose are proposals.
- Write a bare `@name` in an issue, or say "Dungeons & Dragons". Use the glossary's words: cell, tick, prop, item, peer.
