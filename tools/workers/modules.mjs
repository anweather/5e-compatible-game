#!/usr/bin/env node
// modules.mjs: which core test projects a change runs, from modules.json (the map), core/Chem.Tests.props (how the
// Chemistry tests split) and the core/*/*.csproj files (who compiles what, who references whom).
//   node modules.mjs choose [file...]      (files, or one per line on stdin) prints shell lines for gates.sh:
//                                           mode=all|some|none, run=(...), smoke=(...); why goes to stderr
//   node modules.mjs overlap <ours> <theirs> (two files of paths) exit 0 when theirs (dev's new commits) shares a
//                                           module, a test project or the fail-safe with ours (rerun the gates),
//                                           3 when they're apart (push); why on stdout
//   node modules.mjs slnf <out> <project...> writes a solution filter of core/Game.Core.sln (for one dotnet test run)
//   node modules.mjs check                  the map agrees with the csproj files and the props, or exit 1 with why
// Anything it can't read or decide runs everything: a wrong guess here must cost time, never a missed test.
import { existsSync, readdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { dirname, join, posix, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));

export function globRe(glob) {
  let re = "";
  for (let i = 0; i < glob.length; i++) {
    const c = glob[i];
    if (c === "*" && glob[i + 1] === "*") {
      i++;
      if (glob[i + 1] === "/") { i++; re += "(?:.*/)?"; } else re += ".*";
    } else if (c === "*") re += "[^/]*";
    else if (c === "?") re += "[^/]";
    else re += c.replace(/[.+^${}()|[\]\\]/g, "\\$&");
  }
  return new RegExp(`^${re}$`);
}

const attr = (tag, name) => (tag.match(new RegExp(`\\b${name}="([^"]*)"`)) || [])[1];
const tags = (xml, name) => [...xml.replace(/<!--[\s\S]*?-->/g, "").matchAll(new RegExp(`<${name}\\b[^>]*>`, "g"))].map(m => m[0]);
const norm = p => posix.normalize(p.replace(/\\/g, "/"));

function filesFor(root, pattern) {
  const fixed = pattern.split("/");
  const cut = fixed.findIndex(p => /[*?]/.test(p));
  const base = cut < 0 ? pattern : fixed.slice(0, cut).join("/");
  const re = globRe(pattern), out = [];
  const walk = rel => {
    const abs = join(root, rel);
    if (!existsSync(abs)) return;
    if (statSync(abs).isFile()) { if (re.test(rel)) out.push(rel); return; }
    for (const e of readdirSync(abs)) walk(rel ? `${rel}/${e}` : e);
  };
  walk(cut < 0 ? base : base);
  return out;
}

export function load(root = join(here, "..", "..")) {
  const map = JSON.parse(readFileSync(join(root, "tools/workers/modules.json"), "utf8"));
  const propsPath = "core/Chem.Tests.props";
  const props = readFileSync(join(root, propsPath), "utf8");
  const propsDir = posix.dirname(propsPath);
  const vars = { MSBuildThisFileDirectory: `${propsDir}/` };
  for (const m of props.matchAll(/<(\w+)>([^<]*)<\/\1>/g)) vars[m[1]] = m[2].replace(/\$\((\w+)\)/g, (_, v) => vars[v] ?? "");
  const expand = (s, dir) => s.split(";").map(x => x.trim()).filter(Boolean).map(x => {
    if (x.startsWith("@(")) return x;
    const v = x.replace(/\$\((\w+)\)/g, (_, k) => vars[k] ?? "");
    return norm(x.startsWith("$(") ? v : posix.join(dir, v));
  });
  const items = {};
  for (const tag of tags(props, "ChemTests\\w+")) {
    const name = tag.match(/<(\w+)/)[1];
    (items[name] ??= []).push({ include: expand(attr(tag, "Include") ?? "", propsDir), remove: expand(attr(tag, "Remove") ?? "", propsDir) });
  }
  const inItem = (name, file, seen = new Set()) => {
    if (seen.has(name) || !items[name]) return false;
    seen.add(name);
    const hit = pats => pats.some(p => p.startsWith("@(") ? inItem(p.slice(2, -1), file, new Set(seen)) : globRe(p).test(file));
    let inside = false;
    for (const step of items[name]) {
      if (step.include.length && hit(step.include)) inside = true;
      if (step.remove.length && hit(step.remove)) inside = false;
    }
    return inside;
  };
  const projects = {};
  for (const dir of readdirSync(join(root, "core"))) {
    const csproj = `core/${dir}/${dir}.csproj`;
    if (!existsSync(join(root, csproj))) continue;
    const xml = readFileSync(join(root, csproj), "utf8");
    const pdir = `core/${dir}`;
    const imports = tags(xml, "Import").some(t => /Chem\.Tests\.props/.test(attr(t, "Project") ?? ""));
    const compiles = [], refs = [];
    const take = (src, d) => {
      for (const t of tags(src, "Compile")) compiles.push(...expand(attr(t, "Include") ?? "", d));
      for (const t of tags(src, "ProjectReference")) refs.push(posix.basename(norm(attr(t, "Include")), ".csproj"));
    };
    take(xml, pdir);
    if (imports) take(props.replace(/<ChemTests\w+[^>]*>/g, ""), propsDir);
    projects[dir] = { compiles, refs, test: dir.endsWith(".Tests") };
  }
  const compiledBy = file => Object.keys(projects).filter(p => projects[p].compiles.some(c =>
    c.startsWith("@(") ? inItem(c.slice(2, -1), file) : globRe(c).test(file)));
  const modules = map.modules.map(m => ({ ...m, res: (m.paths ?? []).map(globRe) }));
  const problems = [];
  for (const m of modules) {
    for (const p of [...(m.projects ?? []), ...(m.reads ?? []), ...(m.after ?? []), ...(m.runs ?? [])]) if (!projects[p]) problems.push(`module ${m.name} names ${p}, which isn't a core project`);
    if (m.props && !items[m.props]) problems.push(`module ${m.name} reads ${m.props}, which ${propsPath} doesn't have`);
  }
  for (const name of Object.keys(items)) if (!modules.some(m => m.props === name)) problems.push(`${propsPath}'s ${name} has no module`);
  return { root, map, modules, projects, items, inItem, compiledBy, problems, smoke: map.smoke ?? "TestCategory=Unit" };
}

export function withDependents(m, start, stop = []) {
  const out = new Set(start);
  let grew = true;
  while (grew) {
    grew = false;
    for (const [name, p] of Object.entries(m.projects)) {
      if (!out.has(name) && p.refs.some(r => out.has(r) && !stop.includes(r))) { out.add(name); grew = true; }
    }
  }
  return out;
}

export function moduleOf(m, file) {
  return m.modules.find(mod => mod.res.some(r => r.test(file)) || (mod.props && m.inItem(mod.props, file)));
}

export function choose(m, files, { derive = true } = {}) {
  const tests = Object.keys(m.projects).filter(p => m.projects[p].test).sort();
  const why = [], seen = new Map();
  const everything = reason => ({ mode: "all", run: tests, smoke: [], why: [...why, reason], modules: [...seen.keys()], all: true });
  if (m.problems.length) return everything(`everything: the map doesn't agree with core/ (${m.problems[0]})`);
  if (!files.length) return everything("everything: no changed files found");
  const run = new Set();
  for (const file of files) {
    const mod = moduleOf(m, file);
    if (!mod) return everything(`everything: ${file} is in no module (the fail-safe)`);
    const stop = mod.stop ?? [];
    const code = mod.runs ? new Set(mod.runs) : withDependents(m, mod.projects ?? [], stop);
    const compiled = derive && !mod.runs ? m.compiledBy(file) : [];
    for (const p of withDependents(m, compiled, stop)) code.add(p);
    const picks = [...code, ...(mod.reads ?? [])].filter(p => m.projects[p]?.test);
    for (const p of picks) run.add(p);
    const s = seen.get(mod.name) ?? { files: [], picks: new Set(), lib: (mod.projects ?? []).filter(p => !m.projects[p]?.test), stop };
    s.files.push(file); for (const p of picks) s.picks.add(p);
    seen.set(mod.name, s);
  }
  for (const [name, s] of seen) {
    const what = s.picks.size === tests.length ? "every test project" : s.picks.size ? [...s.picks].sort().join(", ") : "no dotnet tests";
    const runs = m.modules.find(x => x.name === name)?.runs;
    const via = runs ? ` (only these in full: what else references ${s.lib.join(", ")} gets the smoke)` : s.lib.length ? ` (through ${s.lib.join(", ")} and what references it${s.stop.length ? `, not past ${s.stop.join(", ")}: what's beyond gets the smoke` : ""})` : "";
    const eg = s.files.slice(0, 2).join(", ") + (s.files.length > 2 ? `, +${s.files.length - 2}` : "");
    why.push(`${name} [${eg}]: ${what}${via}`);
  }
  const node = [...seen.keys()].some(n => m.modules.find(x => x.name === n)?.node);
  if (run.size === tests.length) return { mode: "all", run: tests, smoke: [], why, modules: [...seen.keys()], node, all: true };
  if (!run.size) return { mode: "none", run: [], smoke: [], why, modules: [...seen.keys()], node };
  return { mode: "some", run: [...run].sort(), smoke: tests.filter(t => !run.has(t)), why, modules: [...seen.keys()], node };
}

export function overlap(m, ours, theirs) {
  const a = choose(m, ours), b = choose(m, theirs);
  if (a.all) return { rerun: true, why: "this landing ran everything" };
  if (b.all) return { rerun: true, why: `dev's new commits run everything: ${b.why.at(-1)}` };
  const shared = a.run.filter(p => b.run.includes(p));
  if (shared.length) return { rerun: true, why: `dev's new commits run ${shared.join(", ")} too` };
  const tested = name => { const x = m.modules.find(y => y.name === name); return x && (x.node || x.projects?.length || x.reads?.length); };
  const both = a.modules.filter(n => b.modules.includes(n) && tested(n));
  if (both.length) return { rerun: true, why: `dev's new commits touch ${both.join(", ")} too` };
  return { rerun: false, why: `dev's new commits (${b.modules.join(", ") || "nothing"}) share no tested module with this landing (${a.modules.join(", ")})` };
}

export function check(m) {
  const problems = [...m.problems];
  if (problems.length) return problems;
  const tests = Object.keys(m.projects).filter(p => m.projects[p].test);
  for (const [name, p] of Object.entries(m.projects)) {
    const needFor = mod => p.test ? [name] : mod?.runs ? mod.runs.filter(x => m.projects[x]?.test) : [...withDependents(m, [name], mod?.stop ?? [])].filter(x => m.projects[x].test);
    for (const c of p.compiles) {
      const files = c.startsWith("@(") ? filesFor(m.root, "unity/Assets/Prototype/Chemistry/Tests/*.cs").filter(f => m.inItem(c.slice(2, -1), f)) : filesFor(m.root, c);
      for (const f of files) {
        const need = needFor(moduleOf(m, f));
        const sel = choose(m, [f], { derive: false });
        const missing = need.filter(t => !sel.run.includes(t));
        if (missing.length) problems.push(`${f}: ${name} compiles it, but the map runs ${sel.run.join(", ") || "nothing"}, not ${missing.join(", ")}`);
      }
    }
  }
  for (const f of filesFor(m.root, "unity/Assets/Prototype/Chemistry/Tests/*.cs")) {
    const inside = Object.keys(m.items).filter(i => m.inItem(i, f));
    if (inside.length !== 1) problems.push(`${f} is in ${inside.length} of the props' item lists (${inside.join(", ")})`);
  }
  if (!tests.length) problems.push("no core test projects found");
  return problems;
}

const lines = text => text.split(/\r?\n/).map(s => s.trim()).filter(Boolean);

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const [cmd, ...args] = process.argv.slice(2);
  const root = process.env.MODULES_ROOT || join(here, "..", "..");
  let m;
  try { m = load(root); } catch (e) { m = { problems: [`can't read the map: ${e.message}`], projects: {}, modules: [] }; }
  if (cmd === "choose") {
    const files = args.length ? args : lines(readFileSync(0, "utf8"));
    const c = choose(m, files);
    for (const w of c.why) process.stderr.write(`    ${w}\n`);
    console.log(`mode=${c.mode}`);
    console.log(`run=(${c.run.join(" ")})`);
    console.log(`smoke=(${c.smoke.join(" ")})`);
    console.log(`smoke_filter='${m.smoke ?? "TestCategory=Unit"}'`);
  } else if (cmd === "overlap" && args.length === 2) {
    try {
      const o = overlap(m, lines(readFileSync(args[0], "utf8")), lines(readFileSync(args[1], "utf8")));
      console.log(o.why);
      process.exit(o.rerun ? 0 : 3);
    } catch (e) { console.log(`can't tell (${e.message}): rerun`); process.exit(0); }
  } else if (cmd === "slnf" && args.length >= 2) {
    const [out, ...names] = args;
    const sln = join(root, "core", "Game.Core.sln");
    const path = relative(dirname(resolve(out)), sln).replace(/\\/g, "/");
    writeFileSync(out, JSON.stringify({ solution: { path, projects: names.map(n => `${n}/${n}.csproj`) } }, null, 2) + "\n");
  } else if (cmd === "check") {
    const problems = check(m);
    for (const p of problems) console.log(p);
    if (problems.length) process.exit(1);
    console.log(`modules.json agrees with core/: ${m.modules.length} modules, ${Object.keys(m.projects).length} projects, ${relative(process.cwd(), join(root, "core/Chem.Tests.props"))}'s ${Object.keys(m.items).length} item lists.`);
  } else {
    console.log(readFileSync(fileURLToPath(import.meta.url), "utf8").split("\n").slice(1, 10).map(l => l.replace(/^\/\/ ?/, "")).join("\n"));
    process.exit(2);
  }
}
