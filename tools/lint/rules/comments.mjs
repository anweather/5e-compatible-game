// The comments rule: no comment in code but the one at the top of a file, above its first type (C#) or its first line of code (the rest); one on a new or changed line is an error, one already there a warning, so a codebase can take the rule on without a rewrite; with level "warning" every one is a warning, for a project that purges its comments in one change later.
import { scan } from '../languages.mjs';

export function commentFindings({ path, text, language, changed, level = 'error' }) {
  const { comments, headerEnds } = scan(text, language);
  const rows = text.split('\n');
  const errors = [], warnings = [];
  for (const l of [...comments].sort((a, b) => a - b)) {
    if (l < headerEnds) continue;
    const said = `${path}:${l}: comment: ${rows[l - 1].trim().slice(0, 90)}`;
    if (level !== 'warning' && (changed === 'all' || changed?.has(l))) errors.push(said);
    else warnings.push(said);
  }
  return { errors, warnings };
}
