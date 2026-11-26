#!/usr/bin/env node
// The one-change comment purge (issue #226): takes every comment the lint's comments rule reports out of the files it checks, keeping each file's top comment, preprocessor lines, strings and tool directives, and checks each result against the lint's own scanner before writing it. Usage: node tools/purge-comments.mjs [--check] [paths...]
import { execFileSync } from 'node:child_process';
import { readFileSync, realpathSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { languageOf, scan } from './lint/languages.mjs';

const here = dirname(fileURLToPath(import.meta.url));

const licence = /SPDX-License-Identifier|\bCopyright \(c\)|Licensed under\b/i;
export const keepers = {
  csharp: [/^\/\/\s*<auto-?generated/i, /^\/\/\s*ReSharper (disable|restore)\b/, /^\/\/\s*@formatter:(on|off)\b/, licence],
  shader: [licence],
  js: [/^\/\/\s*@ts-(ignore|expect-error|nocheck|check)\b/, /^\/\/\/\s*<reference\s/, /^\/\/\s*eslint-(disable|enable)/, /^\/\*\s*(eslint|global|istanbul|c8)\s/, /^\/\*!/, licence],
  python: [/^#\s*(noqa|type:|pragma:|pylint:|fmt:|isort:)/, /^#.*-\*-\s*coding/, licence],
};
const keeps = (body, language) => (keepers[language] ?? [licence]).some((k) => k.test(body));

export const commentSpans = (text, language) => scan(text, language).spans;

const lineOf = (text, at) => {
  let l = 1;
  for (let k = text.indexOf('\n'); k >= 0 && k < at; k = text.indexOf('\n', k + 1)) l++;
  return l;
};

const MARK = '\u0000';
const isSpace = (ch) => ch === ' ' || ch === '\t';

function cut(text, span, language) {
  const { from, to } = span;
  const ls = text.lastIndexOf('\n', from - 1) + 1;
  let le = text.indexOf('\n', to);
  if (le < 0) le = text.length;
  const before = text.slice(ls, from);
  const after = text.slice(to, le);
  if (before.trim() === '' && after.trim() === '') {
    let mark = MARK;
    if (span.kind === 'docstring') mark = pythonBodyLeftEmpty(text, ls, le, before) ? before + 'pass' : MARK;
    return text.slice(0, ls) + mark + text.slice(le);
  }
  if (after.trim() === '') {
    let a = from;
    while (a > ls && isSpace(text[a - 1])) a--;
    return text.slice(0, a) + text.slice(le);
  }
  if (before.trim() === '') {
    let b = to;
    while (b < le && isSpace(text[b])) b++;
    return text.slice(0, from) + text.slice(b);
  }
  let a = from, b = to;
  const cb = text[from - 1], ca = text[to];
  if (isSpace(cb) && isSpace(ca)) while (b < le && isSpace(text[b])) b++;
  else if (isSpace(cb) && ';,)]'.includes(ca)) while (a > ls && isSpace(text[a - 1])) a--;
  else if ('(['.includes(cb) && isSpace(ca)) while (b < le && isSpace(text[b])) b++;
  else if (!isSpace(cb) && !isSpace(ca) && /[\w$]/.test(cb) && /[\w$]/.test(ca) && language !== 'python') return text.slice(0, a) + ' ' + text.slice(b);
  return text.slice(0, a) + text.slice(b);
}

function pythonBodyLeftEmpty(text, ls, le, indent) {
  const prev = text.slice(0, ls).replace(/#[^\n]*/g, '').trimEnd();
  if (!prev.endsWith(':')) return false;
  const rest = text.slice(le + 1).split('\n').find((r) => r.trim() !== '');
  if (rest === undefined) return true;
  return (rest.length - rest.trimStart().length) < indent.length;
}

const opens = (row) => /[{([]\s*$/.test(row);
const closes = (row) => /^\s*[})\]]/.test(row);

function tidy(text) {
  const rows = text.split('\n');
  const out = [];
  let k = 0;
  while (k < rows.length) {
    if (rows[k] !== MARK && !(rows[k].trim() === '' && groupHasMark(rows, k))) { out.push(rows[k]); k++; continue; }
    let end = k, blank = false;
    while (end < rows.length && (rows[end] === MARK || rows[end].trim() === '')) { if (rows[end] !== MARK) blank = true; end++; }
    const prev = out.length ? out[out.length - 1] : null;
    const next = end < rows.length ? rows[end] : null;
    const edge = prev === null || next === null || opens(prev) || closes(next);
    if (next === null && end === rows.length && rows[rows.length - 1] === '') out.push('');
    else if (blank && !edge) out.push('');
    k = end;
  }
  return out.join('\n');
}

function groupHasMark(rows, k) {
  for (let j = k; j < rows.length && (rows[j] === MARK || rows[j].trim() === ''); j++) if (rows[j] === MARK) return true;
  return false;
}

const typeLine = /\b(class|struct|interface|enum|record|delegate)\s+\w/;
const isDoc = (body) => body.startsWith('///');

function wholeLine(text, s) {
  const ls = text.lastIndexOf('\n', s.from - 1) + 1;
  let le = text.indexOf('\n', s.to);
  if (le < 0) le = text.length;
  return text.slice(ls, s.from).trim() === '' && text.slice(s.to, le).trim() === '';
}

function firstBlock(text, spans) {
  const block = [];
  for (const s of spans) {
    if (block.length) {
      const gap = text.slice(block[block.length - 1].to, s.from);
      if (gap.trim() !== '' || (gap.match(/\n/g) ?? []).length > 1) break;
    }
    if (!wholeLine(text, s)) break;
    block.push(s);
  }
  return block;
}

function leadBlock(text, spans, language, scanned) {
  const rows = text.split('\n');
  const blankRows = scanned.blanked.split('\n');
  const whole = new Set(spans.filter((s) => wholeLine(text, s)).flatMap((s) => {
    const a = lineOf(text, s.from), b = lineOf(text, s.to);
    return Array.from({ length: b - a + 1 }, (_, k) => a + k);
  }));
  let first = -1;
  if (language === 'csharp') {
    for (let l = scanned.headerEnds; l <= rows.length; l++) if (scanned.code.has(l) && typeLine.test(blankRows[l - 1])) { first = l; break; }
    if (first < 0) return [];
    let l = first - 1;
    while (l > 0 && (whole.has(l) || /^\s*\[/.test(rows[l - 1]))) l--;
    const top = l + 1;
    return spans.filter((s) => whole.has(lineOf(text, s.from)) && lineOf(text, s.from) >= top && lineOf(text, s.from) < first);
  }
  if (language === 'shader') {
    const s = spans[0];
    if (!s) return [];
    const at = lineOf(text, s.from);
    const codeBefore = [...scanned.code].filter((l) => l < at).length;
    return codeBefore <= 2 && wholeLine(text, s) ? firstBlock(text, spans) : [];
  }
  return [];
}

export function plainWords(body) {
  let t = body;
  if (t.startsWith('///')) {
    t = t.slice(3)
      .replace(/<see\s+(?:cref|langword|href)="([^"]*)"\s*\/>/g, '$1')
      .replace(/<(?:paramref|typeparamref)\s+name="([^"]*)"\s*\/>/g, '$1')
      .replace(/<[^>]+>/g, '')
      .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
    return [t.trim()].filter(Boolean);
  }
  if (t.startsWith('//')) return [t.slice(2).replace(/^ /, '').trimEnd()];
  return t.replace(/^\/\*+/, '').replace(/\*+\/$/, '').split('\n').map((r) => r.replace(/^\s*\* ?/, '').trimEnd().trim()).filter(Boolean);
}

export function purge(text, language) {
  if (text.includes(MARK)) return text;
  const scanned = scan(text, language);
  const all = commentSpans(text, language).filter((s) => !keeps(text.slice(s.from, s.to), language));
  const header = all.filter((s) => lineOf(text, s.from) < scanned.headerEnds);
  const plainHeader = header.filter((s) => !isDoc(text.slice(s.from, s.to)) && wholeLine(text, s));
  const firstCode = Math.min(...scanned.code, Infinity);
  const above = plainHeader.filter((s) => lineOf(text, s.from) < firstCode);
  const kept = new Set(above.length ? above : firstBlock(text, plainHeader));
  let lead = [];
  if (!kept.size) {
    const block = leadBlock(text, all, language, scanned);
    lead = block.flatMap((s) => plainWords(text.slice(s.from, s.to)));
  }
  const spans = all.filter((s) => !kept.has(s));
  if (!spans.length) return text;
  let out = text;
  for (const s of spans.sort((a, b) => b.from - a.from)) out = cut(out, s, language);
  if (out.includes(MARK)) out = tidy(out);
  if (lead.length) {
    const bang = out.startsWith('#!') ? out.indexOf('\n') + 1 : out.startsWith('﻿') ? 1 : 0;
    out = out.slice(0, bang) + lead.map((r) => (r ? '// ' + r : '//')).join('\n') + '\n' + out.slice(bang);
  }
  return out;
}

const significant = (text, language) => {
  const blanked = scan(text, language).blanked;
  return (language === 'python' ? blanked.replace(/^[ \t]*pass[ \t]*$/gm, '') : blanked).replace(/\s+/g, '');
};

export function verify(before, after, language) {
  const problems = [];
  if (significant(before, language) !== significant(after, language)) problems.push('the code outside comments changed');
  const s = scan(after, language);
  const kept = new Set(commentSpans(after, language).filter((c) => keeps(after.slice(c.from, c.to), language)).map((c) => lineOf(after, c.from)));
  const left = [...s.comments].filter((l) => l >= s.headerEnds && !kept.has(l));
  if (left.length) problems.push(`comments left on line(s) ${left.slice(0, 5).join(', ')}`);
  return problems;
}

const within = (p, include = [''], exclude = []) => include.some((i) => p.startsWith(i)) && !exclude.some((e) => p.startsWith(e));

export function filesToPurge(root, config) {
  const c = config.rules.comments;
  const listed = execFileSync('git', ['-C', root, 'ls-files'], { encoding: 'utf8', maxBuffer: 1 << 28 }).split('\n').filter(Boolean);
  return listed.filter((p) => languageOf(p, c.languages) && within(p, c.include, c.exclude));
}

function main() {
  const args = process.argv.slice(2);
  const check = args.includes('--check');
  const root = execFileSync('git', ['-C', here, 'rev-parse', '--show-toplevel'], { encoding: 'utf8' }).trim();
  const config = JSON.parse(readFileSync(join(here, 'lint', 'lint.config.json'), 'utf8'));
  const named = args.filter((a) => !a.startsWith('--'));
  const files = named.length ? named : filesToPurge(root, config);
  let changed = 0, removed = 0, failed = 0;
  for (const p of files) {
    const language = languageOf(p, config.rules.comments.languages);
    if (!language) continue;
    let before;
    try { before = readFileSync(join(root, p), 'utf8'); } catch { continue; }
    const after = purge(before, language);
    if (after === before) continue;
    const problems = verify(before, after, language);
    if (problems.length) { failed++; console.error(`${p}: left as it was: ${problems.join('; ')}`); continue; }
    changed++;
    removed += before.split('\n').length - after.split('\n').length;
    if (!check) writeFileSync(join(root, p), after);
  }
  console.log(`${check ? 'would change' : 'changed'} ${changed} file(s), ${removed} line(s) fewer${failed ? `; ${failed} left as they were` : ''}`);
  return failed || (check && changed) ? 1 : 0;
}

if (process.argv[1] && realpathSync(resolve(process.argv[1])) === realpathSync(fileURLToPath(import.meta.url))) process.exit(main());
