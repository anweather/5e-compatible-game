# The brief queue (D15)

One brief per file, written by the `backlog-groom` intake or the PM. Each starts with:

```
Needs: core | unity | mac | any      (what the worker must have)
Area: <files or folders it owns>     (one owner per area: don't queue two briefs with the same area)
Branch: <name>
```

A worker claims one by `git mv queue/<f> queue/taken/<f>`, committing and pushing that on dev. If the push
is refused, fetch and pick again. When it lands, it deletes `taken/<f>` in its landing.
