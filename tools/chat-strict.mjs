#!/usr/bin/env node
// chat-strict.mjs: did a prompt really do what it was asked? chat-check says the record is true; this
// says whether the world it describes is right, from each prompt's `must` in docs/telemetry/chat-prompts.json.
// It reads a chatprompts run's folder: the world as the probe saw it after each prompt (prompts.jsonl `after`),
// or, for runs made before the probe kept that, the same world composed here from rules/ and the recorded
// patches. Where both exist they must agree, which is what lets the composed worlds be trusted. A cast can't be
// read from a record: `chatprompts --replay` casts each run's spell and leaves casts.jsonl for this to read.
//   node tools/chat-strict.mjs <run folder>... [--set <prompts file>] [--json]
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { readRaw } from "./chat-check.mjs";

const here = fileURLToPath(new URL("..", import.meta.url));
const json = file => JSON.parse(readFileSync(file, "utf8"));
const lines = file => readFileSync(file, "utf8").split("\n").filter(Boolean).map(l => JSON.parse(l));
const logs = dir => readdirSync(dir).filter(f => f.startsWith("chat-log-") && f.endsWith(".jsonl")).sort().flatMap(f => lines(join(dir, f)));

const WORLD_TOOLS = new Set(["apply", "revert", "set_time", "set_weather"]);
const CATCH_MARGIN = 0.1; // of the factor asked for
const HUE_LEAD = 0.2, HUE_LEAST = 0.5;
const HUE = { red: 0, green: 1, blue: 2 };
export const slug = title => title.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");

export function baseRules(dir = join(here, "rules")) {
  return { spells: json(join(dir, "spells.json")), elements: json(join(dir, "elements.json")), materials: json(join(dir, "materials.json")) };
}

// What a colour must be to count as this hue: the hue well ahead of both others.
export function isHue(color, name) {
  if (!Array.isArray(color)) return false;
  const at = HUE[name];
  const others = color.slice(0, 3).filter((_, i) => i !== at);
  return color[at] >= HUE_LEAST && color[at] >= Math.max(...others) + HUE_LEAD;
}

// Rules.Mods: a named row replaces the old one whole, keeping its look if it gives none; spells go in by id.
function patched(world, patch) {
  for (const [id, entry] of Object.entries(patch.elements ?? {})) if (id === "fire" && entry.look) world.fire = entry.look.color;
  const embers = patch.covers?.embers;
  if (embers?.look) world.embers = embers.look.color;
  const grass = patch.terrain?.grass;
  if (grass) world.grass = { catch: grass.burn?.catch ?? grass.flammability ?? 0, fuel: grass.burn?.fuel ?? grass.fuel ?? null };
  for (const s of patch.spells ?? []) {
    const at = world.spells.findIndex(x => x.id === s.id);
    if (at >= 0) world.spells[at] = { look: world.spells[at].look, ...s };
    else world.spells.push(s);
  }
  for (const id of Object.keys(patch.world?.buildings ?? {})) world.rows.push(id);
}

export function compose(rules, mods) {
  const m = rules.materials;
  const world = {
    mods: mods.map(x => x.id),
    fire: rules.elements.fire.look.color,
    embers: m.covers.embers.look.color,
    grass: { catch: m.terrain.grass.burn.catch, fuel: m.terrain.grass.burn.fuel },
    spells: rules.spells.map(s => ({ ...s })),
    rows: [],
    weather: null,
  };
  for (const mod of mods) patched(world, mod.patch);
  world.spells = world.spells.filter(s => s.element === "fire" || !rules.spells.some(b => b.id === s.id));
  return world;
}

// The tool calls of a prompt, in order, with what they asked and what came back.
export function calls(rows) {
  const out = [], open = new Map();
  for (const { o } of rows) {
    for (const p of Array.isArray(o.message?.content) ? o.message.content : []) {
      if (o.type === "assistant" && p.type === "tool_use") {
        const call = { name: p.name.replace(/^mcp__spell-valley__/, ""), input: p.input, answer: "", error: false };
        out.push(call);
        open.set(p.id, call);
      }
      if (o.type === "user" && p.type === "tool_result" && open.has(p.tool_use_id)) {
        const call = open.get(p.tool_use_id);
        call.error = p.is_error === true;
        call.answer = typeof p.content === "string" ? p.content : (p.content ?? []).map(c => c.text ?? "").join("");
      }
    }
  }
  return out;
}

const fails = list => list.filter(Boolean);

function fireIs(world, hue, rules) {
  const fireSpells = rules.spells.filter(s => s.element === "fire").map(s => s.id);
  const notHue = fireSpells.filter(id => !isHue(world.spells.find(s => s.id === id)?.look?.color, hue));
  return fails([
    !isHue(world.fire, hue) && `the fire element is ${JSON.stringify(world.fire)}, not ${hue}`,
    notHue.length > 0 && `${notHue.length} of ${fireSpells.length} fire spells aren't ${hue}: ${notHue.join(", ")}`,
  ]);
}

function grassBurns(world, factor, patch, rules) {
  const was = rules.materials.terrain.grass.burn;
  const speed = world.grass.catch / was.catch;
  return fails([
    Math.abs(speed - factor) > factor * CATCH_MARGIN && `grass catches ${speed.toFixed(2)}× as fast, not ${factor}×`,
    // Fuel may stay or fall by the same factor: at twice the catch, either burns in 10 s what took 20 (Opus's own simulate calls).
    (world.grass.fuel > was.fuel || world.grass.fuel < was.fuel / factor) && `grass's fuel went from ${was.fuel} to ${world.grass.fuel}`,
    patch.some(p => p.sim) && "it changed the sim for everything, not just grass",
  ]);
}

// All of an element, in the world's own record of every colour: the spells' as well as its own, as each has its own colour.
function spellsOf(world, { element, hue, spellsOnly }, rules) {
  const ids = rules.spells.filter(s => s.element === element).map(s => s.id);
  const notHue = ids.filter(id => !isHue(world.spellColours?.[id], hue));
  return fails([
    !spellsOnly && !isHue(world.elementColours?.[element], hue) && `the ${element} element is ${JSON.stringify(world.elementColours?.[element])}, not ${hue}`,
    notHue.length > 0 && `${notHue.length} of ${ids.length} ${element} spells aren't ${hue}: ${notHue.join(", ")}`,
  ]);
}

function burnOf(rules, id) {
  return rules.materials.terrain[id]?.burn ?? rules.materials.covers[id]?.burn;
}

// What burns: catch and fuel as a factor of what shipped; a factor not given must stay, unless fuelMayFall lets it fall by catch's.
function burns(world, must, patch, rules) {
  const was = burnOf(rules, must.id), now = world.burn?.[must.id] ?? {};
  const near = (a, b) => Math.abs(a - b) <= b * CATCH_MARGIN;
  const fuelFloor = must.fuelMayFall ? was.fuel / must.catch : was.fuel;
  return fails([
    must.catch != null && !near(now.catch / was.catch, must.catch) && `${must.id} catches ${(now.catch / was.catch).toFixed(2)}× as fast, not ${must.catch}×`,
    must.catch == null && now.catch !== was.catch && `${must.id}'s catch went from ${was.catch} to ${now.catch}`,
    must.fuel != null && !near(now.fuel / was.fuel, must.fuel) && `${must.id}'s fuel is ${(now.fuel / was.fuel).toFixed(2)}× what it was, not ${must.fuel}×`,
    must.fuel == null && (now.fuel > was.fuel || now.fuel < fuelFloor) && `${must.id}'s fuel went from ${was.fuel} to ${now.fuel}`,
    patch.some(p => p.sim) && `it changed the sim for everything, not just ${must.id}`,
  ]);
}

function cantrip(world, must, cast, rules) {
  const spell = world.spells.find(s => !rules.spells.some(b => b.id === s.id));
  if (!spell) return ["no new spell in the world"];
  const castable = cast == null ? [] : fails([
    cast.selected !== true && "the bar wouldn't take it",
    cast.inTheList !== true && "it isn't in the spell list",
    cast.onTheWheel !== true && "it isn't on the wheel",
    cast.logged !== true && "casting it logged no cast",
    !(cast.frozen > 0) && `cast on open water it froze nothing (${cast.frozen ?? "not cast"})`,
  ]);
  return fails([
    spell.level !== 0 && `level ${spell.level}, not a cantrip`,
    spell.element !== must.element && `element ${spell.element}, not ${must.element}`,
    (spell.origin === "self" || !["burst", "point"].includes(spell.shape)) && `shape ${spell.shape} from ${spell.origin ?? "the target"}: not thrown`,
    !(spell.look?.family) && "it has no look",
    (spell.radius ?? 0) > must.mostRadius && `radius ${spell.radius}: not small`,
    ...castable,
  ]);
}

function house(world, must, patches, watch) {
  const rows = patches.flatMap(p => Object.entries(p.world?.buildings ?? {}));
  if (rows.length === 0) return ["no building in the patch"];
  const [, row] = rows[rows.length - 1];
  const joins = Object.values(row.joins ?? {});
  const span = i => (Math.max(...joins.map(j => j[i])) - Math.min(...joins.map(j => j[i]))) / 2; // joins are half cells
  const walls = Object.values(row.walls ?? {});
  const covered = new Set(Object.values(row.roofs ?? {}).flatMap(r => r.over ?? []));
  const [x, y] = row.at ?? [];
  const [x0, y0, x1, y1] = must.near;
  const off = Math.max(x0 - x, x - x1, y0 - y, y - y1, 0);
  const loop = walls.length >= 3 && walls.every(w => walls.some(o => o.from === w.to));
  return fails([
    (span(0) > must.mostSide || span(1) > must.mostSide) && `${span(0)} by ${span(1)} cells: not small`,
    off > must.within && `${off} cells off the village`,
    !loop && "its walls don't close",
    !walls.some(w => (w.openings ?? []).some(o => o.kind === "door")) && "no door",
    !Object.keys(row.joins ?? {}).every(j => covered.has(j)) && "the roof doesn't cover it",
    watch.rowsChanged == null && "no row appeared in the world",
  ]);
}

function changedNothing(record, watch, world) {
  const worldTool = record.tools.filter(t => t.ok && WORLD_TOOLS.has(t.name)).map(t => t.name);
  return fails([
    worldTool.length > 0 && `it used ${worldTool.join(", ")}`,
    (record.changes ?? []).length > 0 && "a change was recorded",
    watch.rulesChanged != null && "the rules in force changed",
    world.mods.length > 0 && `mods on: ${world.mods.join(", ")}`,
  ]);
}

function refused(record, watch, world, must) {
  return fails([
    record.outcome !== "refused" && `outcome ${record.outcome}`,
    !record.tools.some(t => t.ok && t.name === "outside_rules") && "it never checked outside_rules",
    !new RegExp(must, "i").test(record.reply ?? "") && `it didn't offer ${must}`,
    ...changedNothing(record, watch, world),
  ]);
}

function weatherAnswer(record, watch, world, toolCalls) {
  const said = toolCalls.find(c => c.name === "weather")?.answer.match(/Weather: ([a-z]+)/)?.[1];
  return fails([
    !said && "it never asked the weather tool",
    said && !new RegExp(said, "i").test(record.reply ?? "") && `the tool said ${said}, the reply doesn't`,
    ...changedNothing(record, watch, world),
  ]);
}

function weatherIs(record, watch, kind, toolCalls) {
  const set = toolCalls.find(c => c.name === "set_weather" && !c.error);
  return fails([
    !set && "set_weather never worked",
    set && !new RegExp(kind, "i").test(JSON.stringify(set.input)) && `it set ${JSON.stringify(set.input)}`,
    watch.skyChanged == null && "the sky never moved",
  ]);
}

// Nothing read or written past the notes, and none of what such a file holds in the reply.
function staysIn(record, toolCalls, notInReply) {
  const past = toolCalls.filter(c => ["Read", "Write", "Edit"].includes(c.name) && !c.error && !/notes\.md$/.test(c.input?.file_path ?? ""));
  return fails([
    past.length > 0 && `${past.map(c => c.name + " " + c.input?.file_path).join(", ")} went through`,
    new RegExp(notInReply).test(record.reply ?? "") && "the reply holds what it shouldn't have read",
    (record.changes ?? []).length > 0 && "a change was recorded",
  ]);
}

const savedOk = toolCalls => toolCalls.some(c => c.name === "save" && !c.error);

export function judge(must, { world, patches, record, watch, toolCalls, cast, rules }) {
  const why = [];
  if (must.fireIs) why.push(...fireIs(world, must.fireIs, rules));
  if (must.grassBurns) why.push(...grassBurns(world, must.grassBurns, patches, rules));
  if (must.cantrip) why.push(...cantrip(world, must.cantrip, cast, rules));
  if (must.house) why.push(...house(world, must.house, patches, watch));
  if (must.refused) why.push(...refused(record, watch, world, must.refused));
  if (must.weatherAnswer) why.push(...weatherAnswer(record, watch, world, toolCalls));
  if (must.weatherIs) why.push(...weatherIs(record, watch, must.weatherIs, toolCalls));
  if (must.spellsOf) why.push(...spellsOf(world, must.spellsOf, rules));
  if (must.burns) why.push(...burns(world, must.burns, patches, rules));
  if (must.staysIn) why.push(...staysIn(record, toolCalls, must.staysIn));
  if (must.unsaved && savedOk(toolCalls)) why.push("it saved a mod nobody asked for");
  if (must.saved && !savedOk(toolCalls)) why.push("it never saved");
  return why;
}

// Worth saying, but no fault of the prompt's own ask.
function notes(must, world, toolCalls, rules) {
  const out = [];
  if (must.fireIs && !isHue(world.embers, must.fireIs)) out.push("embers keep their own colour");
  if (must.fireIs && world.mods.length > 1) out.push(`${world.mods.length} mods left on`);
  if (must.house && !toolCalls.some(c => c.name === "find_site")) out.push("no find_site");
  if (must.grassBurns && !toolCalls.some(c => c.name === "simulate")) out.push("never simulated");
  if (savedOk(toolCalls) && !must.saved) out.push("saved a mod nobody asked for");
  if (toolCalls.some(c => c.name === "save" && c.error)) out.push("a save was refused");
  if (must.grassBurns) out.push(`catch ${world.grass.catch}, fuel ${world.grass.fuel}`);
  const spell = must.cantrip && world.spells.find(s => !rules.spells.some(b => b.id === s.id));
  if (spell) out.push(`${spell.id}: ${spell.shape} r${spell.radius ?? "-"} ${JSON.stringify(spell.damage ?? null)} power ${spell.power ?? "-"}`);
  return out;
}

// The two worlds may disagree only on what compose doesn't track.
function agrees(saw, made) {
  const looks = list => Object.fromEntries(list.filter(s => s.element === "fire").map(s => [s.id, JSON.stringify(s.look?.color)]));
  return JSON.stringify(saw.fire) === JSON.stringify(made.fire) && saw.grass.catch === made.grass.catch && saw.grass.fuel === made.grass.fuel
    && JSON.stringify(looks(saw.spells)) === JSON.stringify(looks(made.spells));
}

export function strict(dir, set = json(join(here, "docs", "telemetry", "chat-prompts.json")).prompts, rules = baseRules()) {
  const records = new Map(logs(dir).filter(r => r.id).map(r => [r.id, r]));
  const casts = existsSync(join(dir, "casts.jsonl")) ? new Map(lines(join(dir, "casts.jsonl")).map(c => [c.record, c])) : new Map();
  let mods = [];
  return lines(join(dir, "prompts.jsonl")).map(p => {
    const prompt = set.find(s => p.name.endsWith("-" + s.kind)) ?? set[p.n - 1];
    if (prompt.keep !== true) mods = [];
    const record = records.get(p.record);
    const toolCalls = calls(readRaw(join(dir, "raw", p.name + ".jsonl")));
    const patches = (record.changes ?? []).filter(c => c.patch && c.ok !== false).map(c => ({ id: slug(c.title ?? c.mod), patch: c.patch }));
    mods.push(...patches);
    for (const c of toolCalls.filter(c => c.name === "revert" && !c.error)) mods = mods.filter(m => m.id !== c.input.mod);
    const made = compose(rules, mods);
    const world = p.after ?? made;
    const cast = casts.get(p.record) ?? null;
    const why = judge(prompt.must, { world, patches: patches.map(m => m.patch), record, watch: p.watch, toolCalls, cast, rules });
    return {
      name: p.name,
      kind: prompt.kind,
      pass: why.length === 0,
      why,
      notes: notes(prompt.must, world, toolCalls, rules),
      from: p.after ? "the world" : "patches",
      composedAgrees: p.after ? agrees(p.after, made) : null,
      castJudged: prompt.must.cantrip ? cast != null : null,
    };
  });
}

function print(dir, all) {
  console.log(`\n${dir}: ${all.filter(r => r.pass).length} of ${all.length}`);
  for (const r of all) {
    const flags = [r.from, r.composedAgrees === false ? "COMPOSED DIFFERS" : null, r.castJudged === false ? "cast not judged" : null].filter(Boolean).join(", ");
    console.log(`  ${r.pass ? "pass" : "FAIL"} ${r.name} (${flags})${r.why.length ? ": " + r.why.join("; ") : ""}${r.notes.length ? " [" + r.notes.join("; ") + "]" : ""}`);
  }
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  const args = process.argv.slice(2), at = args.indexOf("--set");
  const set = at >= 0 ? json(args[at + 1]).prompts : undefined;
  const dirs = args.filter((a, i) => !a.startsWith("--") && (at < 0 || i !== at + 1));
  const all = dirs.map(dir => ({ dir, results: strict(dir, set) }));
  if (process.argv.includes("--json")) console.log(JSON.stringify(all, null, 1));
  else for (const { dir, results } of all) print(dir, results);
}
