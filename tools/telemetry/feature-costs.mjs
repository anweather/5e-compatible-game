#!/usr/bin/env node
// feature-costs.mjs: what each feature on a branch cost, from the cost trailers commit-cost.mjs
// writes (Worker-*, Coordinator-*, Host-*), beside the lines it changed, with some analysis:
// the most and least costly, outliers, how cost follows lines changed, and clusters.
//   node tools/telemetry/feature-costs.mjs [--ref origin/dev] [--since 2026-10-28] [--out report.md] [--csv costs.csv]
//     [--price <$ per million input tokens>]   adds dollars (Opus ratios: output 5x, cache write 1.25x, cache read 0.1x)
// A feature is a merge on the branch's first-parent line ("Merge <branch>: ..."): its cost is every
// trailer on the commits it brought in plus the merge's own Coordinator-* lines. A commit made
// straight on the branch with cost trailers (a host pass) is a feature of its own. Lines changed are
// the merge's diff against its first parent; "code" lines leave out .meta, images, scenes, prefabs,
// assets and the goldens.
// Cost is counted in input-token equivalents (ite): new input + 1.25 x cache write + 0.1 x cache read
// + 5 x output, so the four kinds of token add up in one number that prices scale linearly.
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";

const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, all) => {
  if (a.startsWith("--")) acc.push([a.slice(2), all[i + 1]?.startsWith("--") || all[i + 1] === undefined ? true : all[i + 1]]);
  return acc;
}, []));
const ref = args.ref ?? "origin/dev";
const git = (...a) => execFileSync("git", a, { encoding: "utf8", maxBuffer: 1 << 28 });

// One commit's cost, from its message: every role's lines (Worker-, Coordinator-, Host-) added up.
const num = s => Number(String(s).replace(/,/g, "")) || 0;
function costOf(body, only = ["Worker", "Coordinator", "Host"]) {
  const c = { input: 0, cacheWrite: 0, cacheRead: 0, output: 0, minutes: 0, replies: 0, tools: 0, failed: 0, roles: new Set(), agents: new Set(), accounts: new Set(), trouble: 0 };
  for (const line of body.split("\n")) {
    const m = line.match(/^(Worker|Coordinator|Host)(-[A-Za-z]+)?:\s*(.*)$/);
    if (!m) continue;
    const [, role, key, v] = m;
    if (!only.includes(role)) continue;
    if (!key) { c.roles.add(role); c.agents.add(role === "Worker" ? v.trim() : role.toLowerCase()); continue; }
    if (key === "-Account") c.accounts.add(v.split(" ")[0]);
    if (key === "-Time") c.minutes += num(v.match(/([\d,.]+)\s*min/)?.[1]);
    if (key === "-Tokens") {
      const g = k => num(v.match(new RegExp(k + " ([\\d,]+)"))?.[1]);
      c.input += g("input"); c.cacheWrite += g("cache write"); c.output += g("output");
      c.cacheRead += num(v.match(/([\d,]+) cache read/)?.[1]);
      c.replies += num(v.match(/([\d,]+) replies/)?.[1]);
    }
    if (key === "-Tools") { c.tools += num(v.match(/([\d,]+) calls/)?.[1]); c.failed += num(v.match(/([\d,]+) failed/)?.[1]); }
    if (key === "-Trouble") c.trouble += 1;
  }
  return c;
}
function add(a, b) {
  for (const k of ["input", "cacheWrite", "cacheRead", "output", "minutes", "replies", "tools", "failed", "trouble"]) a[k] += b[k];
  for (const k of ["roles", "agents", "accounts"]) for (const x of b[k]) a[k].add(x);
  return a;
}
// A feature's cost is what building it took: the workers' and host passes' lines. The coordinator's
// merge lines are overhead (all that account did since its last merge: queueing, review, idle hours,
// restarts), reported apart; --with-overhead counts them in.
const BUILD = args["with-overhead"] ? ["Worker", "Coordinator", "Host"] : ["Worker", "Host"];
const ite = c => c.input + 1.25 * c.cacheWrite + 0.1 * c.cacheRead + 5 * c.output;
const priced = c => args.price ? ite(c) * Number(args.price) / 1e6 : null;

// Lines a diff changed: all, and code only.
const notCode = /\.(meta|png|jpe?g|mp4|unity|prefab|asset|mat|anim|controller|shadergraph)$|(^|\/)goldens?\/|\.golden\./i;
function linesOf(from, to) {
  let all = 0, code = 0, files = 0;
  for (const l of git("diff", "--numstat", from, to).split("\n")) {
    const [a, d, f] = l.split("\t"); if (!f) continue;
    const n = (a === "-" ? 0 : +a) + (d === "-" ? 0 : +d);
    files++; all += n; if (!notCode.test(f)) code += n;
  }
  return { all, code, files };
}

// The features: the first-parent line of the branch.
const since = args.since ? [`--since=${args.since}`] : [];
const recs = git("log", "--first-parent", ...since, "--format=%H%x1f%P%x1f%cI%x1f%s%x1f%B%x1e", ref).split("\x1e").map(r => r.trim()).filter(Boolean);
const features = [];
for (const r of recs) {
  const [h, parents, date, subject, body] = r.split("\x1f");
  const ps = parents.split(" ");
  const bodies = [body];
  if (ps.length > 1) for (const b of git("log", "--format=%B%x1e", `${ps[0]}..${ps[1]}`).split("\x1e")) if (b.trim()) bodies.push(b);
  const sum = roles => bodies.reduce((a, b) => add(a, costOf(b, roles)), costOf("", roles));
  const c = sum(BUILD), o = sum(["Coordinator"]);
  if (!c.roles.size && !o.roles.size) continue; // no telemetry: before the cost hook, or a docs commit
  const name = subject.match(/^Merge (?:remote-tracking branch '[^']+' into )?([^:\s]+)/)?.[1]
    ?? subject.replace(/^\w+: /, "").split(/[,(;]/)[0].trim().slice(0, 48);
  const lines = linesOf(ps[0], h);
  features.push({ hash: h.slice(0, 7), date: date.slice(0, 16).replace("T", " "), name, subject, commits: bodies.length, ...lines, c, ite: ite(c), usd: priced(c), overhead: ite(o) });
}
// Commits with only the coordinator's lines (its own tooling, say) count as overhead, not features.
const overheadOnly = features.filter(f => f.ite === 0);
features.splice(0, features.length, ...features.filter(f => f.ite > 0));
if (!features.length) { console.error(`No commits with cost trailers on ${ref}.`); process.exit(1); }

// Statistics.
const median = xs => { const s = [...xs].sort((a, b) => a - b), m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };
const mad = xs => { const m = median(xs); return median(xs.map(x => Math.abs(x - m))); };
const pearson = (xs, ys) => {
  const n = xs.length, mx = xs.reduce((a, b) => a + b) / n, my = ys.reduce((a, b) => a + b) / n;
  let sxy = 0, sxx = 0, syy = 0;
  for (let i = 0; i < n; i++) { sxy += (xs[i] - mx) * (ys[i] - my); sxx += (xs[i] - mx) ** 2; syy += (ys[i] - my) ** 2; }
  return sxx && syy ? sxy / Math.sqrt(sxx * syy) : NaN;
};
const ranks = xs => { const o = xs.map((x, i) => [x, i]).sort((a, b) => a[0] - b[0]), r = []; o.forEach(([, i], k) => r[i] = k); return r; };
const spearman = (xs, ys) => pearson(ranks(xs), ranks(ys));
const L = x => Math.log10(Math.max(x, 1));

const fmtK = n => n >= 1e6 ? (n / 1e6).toFixed(2) + "M" : n >= 1e3 ? (n / 1e3).toFixed(0) + "k" : String(Math.round(n));
const money = f => f.usd != null ? ` | $${f.usd.toFixed(2)}` : "";
const total = features.reduce((a, f) => add(a, f.c), costOf(""));
const sorted = [...features].sort((a, b) => b.ite - a.ite);

// Cost against code lines, on log scales (both spread over orders of magnitude).
const withCode = features.filter(f => f.code > 0);
const lx = withCode.map(f => L(f.code)), ly = withCode.map(f => L(f.ite));
const r = pearson(lx, ly), rho = spearman(withCode.map(f => f.code), withCode.map(f => f.ite));
// The fit log(ite) = a + b log(lines): b < 1 means bigger changes cost less per line.
const n = lx.length, mx = lx.reduce((a, b) => a + b, 0) / n, my = ly.reduce((a, b) => a + b, 0) / n;
const b = lx.reduce((s, x, i) => s + (x - mx) * (ly[i] - my), 0) / lx.reduce((s, x) => s + (x - mx) ** 2, 0);
const a0 = my - b * mx;
for (const f of withCode) f.resid = L(f.ite) - (a0 + b * L(f.code));

// Outliers: a robust z (median and MAD) of the cost, and of the residual from the fit.
const z = (xs, x) => { const m = median(xs), d = mad(xs) * 1.4826 || 1e-9; return (x - m) / d; };
const logC = features.map(f => L(f.ite)), res = withCode.map(f => f.resid);
const costOut = features.filter(f => Math.abs(z(logC, L(f.ite))) > 2);
const fitOut = withCode.filter(f => Math.abs(z(res, f.resid)) > 2).sort((p, q) => q.resid - p.resid);

// Clusters: k-means (k = 3) on standardised log(code lines) and log(cost), from fixed seeds (deterministic).
function kmeans(pts, k) {
  const s = [...pts].sort((p, q) => p[0] + p[1] - q[0] - q[1]);
  let cs = Array.from({ length: k }, (_, i) => s[Math.round(i * (s.length - 1) / (k - 1))].slice()), as = [];
  for (let it = 0; it < 50; it++) {
    as = pts.map(p => cs.map(c => (p[0] - c[0]) ** 2 + (p[1] - c[1]) ** 2).reduce((bi, d, i, ds) => d < ds[bi] ? i : bi, 0));
    cs = cs.map((c, i) => { const m = pts.filter((_, j) => as[j] === i); return m.length ? [m.reduce((s, p) => s + p[0], 0) / m.length, m.reduce((s, p) => s + p[1], 0) / m.length] : c; });
  }
  return as;
}
const sd = xs => { const m = xs.reduce((a, b) => a + b, 0) / xs.length; return Math.sqrt(xs.reduce((s, x) => s + (x - m) ** 2, 0) / xs.length) || 1; };
const std = (xs) => { const m = xs.reduce((a, b) => a + b, 0) / xs.length, s = sd(xs); return xs.map(x => (x - m) / s); };
const k = Math.min(3, withCode.length);
const sx = std(lx), sy = std(ly);
const as = k >= 2 ? kmeans(sx.map((x, i) => [x, sy[i]]), k) : withCode.map(() => 0);
const clusters = Array.from({ length: k }, (_, i) => withCode.filter((_, j) => as[j] === i))
  .filter(c => c.length).sort((p, q) => median(p.map(f => f.ite)) - median(q.map(f => f.ite)));

// By who did the work.
const byAgent = {};
for (const f of features) for (const a of f.c.agents) (byAgent[a] ??= []).push(f);

// The report.
const row = f => `| ${f.date} | ${f.name} | ${fmtK(f.ite)}${money(f)} | ${Math.round(f.c.minutes)} | ${fmtK(f.c.output)} | ${f.code} | ${f.all} | ${f.code ? fmtK(f.ite / f.code) : "-"} | ${[...f.c.agents].join(", ")} |`;
const head = `| When | Feature | Cost (ite)${args.price ? " | $" : ""} | Min | Output | Code lines | All lines | ite / code line | Who |\n|---|---|---|${args.price ? "---|" : ""}---|---|---|---|---|---|`;
const out = [];
out.push(`# Feature costs on ${ref}`, "");
out.push(`${features.length} features with cost telemetry, ${features[features.length - 1].date} to ${features[0].date}. Cost is in input-token equivalents (ite): new input + 1.25 × cache write + 0.1 × cache read + 5 × output.${args.price ? ` Dollars at $${args.price} per million input tokens.` : ""}`, "");
out.push(`**Total:** ${fmtK(ite(total))} ite${args.price ? ` ($${(ite(total) * args.price / 1e6).toFixed(2)})` : ""}, ${Math.round(total.minutes)} agent minutes, ${fmtK(total.output)} output tokens, ${fmtK(total.cacheRead)} cache read, ${total.tools} tool calls (${total.failed} failed), ${total.trouble} commits with trouble (compactions, API errors).`, "");
const cr = ite({ ...costOf(""), cacheRead: total.cacheRead }), out5 = 5 * total.output, cw = 1.25 * total.cacheWrite;
const oh = [...features, ...overheadOnly].reduce((s, f) => s + f.overhead, 0);
if (!args["with-overhead"]) out.push(`**Coordination overhead**, apart from the above and below: ${fmtK(oh)} ite, ${(100 * oh / (oh + ite(total))).toFixed(0)}% of all spend. It's the coordinator's merge lines, which cover everything that account did between merges, so it follows the clock more than the feature.${overheadOnly.length ? ` ${overheadOnly.length} commits carry only overhead (${overheadOnly.map(f => f.name).join(", ")}).` : ""} --with-overhead counts it in.`, "");
out.push(`**Where the cost is:** cache reads ${(100 * cr / ite(total)).toFixed(0)}%, output ${(100 * out5 / ite(total)).toFixed(0)}%, cache writes ${(100 * cw / ite(total)).toFixed(0)}%, new input ${(100 * total.input / ite(total)).toFixed(0)}%.`, "");
out.push("## Most costly", "", head, ...sorted.slice(0, 8).map(row), "");
out.push("## Least costly", "", head, ...sorted.slice(-6).reverse().map(row), "");
out.push("## Cost against lines changed", "");
out.push(`Over ${withCode.length} features with code changes: Pearson r = ${r.toFixed(2)} on log scales, Spearman ρ = ${rho.toFixed(2)}. The fit is cost ≈ ${fmtK(10 ** a0)} × lines^${b.toFixed(2)} ite${b < 1 ? ": bigger changes cost less per line" : ": bigger changes cost more per line"}. Median ${fmtK(median(withCode.map(f => f.ite / f.code)))} ite per code line.`, "");
out.push("## Outliers", "");
out.push(costOut.length ? `**By cost** (robust z > 2 on log cost): ${costOut.map(f => `${f.name} (${fmtK(f.ite)})`).join(", ")}.` : "**By cost:** none (robust z > 2 on log cost).", "");
out.push(fitOut.length ? "**Against the fit** (cost far from what its lines predict):" : "**Against the fit:** none.", "");
for (const f of fitOut) out.push(`- ${f.name}: ${fmtK(f.ite)} for ${f.code} code lines, ${(10 ** f.resid).toFixed(1)}× ${f.resid > 0 ? "more" : "less"} than predicted.`);
out.push("", "## Clusters (k-means on log code lines and log cost)", "");
clusters.forEach((c, i) => out.push(`- **Cluster ${i + 1}** (${c.length}): median ${median(c.map(f => f.code))} code lines, ${fmtK(median(c.map(f => f.ite)))} ite, ${Math.round(median(c.map(f => f.c.minutes)))} min. ${c.map(f => f.name).join(", ")}.`));
out.push("", "## By who did it", "", "| Who | Features | Cost (ite) | Median per feature | Min |", "|---|---|---|---|---|");
for (const [a, fs] of Object.entries(byAgent).sort((p, q) => q[1].reduce((s, f) => s + f.ite, 0) - p[1].reduce((s, f) => s + f.ite, 0)))
  out.push(`| ${a} | ${fs.length} | ${fmtK(fs.reduce((s, f) => s + f.ite, 0))} | ${fmtK(median(fs.map(f => f.ite)))} | ${Math.round(fs.reduce((s, f) => s + f.c.minutes, 0))} |`);
out.push("", "(A feature worked by several agents counts under each.)", "");
out.push("## Every feature", "", head, ...sorted.map(row), "");
const report = out.join("\n");
if (args.out) writeFileSync(args.out, report); else console.log(report);
if (args.csv) {
  const cols = ["hash", "date", "name", "commits", "files", "all", "code", "ite", "usd", "minutes", "input", "cacheWrite", "cacheRead", "output", "replies", "tools", "failed", "trouble", "agents"];
  writeFileSync(args.csv, [cols.join(","), ...features.map(f => cols.map(k => {
    const v = k in f ? f[k] : k === "agents" ? [...f.c.agents].join(" ") : f.c[k];
    return typeof v === "string" && /[,"]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v ?? "";
  }).join(","))].join("\n"));
}
