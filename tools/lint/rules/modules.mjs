// The modules rule: a map (modules.txt) says which module each source file is in and which modules each may use; the references between files are found (C# types, JS and TS imports, Python imports, shader includes), and a reach the map does not allow fails unless it is on the known list (modules-known.txt), whose count may only fall.
import { scan, languageOf } from '../languages.mjs';

export function readMap(text, name = 'modules.txt') {
  const rows = [], uses = new Map(), order = [];
  for (const raw of text.split(/\r?\n/)) {
    const l = raw.trim();
    if (!l || l.startsWith('#')) continue;
    const words = l.split(/\s+/);
    if (words[0] === 'module') {
      const [, module, ...rest] = words;
      if (uses.has(module)) throw new Error(`${name}: module ${module} named twice`);
      uses.set(module, new Set(rest.filter((w) => w !== 'uses')));
      order.push(module);
      continue;
    }
    if (words.length !== 2) throw new Error(`${name}: a row is a path and a module: ${l}`);
    rows.push({ at: words[0], module: words[1] });
  }
  for (const r of rows) if (!uses.has(r.module)) throw new Error(`${name}: no such module ${r.module} (${r.at})`);
  for (const [m, set] of uses) for (const u of set) if (u !== '*' && !uses.has(u)) throw new Error(`${name}: ${m} uses no such module ${u}`);
  return { rows, uses, order };
}

const pattern = (at) => new RegExp('^' + at.split('').map((c) => (c === '*' ? '[^/]*' : c.replace(/[.+?^${}()|[\]\\]/g, '\\$&'))).join('') + (at.endsWith('/') || at.endsWith('*') ? '' : '$'));

export function moduleOf(path, map, root = '') {
  if (!path.startsWith(root)) return null;
  const inner = path.slice(root.length);
  let best = null;
  for (const r of map.rows) {
    r.re ??= pattern(r.at);
    if (r.re.test(inner) && (!best || r.at.length > best.at.length)) best = r;
  }
  return best ? best.module : '';
}

export const allowed = (map, from, to) => from === to || map.uses.get(from)?.has(to) || map.uses.get(from)?.has('*');

const lineAt = (text, index) => text.slice(0, index).split('\n').length;

function typesIn(blanked) {
  let depth = 0, top = '';
  for (const ch of blanked) {
    if (ch === '{') depth++;
    top += depth === 0 || ch === '\n' ? ch : ' ';
    if (ch === '}') depth = Math.max(0, depth - 1);
  }
  const names = new Set();
  for (const m of top.matchAll(/\b(?:class|struct|interface|enum|record)\s+([A-Za-z_]\w*)/g)) names.add(m[1]);
  return names;
}

function csharpReferences(files, map, root) {
  const where = new Map(), blankedOf = new Map();
  for (const [path, text] of files) {
    if (languageOf(path, ['csharp']) !== 'csharp') continue;
    const b = scan(text, 'csharp').blanked.replace(/\benum\s+\w+[^{;]*\{[^}]*\}/g, (s) => s.replace(/[^\n]/g, ' '));
    blankedOf.set(path, b);
    for (const n of typesIn(b)) {
      if (!where.has(n)) where.set(n, new Set());
      where.get(n).add(path);
    }
  }
  const refs = [];
  for (const [path, code] of blankedOf) {
    const own = new Set([...code.matchAll(/\b(?:class|struct|interface|enum|record)\s+([A-Za-z_]\w*)/g)].map((m) => m[1]));
    const members = new Set([...code.matchAll(/[\w>\])?]+\s+([A-Z]\w*)\s*(?=[;={(]|=>)/g)].map((m) => m[1]));
    for (const m of code.matchAll(/(?<![\w.@])[A-Za-z_]\w*/g)) {
      const defs = where.get(m[0]);
      if (!defs || own.has(m[0]) || defs.has(path)) continue;
      const mods = new Set([...defs].map((d) => moduleOf(d, map, root)));
      if (mods.has(moduleOf(path, map, root)) || mods.size > 1) continue;
      if (members.has(m[0]) && !/(?:\bnew|\btypeof\s*\(|\bis|\bas|<|\[)\s*$/.test(code.slice(Math.max(0, m.index - 12), m.index))) continue;
      refs.push({ path, line: lineAt(code, m.index), kind: 'type', target: m[0], defined: [...defs][0] });
    }
  }
  return refs;
}

function resolveFile(candidates, exists) {
  return candidates.find((c) => exists(c)) ?? null;
}

const dirOf = (p) => p.split('/').slice(0, -1).join('/');
function joinPath(dir, rel) {
  const parts = (dir ? dir.split('/') : []);
  for (const s of rel.split('/')) {
    if (s === '' || s === '.') continue;
    if (s === '..') parts.pop();
    else parts.push(s);
  }
  return parts.join('/');
}

const jsEnds = ['', '.ts', '.tsx', '.mts', '.js', '.mjs', '.cjs', '.jsx', '/index.ts', '/index.tsx', '/index.js', '/index.mjs'];

function jsReferences(files, exists) {
  const refs = [];
  for (const [path, text] of files) {
    if (languageOf(path, ['js']) !== 'js') continue;
    const code = scan(text, 'js').blanked;
    const found = /\b(?:from|import|require\s*\(|import\s*\()\s*(['"])(\.{1,2}\/[^'"]*)\1/g;
    for (const m of text.matchAll(found)) {
      if (code.slice(m.index, m.index + 4) !== text.slice(m.index, m.index + 4)) continue;
      const base = joinPath(dirOf(path), m[2]);
      const target = resolveFile(jsEnds.map((e) => base + e), exists);
      if (target) refs.push({ path, line: lineAt(text, m.index), kind: 'import', target: target.split('/').pop(), defined: target });
    }
  }
  return refs;
}

function pythonReferences(files, exists, roots) {
  const refs = [];
  const asFile = (dotted, from) => {
    const rel = dotted.replace(/\./g, '/');
    for (const r of from) {
      const base = r ? `${r.replace(/\/$/, '')}/${rel}` : rel;
      const hit = resolveFile([base + '.py', base + '/__init__.py'], exists);
      if (hit) return hit;
    }
    return null;
  };
  for (const [path, text] of files) {
    if (languageOf(path, ['python']) !== 'python') continue;
    const code = scan(text, 'python').blanked;
    const add = (target, index, name) => { if (target && target !== path) refs.push({ path, line: lineAt(code, index), kind: 'import', target: name, defined: target }); };
    for (const m of code.matchAll(/^[ \t]*from[ \t]+(\.*)([\w.]*)[ \t]+import[ \t]+\(?([\w ,\t]+)/gm)) {
      const dots = m[1].length;
      let roots2 = roots;
      if (dots) { let d = dirOf(path); for (let k = 1; k < dots; k++) d = dirOf(d); roots2 = [d]; }
      for (const name of m[3].split(',').map((s) => s.trim().split(/\s+/)[0]).filter(Boolean)) {
        const sub = asFile(m[2] ? `${m[2]}.${name}` : name, roots2);
        add(sub ?? (m[2] ? asFile(m[2], roots2) : null), m.index, m[2] ? `${m[1]}${m[2]}` : `${m[1]}${name}`);
      }
    }
    for (const m of code.matchAll(/^[ \t]*import[ \t]+([\w., \t]+)/gm)) {
      for (const name of m[1].split(',').map((s) => s.trim().split(/\s+/)[0]).filter(Boolean)) add(asFile(name, roots), m.index, name);
    }
  }
  return refs;
}

function shaderReferences(files) {
  const byBase = new Map();
  for (const [path] of files) if (languageOf(path, ['shader'])) byBase.set(path.split('/').pop(), path);
  const refs = [];
  for (const [path, text] of files) {
    if (!languageOf(path, ['shader'])) continue;
    const code = scan(text, 'shader').blanked;
    const rows = text.split('\n');
    code.split('\n').forEach((row, i) => {
      if (!/#include/.test(row)) return;
      const inc = /#include\s+"([^"]+)"/.exec(rows[i]);
      const target = inc && byBase.get(inc[1].split('/').pop());
      if (target) refs.push({ path, line: i + 1, kind: 'include', target: target.split('/').pop(), defined: target });
    });
  }
  return refs;
}

export function references(files, map, { root = '', exists = (p) => files.has(p), pythonRoots = [''] } = {}) {
  const raw = [...csharpReferences(files, map, root), ...jsReferences(files, exists), ...pythonReferences(files, exists, pythonRoots), ...shaderReferences(files)];
  const refs = [];
  for (const r of raw) {
    const from = moduleOf(r.path, map, root), to = moduleOf(r.defined, map, root);
    if (from && to && from !== to) refs.push({ ...r, from, to });
  }
  const unmapped = [...files.keys()].filter((p) => moduleOf(p, map, root) === '' && languageOf(p));
  return { refs, unmapped };
}

export const reachKey = (r, root = '') => `${r.path.slice(root.length)} ${r.kind}:${r.target}`;

export function reaches(refs, map, root = '') {
  const by = new Map();
  for (const r of refs) {
    if (allowed(map, r.from, r.to)) continue;
    const k = reachKey(r, root);
    if (!by.has(k)) by.set(k, { ...r, key: k, uses: 0, lines: [] });
    const e = by.get(k);
    e.uses++;
    e.lines.push(r.line);
  }
  return by;
}

export function readKnown(text) {
  const keys = new Set();
  for (const raw of text.split(/\r?\n/)) {
    const l = raw.trim();
    if (!l || l.startsWith('#')) continue;
    keys.add(l.split(/\s+/).slice(0, 2).join(' '));
  }
  return keys;
}

export function knownText(now, header = '') {
  const rows = [...now.values()].sort((a, b) => a.key.localeCompare(b.key)).map((r) => `${r.key}  ${r.uses}  ${r.from}>${r.to}`);
  return header + rows.join('\n') + (rows.length ? '\n' : '');
}

export function reachFindings({ files, mapText, knownText: known = '', root = '', exists, pythonRoots, everyFileMapped = true, mapName = 'modules.txt' }) {
  const map = readMap(mapText, mapName);
  const { refs, unmapped } = references(files, map, { root, exists, pythonRoots });
  const now = reaches(refs, map, root);
  const listed = readKnown(known);
  const errors = [];
  let old = 0;
  for (const [k, r] of now) {
    if (listed.has(k)) { old++; continue; }
    errors.push(`${r.path}:${r.lines[0]}: module: ${r.from} reaches ${r.to} (${r.kind} ${r.target}), which ${mapName} does not let ${r.from} use; go through a port ${r.from} may use, or change the map on purpose`);
  }
  if (everyFileMapped) for (const p of unmapped) errors.push(`${p}:1: module: in no module of ${mapName}; give it a row`);
  const gone = [...listed].filter((k) => !now.has(k));
  return { errors, old, listed: listed.size, gone, now, map, refs };
}

export function cycles(edges, names) {
  const index = new Map(), low = new Map(), on = new Set(), stack = [], out = [];
  let i = 0;
  const visit = (v) => {
    index.set(v, i); low.set(v, i); i++; stack.push(v); on.add(v);
    for (const w of edges.get(v) ?? []) {
      if (!index.has(w)) { visit(w); low.set(v, Math.min(low.get(v), low.get(w))); }
      else if (on.has(w)) low.set(v, Math.min(low.get(v), index.get(w)));
    }
    if (low.get(v) === index.get(v)) {
      const part = [];
      let w;
      do { w = stack.pop(); on.delete(w); part.push(w); } while (w !== v);
      if (part.length > 1) out.push(part.sort());
    }
  };
  for (const n of names) if (!index.has(n)) visit(n);
  return out;
}
