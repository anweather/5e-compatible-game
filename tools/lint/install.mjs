#!/usr/bin/env node
// Has git refuse a commit that breaks the lint (lint.mjs --staged), in this clone and its worktrees, in git's hooks folder or the one core.hooksPath names; each clone runs this once, and a pre-commit hook it did not write is left alone unless --force.
import { execFileSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, readFileSync, realpathSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const mark = 'written by the lint pack';

export function install({ root, lint, force = false }) {
  const hooks = path.resolve(root, execFileSync('git', ['-C', root, 'rev-parse', '--git-path', 'hooks'], { encoding: 'utf8' }).trim());
  const hook = path.join(hooks, 'pre-commit');
  if (existsSync(hook) && !readFileSync(hook, 'utf8').includes(mark) && !force) return { hook, written: false, why: 'a pre-commit hook is already there and was not written by the lint pack; add the line `node <lint> --staged` to it, or run again with --force' };
  const rel = path.relative(root, lint).split(path.sep).join('/');
  mkdirSync(hooks, { recursive: true });
  writeFileSync(hook, `#!/bin/sh
# The project's lint on every commit (${mark}: ${rel}).
top="$(git rev-parse --show-toplevel)"
[ -f "$top/${rel}" ] || exit 0
exec node "$top/${rel}" --staged >/dev/null
`);
  chmodSync(hook, 0o755);
  return { hook, written: true };
}

if (process.argv[1] && realpathSync(path.resolve(process.argv[1])) === realpathSync(fileURLToPath(import.meta.url))) {
  if (process.argv.includes('--help')) { console.log('node <this folder>/install.mjs [--force]'); process.exit(0); }
  const here = path.dirname(fileURLToPath(import.meta.url));
  const root = execFileSync('git', ['-C', here, 'rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim();
  const r = install({ root, lint: path.join(here, 'lint.mjs'), force: process.argv.includes('--force') });
  console.log(r.written ? `installed: ${r.hook}` : `not installed: ${r.why}`);
  process.exit(r.written ? 0 : 1);
}
