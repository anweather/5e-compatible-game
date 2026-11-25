// The paths rule: every path under the watched folders (tools/, docs/ by default) named in a live file is a file or folder that is there, from the repository's root or from a folder above the file that names it; the history (logs, finished specs, studies) keeps the paths of its day and is not read, and a glob, a placeholder or a made folder is not a path.
export const readable = /\.(mjs|cjs|js|ts|tsx|py|cs|md|txt|sh|ps1|json|ya?ml|toml|html|shader|hlsl|compute|csproj|props)$|(^|\/)\.gitignore$/i;

export const isTest = (f) => /\.test\.[cm]?[jt]sx?$|(^|\/)test_[^/]*\.py$|_test\.py$|Tests?\.cs$|(^|\/)tests?\//.test(f);

export function thereFrom(file, p, exists) {
  if (exists(p)) return true;
  const dirs = file.split('/').slice(0, -1);
  for (let i = dirs.length; i > 0; i--) if (exists(`${dirs.slice(0, i).join('/')}/${p}`)) return true;
  return false;
}

export function namedPaths(text, prefixes) {
  const words = prefixes.map((p) => p.replace(/\/$/, '').replace(/[.+?^${}()|[\]\\]/g, '\\$&')).join('|');
  const named = new RegExp(`(?<![A-Za-z0-9_.-])(?:${words})\\/[A-Za-z0-9_./-]*`, 'g');
  const found = [];
  for (const m of text.matchAll(named)) {
    const next = text[m.index + m[0].length] ?? '';
    if (next && '*<${['.includes(next)) continue;
    const p = m[0].replace(/[./-]+$/, '');
    if (!p.includes('/')) continue;
    found.push({ path: p, line: text.slice(0, m.index).split('\n').length });
  }
  return found;
}

export function pathFindings({ files, read, exists, config, changedOf = () => undefined }) {
  const prefixes = config.prefixes ?? ['tools/', 'docs/'];
  const history = (config.history ?? []).map((h) => new RegExp(h));
  const made = config.made ?? [];
  const ignore = config.ignore ?? [];
  const strict = config.strict ?? true;
  const errors = [], warnings = [];
  for (const f of files) {
    if (!readable.test(f) || history.some((h) => h.test(f)) || f.includes('/fixtures/') || ((config.skipTests ?? true) && isTest(f))) continue;
    const changed = changedOf(f);
    for (const { path, line } of namedPaths(read(f), prefixes)) {
      if (made.some((m) => path === m || path.startsWith(m + '/'))) continue;
      if (ignore.some((m) => path === m || path.startsWith(m))) continue;
      if (thereFrom(f, path, exists)) continue;
      const said = `${f}:${line}: path: ${path} is named and is not there`;
      if (strict || changed === 'all' || changed?.has(line)) errors.push(said);
      else warnings.push(said);
    }
  }
  return { errors, warnings };
}
