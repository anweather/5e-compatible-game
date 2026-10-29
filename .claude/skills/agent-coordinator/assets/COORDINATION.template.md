# Coordination

<N> Claude sessions work on this repo at once, on <machines>. This file
says who owns what, so they don't collide. <Human> approved the lanes on
<date>.

The coordinator is the Claude session **"<coordinator session name>"**.
Message it with SendMessage. It doesn't write feature code.

## The coordinator's responsibilities

1. **Lanes and ownership:** each file has one owner. It approves every
   edit outside a lane, and every shared-file change.
2. **Merges:** it sets the merge order, reviews each branch, and has agents
   rebase in turn.
3. **The scarce resource** (<e.g. the PC's Unity Editor>): one heavy job at a
   time. It hands the slot from agent to agent.
4. **Handing out work:** it gives the next task to each agent as it finishes,
   from the backlog's priority order.
5. **This file:** it keeps COORDINATION.md current from its own worktree.
6. **Judgment calls:** it checks `JUDGMENT_CALLS.md` before asking <Human>,
   asks with the question tool, and records the answer.
7. **Reporting:** a one-screen project update for <Human>.

It asks <Human> first before it:
- deletes files or unmerged branches;
- force-pushes;
- resolves a conflict where work would be lost;
- changes the overall plan.

## Rules for every agent

1. Read this file before each new task (`git fetch`, then read it on
   `origin/<base>`).
2. Work only inside your lane. Ask the coordinator before touching anything
   outside it.
3. Work on a short-lived branch off `<base>`, never directly on `<base>` or
   `main`.
4. Commit often, in small commits.
5. Merge only when the coordinator says it's your turn. Rebase on
   `origin/<base>` first, and push straight after.
6. Report to the coordinator when a task is done or blocked, as DONE /
   FAILED / BLOCKED / NEXT / <RESOURCE>: held or not.
7. Never discard, reset or overwrite work you didn't write. If you find
   uncommitted work outside your lane, stop and report it.
8. One heavy <Editor/build> job at a time on <machine>. Ask before you open
   one.

## Agents and lanes

| Agent (session name) | Machine and checkout | Lane |
|---|---|---|
| **<Lane>** (`<session>`) | <machine>, <path or worktree> | <feature area> |

## File ownership

| Owner | Paths |
|---|---|
| <Lane> | `<paths/**>` |

These shared files have **no owner**, so changes go through the
coordinator's queue: `CLAUDE.md`, `README.md`, `.gitignore`, <...>.

## Current tasks

| Agent | Task | Branch | State |
|---|---|---|---|

## Shared-file queue

| # | File | Agent | Why | State |
|---|---|---|---|---|

## Merge order

1. <branch> (<lane>: <what>)

## Open questions

(none)

## Log

- <date>: <what changed in the plan, and why>
