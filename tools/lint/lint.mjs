#!/usr/bin/env node
// The project's lint, copied in from andrew-claude-kit's lint pack: reads lint.config.json beside it, runs the rules switched on there over the repository's files, and fails on an error; a rule's old breaks are warnings and its new ones errors, where the rule says so.
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, realpathSync } from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { languageOf } from './languages.mjs';
import { commentFindings } from './rules/comments.mjs';
import { pathFindings } from './rules/paths.mjs';
import { ownDataFindings } from './rules/own-data.mjs';
import { reachFindings, knownText, cycles } from './rules/modules.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const emptyTree = '4b825dc642cb6eb9a060e54bf8d69288fbee4904';

export const usage = `node <this folder>/lint.mjs [--base <commit>] [--staged] [--warnings] [--config <file>] [--known]
The project's code rules, as lint.config.json beside this file switches them on. A break on a new or changed line
(since the base: where the branch left the first of the config's "base" refs, or with --staged what is about to be
committed) is an error and fails the run; one already in the code is a warning, for the rules that allow old ones.
  --warnings   list every warning, not only how many each file has
  --known      print the module map's reaches today, as the known list holds them (to start or rewrite it)
  --modules    the module map's state: reaches by pair of modules, worst first, and the cycles among modules
  --reaches <from> [to]   every reach out of one module (into another), with its file and lines
Rules: comments (no comment but one at the top of a file), paths (every tools/ or docs/ path named is there),
ownData (no tool names the user's own data folder), modules (the module map and its ratchet). README.md says more.`;

export function gitIn(root) {
  return (...a) => execFileSync('git', ['-C', root, ...a], { encoding: 'utf8', maxBuffer: 1 << 28, stdio: ['ignore', 'pipe', 'ignore'] });
}

export function readConfig(file) {
  const config = JSON.parse(readFileSync(file, 'utf8'));
  config.rules ??= {};
  return config;
}

function baseOf(git, config, { staged, base }) {
  if (base) return base;
  let head = true;
  try { git('rev-parse', '--verify', 'HEAD'); } catch { head = false; }
  if (!head) return emptyTree;
  if (staged) return 'HEAD';
  for (const from of config.base ?? ['origin/main', 'main']) {
    try { return git('merge-base', 'HEAD', from).trim(); } catch { }
  }
  return 'HEAD';
}

export function changedLines(git, from, staged) {
  const changed = new Map();
  const diff = git('diff', '--unified=0', '--no-color', '--no-ext-diff', ...(staged ? ['--cached'] : []), from);
  let path = null;
  for (const row of diff.split('\n')) {
    if (row.startsWith('+++ ')) { path = row === '+++ /dev/null' ? null : row.slice(6); continue; }
    const hunk = /^@@ -\S+ \+(\d+)(?:,(\d+))? @@/.exec(row);
    if (!hunk || path === null) continue;
    if (!changed.has(path)) changed.set(path, new Set());
    const count = hunk[2] === undefined ? 1 : Number(hunk[2]);
    for (let l = Number(hunk[1]); l < Number(hunk[1]) + count; l++) changed.get(path).add(l);
  }
  if (!staged) for (const p of git('ls-files', '--others', '--exclude-standard').split('\n')) if (p) changed.set(p, 'all');
  return changed;
}

export function load({ root, configFile, staged = false, base } = {}) {
  root ??= execFileSync('git', ['-C', here, 'rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim();
  configFile ??= join(here, 'lint.config.json');
  const config = readConfig(configFile);
  const git = gitIn(root);
  const from = baseOf(git, config, { staged, base });
  const changed = changedLines(git, from, staged);
  const listed = (staged ? git('ls-files') : git('ls-files', '--cached', '--others', '--exclude-standard')).split('\n').filter(Boolean);
  const texts = new Map();
  const read = (p) => {
    if (texts.has(p)) return texts.get(p);
    let t = '';
    try { t = staged ? git('show', ':' + p) : readFileSync(join(root, p), 'utf8'); } catch { t = null; }
    texts.set(p, t);
    return t;
  };
  const files = listed.filter((p) => staged || existsSync(join(root, p)));
  const pack = relative(root, here).split(sep).join('/') + '/';
  return { root, config, git, from, changed, files, read, pack, exists: (p) => existsSync(join(root, p)) };
}

const within = (p, include = [''], exclude = []) => include.some((i) => p.startsWith(i)) && !exclude.some((e) => p.startsWith(e));

export function moduleInputs(ctx) {
  const m = ctx.config.rules.modules;
  const root = m.root ?? '';
  const files = new Map();
  for (const p of ctx.files) if (p.startsWith(root) && languageOf(p) && within(p, [root], m.exclude ?? [])) files.set(p, ctx.read(p) ?? '');
  const mapFile = join(ctx.root, m.map), knownFile = join(ctx.root, m.known);
  return {
    files, root, pythonRoots: (m.pythonRoots ?? ['']).map((r) => r), everyFileMapped: m.everyFileMapped ?? true,
    mapText: readFileSync(mapFile, 'utf8'), knownText: existsSync(knownFile) ? readFileSync(knownFile, 'utf8') : '',
    exists: (p) => files.has(p) || ctx.exists(p), mapName: m.map,
  };
}

export function run(ctx, { everyWarning = false, print = console.log } = {}) {
  const { config, changed, files, read } = ctx;
  const rules = config.rules;
  const errors = [];
  const warned = new Map();
  const warn = (rule, list) => { for (const w of list) { const p = w.split(':')[0]; const k = `${rule} ${p}`; warned.set(k, [...(warned.get(k) ?? []), w]); } };

  if (rules.comments?.on) {
    const c = rules.comments;
    for (const p of files) {
      const language = languageOf(p, c.languages);
      if (!language || !within(p, c.include, c.exclude)) continue;
      const text = read(p);
      if (text === null) continue;
      const r = commentFindings({ path: p, text, language, changed: changed.get(p), level: c.level });
      errors.push(...r.errors);
      warn('comment', r.warnings);
    }
  }
  if (rules.paths?.on) {
    const r = pathFindings({ files: files.filter((p) => !p.startsWith(ctx.pack)), read: (p) => read(p) ?? '', exists: ctx.exists, config: rules.paths, changedOf: (p) => changed.get(p) });
    errors.push(...r.errors);
    warn('path', r.warnings);
  }
  if (rules.ownData?.on) {
    const r = ownDataFindings({ files, read: (p) => read(p) ?? '', config: rules.ownData });
    errors.push(...r.errors);
  }
  if (rules.modules?.on) {
    const inputs = moduleInputs(ctx);
    const r = reachFindings(inputs);
    errors.push(...r.errors);
    print(`reaches across the module map (${rules.modules.map}): ${r.old} old, on ${rules.modules.known}${r.gone.length ? `; ${r.gone.length} listed and gone from the code (take them off the list): ${r.gone.join(', ')}` : ''}; ${r.errors.length} new or unmapped`);
  }
  let count = 0;
  for (const [k, list] of [...warned].sort((a, b) => b[1].length - a[1].length)) {
    count += list.length;
    if (everyWarning) for (const w of list) print('warning: ' + w);
    else print(`warning: ${k.split(' ').slice(1).join(' ')}: ${list.length} ${k.split(' ')[0]} line(s) already there`);
  }
  print(`${count} warning(s): already in the code, to be taken out when their lines are next touched`);
  return { errors, warnings: count };
}

export function moduleReport(findings, from, to) {
  const out = [];
  const all = [...findings.now.values()];
  if (from) {
    for (const r of all.filter((x) => x.from === from && (!to || x.to === to)).sort((a, b) => b.uses - a.uses)) out.push(`${r.path}:${r.lines.join(',')}  ${r.from}>${r.to}  ${r.kind}:${r.target}`);
    return out.length ? out : [`no reach out of ${from}${to ? ` into ${to}` : ''}`];
  }
  const pairs = new Map();
  for (const r of all) pairs.set(`${r.from}>${r.to}`, (pairs.get(`${r.from}>${r.to}`) ?? 0) + 1);
  out.push(`${all.length} reach(es) the map does not allow, ${findings.old} on the known list`);
  for (const [k, n] of [...pairs].sort((a, b) => b[1] - a[1])) out.push(`  ${String(n).padStart(4)}  ${k}`);
  const edges = new Map();
  for (const r of findings.refs) { if (!edges.has(r.from)) edges.set(r.from, new Set()); edges.get(r.from).add(r.to); }
  const knots = cycles(edges, findings.map.order);
  out.push(knots.length ? `cycles: ${knots.map((c) => c.join(' ')).join('; ')}` : 'cycles: none');
  return out;
}

function main() {
  const args = process.argv.slice(2);
  if (args.includes('--help') || args.includes('-h')) { console.log(usage); return 0; }
  const opt = (n) => { const i = args.indexOf(n); return i >= 0 ? args[i + 1] : undefined; };
  const ctx = load({ configFile: opt('--config') && resolve(opt('--config')), staged: args.includes('--staged'), base: opt('--base') });
  if (args.includes('--known')) {
    if (!ctx.config.rules.modules) { console.error('lint.config.json has no modules rule'); return 1; }
    const r = reachFindings({ ...moduleInputs(ctx), knownText: '' });
    process.stdout.write(knownText(r.now, `# The reaches across ${ctx.config.rules.modules.map} in the code when this list was made: a line a file and what it reaches, with its uses and the two modules.\n# The lint fails a reach not listed here; this list may only shrink. Made by lint.mjs --known.\n`));
    return 0;
  }
  if (args.includes('--modules') || args.includes('--reaches')) {
    const at = args.indexOf('--reaches');
    const from = at >= 0 ? args[at + 1] : undefined;
    const to = at >= 0 && args[at + 2] && !args[at + 2].startsWith('--') ? args[at + 2] : undefined;
    for (const l of moduleReport(reachFindings(moduleInputs(ctx)), from, to)) console.log(l);
    return 0;
  }
  const { errors } = run(ctx, { everyWarning: args.includes('--warnings') });
  if (errors.length) {
    console.error(errors.map((e) => 'error: ' + e).join('\n'));
    console.error(`${errors.length} error(s) since ${ctx.from.slice(0, 7)}. A comment: say it with a name or a test, or in the docs. A path: fix it or the file. A reach: go through a port the module may use.`);
    return 1;
  }
  console.log('no errors');
  return 0;
}

if (process.argv[1] && realpathSync(resolve(process.argv[1])) === realpathSync(fileURLToPath(import.meta.url))) process.exit(main());
