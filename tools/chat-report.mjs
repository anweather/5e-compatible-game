#!/usr/bin/env node
// chat-report.mjs: what the chat logs say (docs/design/chat-telemetry.md, #59): prompts, outcome
// counts, prompt to world, where the time went, tokens and cost, by kind, the slowest and dearest
// ten, and what players asked for that Claude refused, which feeds the backlog.
//   node tools/chat-report.mjs <folder> [--json]
// <folder> holds chat-log-<date>.jsonl files, or is a player's data folder: the records are in its
// chat-records subfolder, and any it holds itself are from before they moved. A record with no "in the world" time (chatbench, or a
// game that didn't reach it) counts its `applied` mark instead, and the report says how many did.
// A line with `of` is a later word on the record with that id (ChatLog.Afterwards).
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const BUILDING = new Set(["add_building", "add_wing", "set_building", "add_building_part"]);
const WEATHER = new Set(["set_weather", "set_time"]);
const RULE = new Set(["add_material", "set_material", "add_reaction", "set_reaction", "add_spell", "set_spell", "tune"]);

export function read(folder) {
  const records = new Map();
  const later = [];
  let bad = 0;
  const moved = join(folder, "chat-records");
  const files = dir => readdirSync(dir).filter(f => /^chat-log-.*\.jsonl$/.test(f)).sort().map(f => join(dir, f));
  for (const file of [...files(folder), ...(existsSync(moved) ? files(moved) : [])])
    for (const line of readFileSync(file, "utf8").split("\n").filter(Boolean)) {
      try {
        const o = JSON.parse(line);
        if (o.of) later.push(o);
        else records.set(o.id, o);
      } catch {
        bad++;
      }
    }
  for (const { of, afterwards } of later) if (records.has(of)) records.get(of).afterwards = afterwards;
  return { records: [...records.values()], bad };
}

export function kindOf(r) {
  if (r.outcome !== "applied") return r.outcome;
  const used = new Set(r.tools.filter(t => t.ok).map(t => t.name));
  const any = set => [...set].some(t => used.has(t));
  if (any(BUILDING)) return "building";
  if (any(WEATHER)) return "weather";
  if (any(RULE)) return "rule";
  return used.has("set_look") ? "look" : "other";
}

// Nearest rank, so a percentile is always a run that happened.
function percentile(values, p) {
  const sorted = values.filter(v => v != null).sort((a, b) => a - b);
  return sorted.length ? sorted[Math.max(0, Math.ceil(sorted.length * p) - 1)] : null;
}
const median = values => percentile(values, 0.5);
const sum = values => values.reduce((a, b) => a + (b ?? 0), 0);
const toWorld = r => r.at.inWorld ?? r.at.applied;
const reached = r => r.outcome === "applied";

function byKind(records) {
  const kinds = {};
  for (const r of records) (kinds[kindOf(r)] ??= []).push(r);
  return Object.entries(kinds).sort(([a], [b]) => a.localeCompare(b)).map(([kind, rs]) => ({
    kind,
    prompts: rs.length,
    medianDoneMs: median(rs.map(r => r.at.done)),
    medianWorldMs: median(rs.filter(reached).map(toWorld)),
    medianOutput: median(rs.map(r => r.tokens.output)),
    medianCost: median(rs.map(r => r.tokens.cost)),
  }));
}

// Each split's share of all the time in runs that finished: a cut-off run has no model time to split.
function shares(records) {
  const whole = records.filter(r => r.split.model != null);
  const total = sum(whole.map(r => r.split.total));
  const of = key => (total ? sum(whole.map(r => r.split[key])) / total : null);
  return { startingUp: of("startingUp"), model: of("model"), tools: of("tools"), leftOver: of("leftOver") };
}

const brief = r => ({ prompt: r.prompt, kind: kindOf(r), outcome: r.outcome, doneMs: r.at.done, cost: r.tokens.cost, output: r.tokens.output, turns: r.tokens.turns });
const top = (records, by) => [...records].sort((a, b) => (by(b) ?? -1) - (by(a) ?? -1)).slice(0, 10).map(brief);

export function report({ records, bad }) {
  const outcomes = {};
  for (const r of records) outcomes[r.outcome] = (outcomes[r.outcome] ?? 0) + 1;
  const applied = records.filter(reached);
  const toWorlds = applied.map(toWorld);
  return {
    prompts: records.length,
    unreadableLines: bad,
    outcomes,
    world: {
      medianMs: median(toWorlds),
      p90Ms: percentile(toWorlds, 0.9),
      measuredInTheGame: applied.filter(r => r.at.inWorld != null).length,
      upToTheRoomsAnswer: applied.filter(r => r.at.inWorld == null && r.at.applied != null).length,
    },
    shares: shares(records),
    perPrompt: {
      input: median(records.map(r => r.tokens.input)),
      cacheWrite: median(records.map(r => r.tokens.cacheWrite)),
      cacheRead: median(records.map(r => r.tokens.cacheRead)),
      output: median(records.map(r => r.tokens.output)),
      cost: median(records.map(r => r.tokens.cost)),
      turns: median(records.map(r => r.tokens.turns)),
    },
    totalCost: sum(records.map(r => r.tokens.cost)),
    byKind: byKind(records),
    slowest: top(records, r => r.at.done),
    dearest: top(records, r => r.tokens.cost),
    refused: records.filter(r => r.outcome === "refused").map(r => ({ prompt: r.prompt, why: r.why })),
    reverted: records.filter(r => r.afterwards?.reverted).length,
    fixedByTheNext: records.filter(r => r.afterwards?.fixedBy).length,
  };
}

const seconds = ms => (ms == null ? "-" : (ms / 1000).toFixed(1) + " s");
const percent = share => (share == null ? "-" : (share * 100).toFixed(0) + "%");
const dollars = cost => (cost == null ? "-" : "$" + cost.toFixed(4));
const tokens = n => (n == null ? "-" : Math.round(n).toLocaleString("en-GB"));

export function text(r) {
  const out = [];
  const list = rows => rows.forEach((x, i) => out.push(`${i + 1}. ${seconds(x.doneMs)}, ${dollars(x.cost)}, ${x.turns} turns, ${x.kind}: ${x.prompt}`));
  out.push(`# Chat report`, "", `${r.prompts} prompts: ${Object.entries(r.outcomes).map(([k, n]) => `${n} ${k}`).join(", ")}.`);
  if (r.unreadableLines) out.push(`${r.unreadableLines} lines couldn't be read.`);
  out.push("", `**Prompt to world:** median ${seconds(r.world.medianMs)}, p90 ${seconds(r.world.p90Ms)}`
    + ` (${r.world.measuredInTheGame} measured in the game, ${r.world.upToTheRoomsAnswer} only up to the room's answer).`);
  out.push(`**Time:** starting up ${percent(r.shares.startingUp)}, the model ${percent(r.shares.model)}, tools ${percent(r.shares.tools)}, left over ${percent(r.shares.leftOver)}.`);
  out.push(`**Per prompt (median):** ${tokens(r.perPrompt.input)} input, ${tokens(r.perPrompt.cacheWrite)} cache write, ${tokens(r.perPrompt.cacheRead)} cache read, ${tokens(r.perPrompt.output)} output, ${dollars(r.perPrompt.cost)}, ${r.perPrompt.turns ?? "-"} turns. All together ${dollars(r.totalCost)}.`);
  out.push(`**Afterwards:** ${r.reverted} reverted, ${r.fixedByTheNext} fixed by the next prompt.`);
  out.push("");
  out.push("| Kind | Prompts | To world | Done | Output | Cost |", "|---|---:|---:|---:|---:|---:|");
  for (const k of r.byKind) out.push(`| ${k.kind} | ${k.prompts} | ${seconds(k.medianWorldMs)} | ${seconds(k.medianDoneMs)} | ${tokens(k.medianOutput)} | ${dollars(k.medianCost)} |`);
  out.push("", "## Slowest");
  list(r.slowest);
  out.push("", "## Dearest");
  list(r.dearest);
  out.push("", "## Asked for, and refused");
  if (!r.refused.length) out.push("Nothing.");
  for (const x of r.refused) out.push(`- ${x.prompt}: ${x.why}`);
  return out.join("\n");
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const args = process.argv.slice(2);
  const folder = args.find(a => !a.startsWith("--"));
  if (!folder) {
    console.error("usage: node tools/chat-report.mjs <folder> [--json]");
    process.exit(2);
  }
  const r = report(read(folder));
  console.log(args.includes("--json") ? JSON.stringify(r, null, 2) : text(r));
}
