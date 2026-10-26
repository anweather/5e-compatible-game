# Architecture decision records

One file per decision. The index, with each decision's status, is
[`../DECISIONS.md`](../DECISIONS.md).

## Adding one

1. Take the next number. The file is `NNNN-short-title.md`, and the
   heading is `# DN. Title (date)`; other docs cite it as "DN".
2. Copy the template below. Start it as **Proposed**; make it
   **Accepted** once it's agreed.
3. Add a row to the table in `DECISIONS.md`.

Never rewrite an accepted decision to mean something else. Amend it with
a dated note, or write a new one and mark the old one
**Superseded by DN**.

## Template

```markdown
# DN. Title (YYYY-MM-DD)

**Status:** Proposed

- **Context:** what forced the question, and what was already true.
- **Decision:** what we will do (and won't).
- **Why:** the reasons, with the evidence.
- **Reopen if:** what would make us decide again.
- **Sources:** where the evidence came from (optional).
```
