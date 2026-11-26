// The languages the lint reads, and one scanner that finds each file's comment lines and spans, code lines and a copy with comments and string insides blanked: a language is an entry in `languages` and a test in languages.test.mjs.
const cKeywordsBeforeRegex = new Set(['return', 'typeof', 'case', 'do', 'else', 'in', 'of', 'new', 'delete', 'void', 'throw', 'yield', 'await', 'instanceof']);

export const languages = {
  csharp: { exts: ['.cs'], line: ['//'], block: [['/*', '*/']], strings: 'csharp', headerUntil: /\b(class|struct|interface|enum|record|namespace|delegate)\s+\w/ },
  shader: { exts: ['.shader', '.hlsl', '.cginc', '.compute', '.glsl', '.usf', '.ush', '.wgsl'], line: ['//'], block: [['/*', '*/']], strings: 'plain' },
  js: { exts: ['.js', '.mjs', '.cjs', '.jsx', '.ts', '.tsx', '.mts', '.cts'], line: ['//'], block: [['/*', '*/']], strings: 'js' },
  python: { exts: ['.py', '.pyi'], line: ['#'], block: [], strings: 'python', docstrings: true },
};

export function languageOf(path, enabled = Object.keys(languages)) {
  for (const name of enabled) {
    const lang = languages[name];
    if (lang && lang.exts.some((e) => path.endsWith(e))) return name;
  }
  return null;
}

export function scan(text, name) {
  const lang = typeof name === 'string' ? languages[name] : name;
  const n = text.length;
  const out = text.split('');
  const comments = new Set();
  const spans = [];
  const code = new Set();
  let line = 1;
  let i = 0;
  let lastSignificant = '';
  let lastWord = '';
  const templates = [];
  const blank = (from, to) => { for (let k = from; k < to; k++) if (out[k] !== '\n') out[k] = ' '; };
  const advance = (to) => { for (; i < to && i < n; i++) if (text[i] === '\n') line++; };
  const markCode = () => { code.add(line); };
  const markComment = (from, to) => { for (let l = from; l <= to; l++) comments.add(l); };
  const startsWith = (s) => text.startsWith(s, i);

  const quoted = (quote, { escapes = true, multiline = false, verbatim = false } = {}) => {
    i += quote.length;
    const from = i;
    while (i < n) {
      if (verbatim && text[i] === '"' && text[i + 1] === '"') { i += 2; continue; }
      if (escapes && !verbatim && text[i] === '\\') { if (text[i + 1] === '\n') line++; i += 2; continue; }
      if (startsWith(quote)) { blank(from, i); i += quote.length; return; }
      if (text[i] === '\n') { if (!multiline) { blank(from, i); return; } line++; }
      i++;
    }
    blank(from, n);
  };

  const pythonDocstring = (quoteAt, quote) => {
    if (!lang.docstrings) return false;
    const rowStart = text.lastIndexOf('\n', quoteAt - 1) + 1;
    if (text.slice(rowStart, quoteAt).replace(/[rRuUbBfF]/g, '').trim() !== '') return false;
    const close = text.indexOf(quote, quoteAt + quote.length);
    if (close < 0) return false;
    const after = text.slice(close + quote.length, (text.indexOf('\n', close) + 1 || n + 1) - 1);
    if (after.trim() !== '' && !after.trim().startsWith('#')) return false;
    const before = text.slice(0, rowStart).replace(/#[^\n]*/g, '').trimEnd();
    return before === '' || before.endsWith(':');
  };

  if (text.startsWith('#!')) while (i < n && text[i] !== '\n') i++;
  while (i < n) {
    const c = text[i];
    if (c === '\n') { line++; i++; continue; }
    if (c === ' ' || c === '\t' || c === '\r') { i++; continue; }
    if (templates.length && c === '}' && templates[templates.length - 1] === 0) {
      templates.pop();
      markCode();
      i++;
      jsTemplate();
      continue;
    }
    if (lang.line.some((t) => startsWith(t))) {
      const from = i;
      comments.add(line);
      while (i < n && text[i] !== '\n') i++;
      spans.push({ from, to: i, kind: 'line' });
      blank(from, i);
      continue;
    }
    const block = lang.block.find(([open]) => startsWith(open));
    if (block) {
      const from = i, fromLine = line;
      const stop = text.indexOf(block[1], i + block[0].length);
      advance(stop < 0 ? n : stop + block[1].length);
      markComment(fromLine, line);
      spans.push({ from, to: i, kind: 'block' });
      blank(from, i);
      continue;
    }
    if (lang.strings === 'python' && (c === '"' || c === "'")) {
      const triple = startsWith(c.repeat(3));
      const quote = triple ? c.repeat(3) : c;
      if (triple && pythonDocstring(i, quote)) {
        const from = i, fromLine = line;
        const stop = text.indexOf(quote, i + 3);
        advance(stop + 3);
        markComment(fromLine, line);
        spans.push({ from, to: i, kind: 'docstring' });
        blank(from, i);
        continue;
      }
      markCode();
      quoted(quote, { multiline: triple });
      lastSignificant = '"';
      continue;
    }
    if (lang.strings === 'csharp' && csharpStringAt()) {
      markCode();
      csharpString();
      lastSignificant = '"';
      continue;
    }
    if ((lang.strings === 'plain' || lang.strings === 'js') && c === '"') { markCode(); quoted('"'); lastSignificant = '"'; continue; }
    if (lang.strings === 'js' && c === "'") { markCode(); quoted("'"); lastSignificant = '"'; continue; }
    if (lang.strings === 'js' && c === '`') { markCode(); i++; jsTemplate(); lastSignificant = '"'; continue; }
    if (lang.strings === 'js' && c === '/' && regexAllowed()) { markCode(); jsRegex(); lastSignificant = '"'; continue; }
    markCode();
    if (/[\w$]/.test(c)) {
      const m = /^[\w$]+/.exec(text.slice(i, i + 64));
      lastWord = m[0];
      lastSignificant = 'w';
      i += m[0].length;
      continue;
    }
    if (templates.length) {
      if (c === '{') templates[templates.length - 1]++;
      else if (c === '}') templates[templates.length - 1]--;
    }
    lastSignificant = c;
    lastWord = '';
    i++;
  }

  function csharpStringAt() {
    const ch = text[i];
    return ch === '"' || ch === "'" || ((ch === '@' || ch === '$') && (text[i + 1] === '"' || ((text[i + 1] === '@' || text[i + 1] === '$') && text[i + 2] === '"')));
  }

  function csharpString() {
    let verbatim = false, interpolated = false;
    while (text[i] === '@' || text[i] === '$') { if (text[i] === '@') verbatim = true; else interpolated = true; i++; }
    if (startsWith('"""')) { quoted('"""', { escapes: false, multiline: true }); return; }
    if (text[i] === "'") { quoted("'"); return; }
    if (!interpolated) { quoted('"', { verbatim, multiline: verbatim }); return; }
    let from = ++i;
    while (i < n) {
      const ch = text[i];
      if (verbatim && ch === '"' && text[i + 1] === '"') { i += 2; continue; }
      if (!verbatim && ch === '\\') { i += 2; continue; }
      if ((ch === '{' && text[i + 1] === '{') || (ch === '}' && text[i + 1] === '}')) { i += 2; continue; }
      if (ch === '"') { blank(from, i); i++; return; }
      if (ch === '\n') { if (!verbatim) { blank(from, i); return; } line++; }
      if (ch === '{') { blank(from, i); i++; csharpHole(); from = i; continue; }
      i++;
    }
    blank(from, n);
  }

  function csharpHole() {
    let depth = 0;
    while (i < n) {
      const ch = text[i];
      if (csharpStringAt()) { csharpString(); continue; }
      if (ch === '\n') line++;
      if (ch === '{') depth++;
      else if (ch === '}') { if (depth === 0) { i++; return; } depth--; }
      i++;
    }
  }

  function regexAllowed() {
    if (lastSignificant === 'w') return cKeywordsBeforeRegex.has(lastWord);
    if (lastSignificant === '"' || lastSignificant === ')' || lastSignificant === ']' || lastSignificant === '}') return false;
    return true;
  }

  function jsRegex() {
    const from = ++i;
    let inClass = false;
    while (i < n && text[i] !== '\n') {
      if (text[i] === '\\') { i += 2; continue; }
      if (text[i] === '[') inClass = true;
      else if (text[i] === ']') inClass = false;
      else if (text[i] === '/' && !inClass) break;
      i++;
    }
    blank(from, i);
    i++;
    while (i < n && /[a-z]/.test(text[i])) i++;
  }

  function jsTemplate() {
    const from = i;
    while (i < n) {
      if (text[i] === '\\') { if (text[i + 1] === '\n') line++; i += 2; continue; }
      if (text[i] === '`') { blank(from, i); i++; return; }
      if (text[i] === '$' && text[i + 1] === '{') { blank(from, i); i += 2; templates.push(0); return; }
      if (text[i] === '\n') line++;
      i++;
    }
    blank(from, n);
  }

  const blanked = out.join('');
  const blankRows = blanked.split('\n');
  let headerEnds = blankRows.length + 1;
  for (let l = 1; l <= blankRows.length; l++) {
    if (!code.has(l)) continue;
    if (!lang.headerUntil || lang.headerUntil.test(blankRows[l - 1])) { headerEnds = l; break; }
  }
  return { comments, spans, code, headerEnds, blanked };
}
