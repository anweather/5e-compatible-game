#!/usr/bin/env node
// queue.mjs: the brief queue (D15, docs/handoffs/briefs/queue/README.md) from a worker's side: what this machine can
// do, which queued brief it can take, and the claim (the brief moved to queue/taken/ in a commit pushed on dev). The
// runner (runner.mjs) uses it when its inbox is empty; land.sh asks it what this machine has.
//   node queue.mjs caps                 this machine's capabilities, one per line (core, unity, mac)
//   node queue.mjs pick [<repo>]        the brief this machine would take from origin/dev's queue, or nothing
//   node queue.mjs claim [<repo>]       take it: prints the path in queue/taken/, or nothing when there's none
// Capabilities: WORKER_CAPS ("core unity"), else ~/.worker/caps, else found: core everywhere; mac on macOS; unity
// where a Unity Editor is installed on Windows or macOS (the Linux VMs have one but no licence and no GPU).
// Run copied beside runner.mjs in ~/.worker, so it imports nothing from the repo.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

export const QUEUE = 'docs/handoffs/briefs/queue';
const base = path.join(os.homedir(), '.worker');

export function caps({ env = process.env, platform = process.platform, exists = fs.existsSync, read = f => fs.readFileSync(f, 'utf8') } = {}) {
  let given = env.WORKER_CAPS;
  if (given == null) { try { given = read(path.join(base, 'caps')); } catch { } }
  if (given != null) return new Set(['core', ...given.split(/[\s,]+/).filter(Boolean)]);
  const found = new Set(['core']);
  if (platform === 'darwin') found.add('mac');
  const editors = { win32: 'C:/Program Files/Unity/Hub/Editor', darwin: '/Applications/Unity/Hub/Editor' }[platform];
  if (editors && exists(editors)) found.add('unity');
  return found;
}

// Whether the queue is on for this machine: WORKER_QUEUE (1/on or 0/off), else ~/.worker/queue holding on or off,
// else on for u2 only (D15's first self-landing worker).
export function queueOn(name, { env = process.env, read = f => fs.readFileSync(f, 'utf8') } = {}) {
  let v = env.WORKER_QUEUE;
  if (v == null) { try { v = read(path.join(base, 'queue')); } catch { } }
  if (v == null) return name === 'u2';
  return /^\s*(1|on|yes|true)\s*$/i.test(v);
}

// A brief's head: "Needs: unity (a machine with an Editor)", "Area: tools/workers/**, docs/x.md", "Branch: name".
export function parseBrief(text) {
  const field = k => text.match(new RegExp(`^${k}:[ \\t]*(.*)$`, 'mi'))?.[1].trim();
  const needs = field('Needs')?.split(/[\s|(]/)[0].toLowerCase() || null;
  const area = (field('Area') ?? '').replace(/\([^)]*\)/g, ' ').split(/[\s,;]+/)
    .filter(t => /[/.*]/.test(t)).map(t => t.replace(/`/g, ''));
  return { needs, area, branch: field('Branch')?.replace(/`/g, '') || null };
}

export function meets(needs, have) {
  if (needs === 'any' || needs === 'core') return true;
  return needs === 'unity' || needs === 'mac' ? have.has(needs) : false; // no Needs, or one we don't know: not ours
}

// The literal part of an area, as path segments: "tools/workers/**" and "tools/workers/*.cmd" are both tools/workers.
const segments = a => {
  const lit = a.split('*')[0];
  const cut = a.includes('*') ? lit.slice(0, lit.lastIndexOf('/') + 1) : lit;
  return cut.split('/').filter(Boolean);
};
// Two areas overlap when one is inside the other. A brief that names no area could touch anything.
export function overlaps(a, b) {
  if (!a.length || !b.length) return true;
  return a.some(x => b.some(y => {
    const [p, q] = [segments(x), segments(y)];
    const n = Math.min(p.length, q.length);
    return p.slice(0, n).every((s, i) => s === q[i]);
  }));
}

// briefs: [{ name, text }] in the queue; taken: the same for queue/taken/. The first by name this machine can do
// whose area no taken brief holds, with why each other one was passed over.
export function pick(briefs, taken, have) {
  const held = taken.map(t => ({ ...t, ...parseBrief(t.text) }));
  const passed = [];
  for (const b of [...briefs].sort((x, y) => x.name.localeCompare(y.name))) {
    const p = parseBrief(b.text);
    if (!meets(p.needs, have)) { passed.push(`${b.name}: needs ${p.needs ?? '(not said)'}`); continue; }
    const clash = held.find(t => overlaps(p.area, t.area));
    if (clash) { passed.push(`${b.name}: its area overlaps ${clash.name}, taken`); continue; }
    return { pick: { ...b, ...p }, passed };
  }
  return { pick: null, passed };
}

const git = (repo, args, { input, env = {} } = {}) => spawnSync('git', args, { cwd: repo, encoding: 'utf8', input, env: { ...process.env, ...env } });
const ok = r => { if (r.status !== 0) throw new Error(`git: ${r.stderr.trim()}`); return r.stdout.trim(); };

// The queue as origin/dev has it (the worker's checkout may be on any branch, with changes: it isn't touched).
export function readQueue(repo, rev = 'origin/dev') {
  const list = dir => ok(git(repo, ['ls-tree', '--name-only', `${rev}:${dir}`])).split('\n')
    .filter(f => f.endsWith('.md') && f !== 'README.md')
    .map(f => ({ name: f, text: ok(git(repo, ['show', `${rev}:${dir}/${f}`])) }));
  const taken = git(repo, ['cat-file', '-e', `${rev}:${QUEUE}/taken`]).status === 0 ? list(`${QUEUE}/taken`) : [];
  return { briefs: list(QUEUE), taken };
}

// Claim the next brief: the move to taken/ is made as a commit on origin/dev from a scratch index (the same tree
// `git mv` + commit would give) and pushed. A push that loses the race to another worker fetches and picks again.
export function claim(repo, have, who, { tries = 5, log = () => { } } = {}) {
  const index = path.join(os.tmpdir(), `queue-index-${process.pid}`);
  const scratch = (args, input) => git(repo, args, { input, env: { GIT_INDEX_FILE: index } });
  try {
    for (let i = 0; i < tries; i++) {
      ok(git(repo, ['fetch', '-q', 'origin', 'dev']));
      const { briefs, taken } = readQueue(repo);
      const { pick: p, passed } = pick(briefs, taken, have);
      for (const why of passed) log(`queue: passed over ${why}`);
      if (!p) return null;
      const head = ok(git(repo, ['rev-parse', 'origin/dev']));
      ok(scratch(['read-tree', head]));
      const blob = ok(git(repo, ['rev-parse', `${head}:${QUEUE}/${p.name}`]));
      ok(scratch(['update-index', '--force-remove', `${QUEUE}/${p.name}`]));
      ok(scratch(['update-index', '--add', '--cacheinfo', `100644,${blob},${QUEUE}/taken/${p.name}`]));
      const tree = ok(scratch(['write-tree']));
      const commit = ok(scratch(['commit-tree', tree, '-p', head], `queue: ${who} takes ${p.name}\n`));
      if (git(repo, ['push', '-q', 'origin', `${commit}:refs/heads/dev`]).status === 0) {
        log(`queue: took ${p.name} (${commit.slice(0, 8)})`);
        return { ...p, taken: `${QUEUE}/taken/${p.name}` };
      }
      log(`queue: lost the race for ${p.name}; picking again`);
    }
    log(`queue: ${tries} pushes refused; giving up until the next look`);
    return null;
  } finally {
    fs.rmSync(index, { force: true });
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [cmd, repo = process.cwd()] = process.argv.slice(2);
  const have = caps();
  if (cmd === 'caps') console.log([...have].join('\n'));
  else if (cmd === 'pick') {
    ok(git(repo, ['fetch', '-q', 'origin', 'dev']));
    const { briefs, taken } = readQueue(repo);
    const { pick: p, passed } = pick(briefs, taken, have);
    for (const why of passed) console.error(`passed over ${why}`);
    if (p) console.log(p.name);
  } else if (cmd === 'claim') {
    const p = claim(repo, have, os.hostname(), { log: m => console.error(m) });
    if (p) console.log(p.taken);
  } else { console.error('node queue.mjs caps | pick [<repo>] | claim [<repo>]'); process.exit(2); }
}
