# Judgment calls

Every decision the coordinator asked <Human> for. **Before you ask <Human>
anything, look here first.** If a past decision covers the question, follow
it, and add 1 to its *Revisited* count. Ask again only if something has
changed, and say what.

(Not to be confused with `docs/DECISIONS.md`, if the repo has one: the
architecture decisions.)

Agents send judgment calls to the coordinator, not to <Human>. The
coordinator checks this log, asks <Human> with the question tool if needed,
and records the answer here.

Each entry has:
- **Asked:** the question, when, and who raised it.
- **Also comes up when:** other sessions or situations likely to hit the
  same question.
- **Answer:** what <Human> decided.
- **Why:** <Human>'s reason, or the coordinator's accepted recommendation,
  marked "(recommendation)".
- **Revisited:** how many times it came up again, with dates.

## Open

(none)

## Decided

### J-001: <question>

- **Asked:** <date>, raised by <who>.
- **Also comes up when:** <...>
- **Answer (<date>):** <...>
- **Why:** <...>
- **Revisited:** 0.
