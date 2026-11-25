// The lint end to end in a throwaway repository: an old comment warns, a new one fails, a staged one is refused by the hook, a wrong path fails, and the module list ratchets.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { cpSync, mkdtempSync, readFileSync, rmSync, writeFileSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));

function project() {
  const dir = mkdtempSync(path.join(tmpdir(), 'lint-pack-'));
  const git = (...a) => execFileSync('git', ['-C', dir, ...a], { encoding: 'utf8' });
  git('init', '-q', '-b', 'main');
  git('config', 'user.email', 'test@example.invalid');
  git('config', 'user.name', 'test');
  git('config', 'commit.gpgsign', 'false');
  cpSync(here, path.join(dir, 'tools', 'lint'), { recursive: true, filter: (s) => !s.endsWith('.test.mjs') });
  const config = JSON.parse(readFileSync(path.join(here, 'lint.config.example.json'), 'utf8'));
  config.rules.modules.on = true;
  config.rules.modules.map = 'tools/lint/modules.txt';
  config.rules.modules.known = 'tools/lint/modules-known.txt';
  writeFileSync(path.join(dir, 'tools', 'lint', 'lint.config.json'), JSON.stringify(config, null, 2));
  writeFileSync(path.join(dir, 'tools', 'lint', 'modules.txt'), 'module core uses\nmodule app uses core\ncore/ core\napp/ app\n');
  writeFileSync(path.join(dir, 'tools', 'lint', 'modules-known.txt'), '');
  mkdirSync(path.join(dir, 'src', 'core'), { recursive: true });
  mkdirSync(path.join(dir, 'src', 'app'), { recursive: true });
  writeFileSync(path.join(dir, 'src', 'core', 'rules.py'), '"""The rules."""\n\ndef score():\n    return 1  # an old comment\n');
  writeFileSync(path.join(dir, 'src', 'app', 'main.py'), '"""The app."""\nfrom core import rules\n');
  writeFileSync(path.join(dir, 'README.md'), 'Run tools/lint/lint.mjs.\n');
  config.rules.modules.pythonRoots = ['src'];
  writeFileSync(path.join(dir, 'tools', 'lint', 'lint.config.json'), JSON.stringify(config, null, 2));
  git('add', '-A');
  git('commit', '-q', '-m', 'start', '--no-verify');
  const lint = (...a) => spawnSync('node', [path.join(dir, 'tools', 'lint', 'lint.mjs'), ...a], { cwd: dir, encoding: 'utf8' });
  return { dir, git, lint, done: () => rmSync(dir, { recursive: true, force: true }) };
}

test('an old comment is a warning and the run passes; a new one fails on its line', () => {
  const p = project();
  try {
    const clean = p.lint('--base', 'HEAD');
    assert.equal(clean.status, 0, clean.stderr);
    assert.match(clean.stdout, /src\/core\/rules\.py: 1 comment line\(s\) already there/);
    writeFileSync(path.join(p.dir, 'src', 'core', 'rules.py'), '"""The rules."""\n\ndef score():\n    return 1  # an old comment\n\ndef more():\n    # a new comment\n    return 2\n');
    const r = p.lint('--base', 'HEAD');
    assert.equal(r.status, 1);
    assert.match(r.stderr, /error: src\/core\/rules\.py:7: comment: # a new comment/);
  } finally { p.done(); }
});

test('the hook refuses a staged commit with a new comment, and lets a clean one through', () => {
  const p = project();
  try {
    const installed = spawnSync('node', [path.join(p.dir, 'tools', 'lint', 'install.mjs')], { cwd: p.dir, encoding: 'utf8' });
    assert.equal(installed.status, 0, installed.stdout + installed.stderr);
    writeFileSync(path.join(p.dir, 'src', 'app', 'view.ts'), '// The view.\nexport const a = 1; // new\n');
    p.git('add', '-A');
    assert.throws(() => p.git('commit', '-q', '-m', 'with a comment'));
    writeFileSync(path.join(p.dir, 'src', 'app', 'view.ts'), '// The view.\nexport const a = 1;\n');
    p.git('add', '-A');
    p.git('commit', '-q', '-m', 'clean');
    assert.match(p.git('log', '--oneline', '-1'), /clean/);
  } finally { p.done(); }
});

test('with core.hooksPath set (the cost hook\'s .githooks), the lint hook goes there', () => {
  const p = project();
  try {
    p.git('config', 'core.hooksPath', '.githooks');
    const installed = spawnSync('node', [path.join(p.dir, 'tools', 'lint', 'install.mjs')], { cwd: p.dir, encoding: 'utf8' });
    assert.match(installed.stdout, /\.githooks[\\/]pre-commit/);
    writeFileSync(path.join(p.dir, 'src', 'app', 'view.ts'), '// The view.\nexport const a = 1; // new\n');
    p.git('add', '-A');
    assert.throws(() => p.git('commit', '-q', '-m', 'with a comment'));
  } finally { p.done(); }
});

test('a tool or doc path named and not there fails', () => {
  const p = project();
  try {
    writeFileSync(path.join(p.dir, 'README.md'), 'Run tools/lint/lint.mjs, then tools/lint/lnt.mjs.\n');
    const r = p.lint('--base', 'HEAD');
    assert.equal(r.status, 1);
    assert.match(r.stderr, /README\.md:1: path: tools\/lint\/lnt\.mjs is named and is not there/);
  } finally { p.done(); }
});

test('a reach the map does not allow fails; listed by --known it passes; the list may then only shrink', () => {
  const p = project();
  try {
    writeFileSync(path.join(p.dir, 'src', 'core', 'rules.py'), '"""The rules."""\nfrom app import main\n\ndef score():\n    return 1  # an old comment\n');
    p.git('add', '-A');
    p.git('commit', '-q', '-m', 'a reach', '--no-verify');
    const r = p.lint('--base', 'HEAD');
    assert.equal(r.status, 1);
    assert.match(r.stderr, /src\/core\/rules\.py:2: module: core reaches app \(import app\)/);
    const known = p.lint('--known');
    assert.match(known.stdout, /^core\/rules\.py import:app {2}1 {2}core>app$/m);
    writeFileSync(path.join(p.dir, 'tools', 'lint', 'modules-known.txt'), known.stdout);
    const listed = p.lint('--base', 'HEAD');
    assert.equal(listed.status, 0, listed.stderr);
    assert.match(listed.stdout, /1 old/);
    const report = p.lint('--modules');
    assert.match(report.stdout, /1 reach\(es\) the map does not allow, 1 on the known list\n {5}1 {2}core>app\ncycles: app core/);
    assert.match(p.lint('--reaches', 'core', 'app').stdout, /^src\/core\/rules\.py:2  core>app  import:app$/m);
    assert.match(p.lint('--reaches', 'app').stdout, /no reach out of app/);
  } finally { p.done(); }
});

test('in a repository with no commit yet, every staged line is new', () => {
  const dir = mkdtempSync(path.join(tmpdir(), 'lint-pack-'));
  try {
    execFileSync('git', ['-C', dir, 'init', '-q']);
    cpSync(here, path.join(dir, 'lint'), { recursive: true, filter: (s) => !s.endsWith('.test.mjs') });
    writeFileSync(path.join(dir, 'lint', 'lint.config.json'), JSON.stringify({ rules: { comments: { on: true } } }));
    writeFileSync(path.join(dir, 'a.mjs'), '// The file.\nconst a = 1; // no\n');
    execFileSync('git', ['-C', dir, 'add', '-A']);
    const r = spawnSync('node', [path.join(dir, 'lint', 'lint.mjs'), '--staged'], { cwd: dir, encoding: 'utf8' });
    assert.equal(r.status, 1);
    assert.match(r.stderr, /a\.mjs:2: comment/);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
