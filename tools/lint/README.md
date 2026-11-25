# The lint

The project's code rules, copied in from andrew-claude-kit's lint pack (`KIT_VERSION.txt` says which commit). The
rules are switched on and set in `lint.config.json`; that file is the project's, and a new copy of the pack never
touches it.

```sh
node <this folder>/lint.mjs              # ends "no errors"; --warnings lists every old break
node <this folder>/lint.mjs --staged     # what the commit hook runs
node <this folder>/install.mjs           # the commit hook, once a clone
node <this folder>/lint.mjs --known      # the module map's reaches today, in the known list's shape
node <this folder>/lint.mjs --modules    # reaches by pair of modules, and the cycles
node <this folder>/lint.mjs --reaches <from> [to]   # every reach out of one module, with its lines
node --test "<this folder>/**/*.test.mjs"   # the rules' own tests
```

A break on a new or changed line is an error; one already in the code is a warning, for the rules that allow old
ones. The rules, their languages and how to add one: the kit's `plugins/project-shape/lint-pack/README.md`.

## In this project

- The comments rule runs at `"level": "warning"`: every comment past a file's top one is a warning, on new and
  touched lines too, so the lint never fails on a comment for now. The old comments go in one purge change
  (issue #226); then `level` comes off the config and a new comment is an error again. `level` is a local change
  to the pack (`rules/comments.mjs`, `lint.mjs`, with a test), to bring to the kit.
- The commit hook is not installed (no `install.mjs`, no `core.hooksPath`): `.git/hooks` holds the Git LFS hooks
  and the cost hook, which a `core.hooksPath` would switch off (D21). Run the lint by hand or read it in CI's
  quality workflow, where it prints and never fails the job.
- `lint.mjs` and `install.mjs` compare their own path with `realpathSync`, so they run from a symlinked folder
  (macOS's temp folder, where the pack's own tests make their repositories). Another local change for the kit.
- The tests: `node --test "tools/lint/**/*.test.mjs"` (Node 22 takes a glob, not a folder).
