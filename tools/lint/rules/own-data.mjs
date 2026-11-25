// The own-data rule: no tool names the user's or the player's own data folder (their saves, settings, documents) but the few files listed as allowed, each with its reason, such as the one that names it to refuse it; an error on any line, old or new.
import { isTest } from './paths.mjs';

export function ownDataFindings({ files, read, config }) {
  const within = config.in ?? ['tools/'];
  const patterns = (config.patterns ?? []).map((p) => new RegExp(p));
  const allowed = config.allowed ?? {};
  const kinds = /\.(mjs|cjs|js|ts|py|cs|sh|ps1)$/;
  const errors = [];
  if (!patterns.length) return { errors, warnings: [] };
  for (const f of files) {
    if (!within.some((w) => f.startsWith(w)) || !kinds.test(f) || allowed[f] || ((config.skipTests ?? true) && isTest(f))) continue;
    read(f).split('\n').forEach((row, i) => {
      const hit = patterns.find((p) => p.test(row));
      if (hit) errors.push(`${f}:${i + 1}: own data: a tool names the user's own data folder (${hit.source}); go through the one place allowed to, or list this file in the config with its reason: ${row.trim().slice(0, 90)}`);
    });
  }
  return { errors, warnings: [] };
}
