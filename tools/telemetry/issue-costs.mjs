#!/usr/bin/env node
// issue-costs.mjs: what each GitHub issue cost to find, find again and fix, from the issues'
// Discovery-Cost lines (the agent-cost-telemetry skill's template) and the cost trailers on the
// commits that reference them. Needs `gh` signed in, run inside the repository.
//   node issue-costs.mjs [--label playtest] [--state all|open|closed] [--ref origin/main] [--out report.md] [--csv issues.csv]
// Per issue: times found (the issue itself plus each comment carrying a Discovery-Cost line), the
// summed discovery cost, the fix cost (trailers on commits whose message names "#<n>"), and days open.
// A cost line looks like:  Discovery-Cost: 61,250 ite; 200,526 tokens; 42 min; session "playtest 0.1.8"
// Older free-text shares ("This issue's share: about 50,000 tokens") are read too, as tokens.
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";

const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, all) => {
  if (a.startsWith("--")) acc.push([a.slice(2), all[i + 1]?.startsWith("--") || all[i + 1] === undefined ? true : all[i + 1]]);
  return acc;
}, []));
const run = (cmd, a) => execFileSync(cmd, a, { encoding: "utf8", maxBuffer: 1 << 28 });
const num = s => Number(String(s ?? "").replace(/,/g, "")) || 0;
const fmtK = n => n >= 1e6 ? (n / 1e6).toFixed(2) + "M" : n >= 1e3 ? Math.round(n / 1e3) + "k" : String(Math.round(n));

// Cost lines in a body: ite, tokens and minutes, each optional. Each line is one find; the same
// session named twice on one issue is one find (a cost added again later, not a rediscovery).
function costs(text) {
  const out = [];
  for (const m of (text ?? "").matchAll(/^\s*\**Discovery-Cost:?\**\s*(.*)$/gim)) {
    const v = m[1];
    out.push({ ite: num(v.match(/([\d,.]+)\s*ite/)?.[1]), tokens: num(v.match(/([\d,.]+)\s*tokens/)?.[1]), minutes: num(v.match(/([\d,.]+)\s*min/)?.[1]), session: v.match(/session\s+"([^"]+)"/i)?.[1] });
  }
  return out;
}
// Older free text ("This issue's share: about 50,000 tokens"): used only when the issue has no cost line anywhere.
const prose = text => { const t = (text ?? "").match(/share:?\**\s*(?:is\s*)?about\s*([\d,]+)\s*tokens/i); return t ? [{ ite: 0, tokens: num(t[1]), minutes: 0 }] : []; };
function findsOf(issue) {
  const seen = new Set();
  const lines = [...costs(issue.body), ...issue.comments.flatMap(c => costs(c.body))]
    .filter(c => !c.session || (seen.has(c.session) ? false : (seen.add(c.session), true)));
  // Free text from a body with no cost line is a find of its own, unless a cost line repeats it
  // (the same token figure within 10%: the same session's cost added again as a line).
  const texts = [issue.body, ...issue.comments.map(c => c.body)].filter(b => !costs(b).length).flatMap(prose)
    .filter(p => !lines.some(l => l.tokens && Math.abs(l.tokens - p.tokens) <= 0.1 * l.tokens));
  return [...texts, ...lines];
}

// The fix cost: cost trailers on commits that name the issue.
const ite = b => {
  let s = 0;
  for (const m of b.matchAll(/^[A-Z][A-Za-z]*-Tokens:\s*(.*)$/gm)) {
    const v = m[1], g = k => num(v.match(new RegExp(k + " ([\\d,]+)"))?.[1]);
    s += g("input") + 1.25 * g("cache write") + 0.1 * num(v.match(/([\d,]+) cache read/)?.[1]) + 5 * g("output");
  }
  return s;
};
const ref = args.ref ?? (() => { try { return run("git", ["symbolic-ref", "--short", "refs/remotes/origin/HEAD"]).trim(); } catch { return "HEAD"; } })();
function fixCost(n) {
  let s = 0, commits = 0;
  for (const b of run("git", ["log", ref, "--all", "--format=%B%x1e", "-E", `--grep=#${n}([^0-9]|$)`]).split("\x1e")) if (b.trim()) { s += ite(b); commits++; }
  return { ite: s, commits };
}

const list = ["issue", "list", "--state", args.state ?? "all", "--limit", "500", "--json", "number,title,state,createdAt,closedAt,labels,body,comments,url"];
if (args.label) list.push("--label", args.label);
const issues = JSON.parse(run("gh", list)).map(i => {
  const found = findsOf(i);
  const fix = fixCost(i.number);
  const end = i.closedAt ? Date.parse(i.closedAt) : Date.now();
  return { n: i.number, title: i.title, state: i.state, url: i.url, labels: i.labels.map(l => l.name),
    finds: found.length, ite: found.reduce((s, c) => s + c.ite, 0), tokens: found.reduce((s, c) => s + c.tokens, 0),
    minutes: found.reduce((s, c) => s + c.minutes, 0), fixIte: fix.ite, fixCommits: fix.commits, days: (end - Date.parse(i.createdAt)) / 864e5 };
}).filter(i => i.finds);
if (!issues.length) { console.error("No issues with a Discovery-Cost line" + (args.label ? ` under label ${args.label}` : "") + "."); process.exit(1); }

// Rank: open issues by what finding them has cost so far (ite, else tokens), then times found.
const cost = i => i.ite || i.tokens;
const unit = issues.some(i => i.ite) ? "ite" : "tokens";
const open = issues.filter(i => i.state === "OPEN").sort((a, b) => cost(b) - cost(a) || b.finds - a.finds);
const closed = issues.filter(i => i.state !== "OPEN");
const total = issues.reduce((s, i) => s + cost(i), 0), finds = issues.reduce((s, i) => s + i.finds, 0);
const again = issues.reduce((s, i) => s + Math.max(0, i.finds - 1), 0);
const againCost = issues.reduce((s, i) => s + (i.finds > 1 ? cost(i) * (i.finds - 1) / i.finds : 0), 0);

const out = [`# Issue costs${args.label ? ` (label ${args.label})` : ""}`, "",
  `${issues.length} issues with discovery costs, ${finds} finds in all (${again} of them rediscoveries). Finding them cost ${fmtK(total)} ${unit}, ${fmtK(total / finds)} per find.`,
  `**Rediscovery:** ${fmtK(againCost)} ${unit} (${(100 * againCost / total).toFixed(0)}%) went on finding issues we already knew about.`, "",
  "## Fix first: open issues by what finding them has cost", "",
  `| # | Issue | Found | Discovery cost (${unit}) | Days open |`, "|---|---|---|---|---|",
  ...open.map(i => `| [#${i.n}](${i.url}) | ${i.title} | ${i.finds}× | ${fmtK(cost(i))} | ${Math.round(i.days)} |`), ""];
if (closed.length) {
  out.push("## Closed: found against fixed", "", `| # | Issue | Found | Discovery cost (${unit}) | Fix cost (ite) | Fix commits | Days to fix |`, "|---|---|---|---|---|---|---|",
    ...closed.map(i => `| [#${i.n}](${i.url}) | ${i.title} | ${i.finds}× | ${fmtK(cost(i))} | ${i.fixCommits ? fmtK(i.fixIte) : "-"} | ${i.fixCommits} | ${Math.round(i.days)} |`), "");
  const both = closed.filter(i => i.fixCommits && i.fixIte);
  if (both.length) out.push(`Over ${both.length} fixed issues with fix telemetry, finding cost ${fmtK(both.reduce((s, i) => s + cost(i), 0))} ${unit} and fixing ${fmtK(both.reduce((s, i) => s + i.fixIte, 0))} ite.`, "");
}
const report = out.join("\n");
if (args.out) writeFileSync(args.out, report); else console.log(report);
if (args.csv) writeFileSync(args.csv, ["number,title,state,finds,ite,tokens,minutes,fix_ite,fix_commits,days",
  ...issues.map(i => [i.n, `"${i.title.replace(/"/g, '""')}"`, i.state, i.finds, Math.round(i.ite), i.tokens, i.minutes, Math.round(i.fixIte), i.fixCommits, i.days.toFixed(1)].join(","))].join("\n"));
