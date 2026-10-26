// Typed view of ../../rules/*.json. The JSON is the asset: the future C#
// Game.Core loads the same files, so nothing about a rule lives here.
import materials from "../../rules/materials.json" with { type: "json" };
import reactions from "../../rules/reactions.json" with { type: "json" };
import spells from "../../rules/spells.json" with { type: "json" };
import critters from "../../rules/critters.json" with { type: "json" };
import entities from "../../rules/entities.json" with { type: "json" };
import items from "../../rules/items.json" with { type: "json" };
import machines from "../../rules/machines.json" with { type: "json" };
import sim from "../../rules/sim.json" with { type: "json" };
import groups from "../../rules/groups.json" with { type: "json" };

export interface TerrainDef {
  name: string;
  tags: string[];
  flammability?: number;
  fuel?: number;
  burnsTo?: string;
  breaksTo?: string;
  elevation?: number;
  /** Water a spring keeps the square filled to each tick (units; a river), or none. */
  spring?: number;
  burnLog?: string;
  /** What earth shaping leaves of it (a tree topples into deadfall) before shaping the ground, and the log line. */
  uprootsTo?: string;
  uprootLog?: string;
  /** What digging leaves (grass is dug down to dirt); unset keeps the terrain. */
  dugTo?: string;
}
export interface CoverDef {
  name: string;
  tags: string[];
  flammability?: number;
  fuel?: number;
  burnIntensity?: number;
}
export interface GasDef {
  name: string;
  scald?: number;
}

export interface CellCond {
  cover?: string[];
  fire?: boolean | number;
  fireBelow?: number;
  water?: boolean;
  frozen?: boolean;
  gas?: string;
  terrainTag?: string;
  notTerrainTag?: string;
  conductive?: boolean;
}
export interface EntityCond {
  status?: string;
  flammable?: boolean;
}

/** One effect object; exactly one of the verb keys is set. */
export interface Effect {
  ignite?: number | "cover";
  boil?: number;
  melt?: boolean;
  freeze?: boolean;
  extinguish?: boolean;
  condense?: boolean;
  addWater?: number | "power";
  setCover?: string | null;
  spreadCover?: number;
  break?: boolean;
  setTerrain?: string;
  raise?: string;
  conduct?: number;
  fan?: boolean;
  pushGas?: boolean;
  clearGas?: boolean;
  mark?: string;
  gas?: string;
  n?: number;
  status?: Record<string, number>;
  clear?: string;
  damageMul?: number;
}
export interface CellRule {
  id: string;
  element: string;
  if?: CellCond;
  do: Effect[];
  log?: string;
  notable?: boolean;
  stop?: boolean;
}
export interface EntityRule {
  id: string;
  element: string;
  if?: EntityCond;
  do: Effect[];
  log?: string;
  notable?: boolean;
  stop?: boolean;
}

export type Shape = "point" | "line" | "square" | "burst" | "target" | "cone";
export interface SpellDef {
  id: string;
  name: string;
  level: number;
  shape: Shape;
  /** "self": the spell starts at the caster (beams, Thunderwave), not at the clicked square. */
  origin?: "self";
  element?: string;
  /** The Unity spell radial's element it sits under (a groups id). */
  group?: string;
  power?: number;
  damage?: [number, number];
  length?: number;
  size?: number;
  radius?: number;
  push?: number;
  wind?: number;
  effect?: "levitate" | "carry" | "teleport" | "terraform";
  /**
   * terraform: steps to raise or to dig, or a height to raise the cells to (exactly one of
   * the three), the terrain the cells become, and taper (a hill or crater over radius).
   */
  raise?: number;
  dig?: number;
  height?: number;
  terrain?: string;
  taper?: boolean;
  /** The ground it blows out where it lands (Fireball). */
  crater?: CraterDef;
  duration?: number;
  maxMass?: number;
  range?: number;
  blurb: string;
}
export interface CritterDef {
  id: string;
  name: string;
  glyph: string;
  color: string;
  mode: "emit" | "act";
  element: string;
  power?: number;
  every?: number;
  radius?: number;
  length?: number;
  directional?: boolean;
  blurb: string;
}
export interface EntityDef {
  name: string;
  hp: number;
  mass: number;
  flammable: boolean;
  creature?: boolean;
  player?: boolean;
  explodes?: { cover: string; radius: number; fire: number; push: number; damage: [number, number]; crater?: CraterDef };
}
/** Ground a blast digs out: depth steps in the middle, tapering to none past radius (default 1) squares. */
export interface CraterDef {
  radius?: number;
  depth: number;
}
/**
 * A thing to carry (items.json). The Unity game picks these up, throws them and
 * uses them; the sandbox has no items on its board but carries the table through
 * mods and checks it, so both engines accept the same patches (Items.cs).
 */
export interface ItemDef {
  name: string;
  /** Kilograms. */
  mass?: number;
  use?: "light" | "zoom" | "send" | "point";
  /** Feet of bright and then dim light, its colour, and a cone's full angle in degrees (0 or unset: all round). */
  light?: { bright?: number; dim?: number; color?: string; cone?: number };
  zoom?: number;
  /** use "point": how far away its light can go, in feet. */
  reach?: number;
  message?: string;
  /** Breaks when it lands at speed (m/s) or harder, spilling a cover (lit) over a radius. */
  breaks?: { speed: number; spill?: { cover: string; radius?: number; fire?: number } };
  srd?: string;
}
export interface MachineDef {
  name: string;
  needs: "steam" | "mass" | "flow" | "charge" | "wind";
  build?: number;
  max?: number;
  decayEvery?: number;
  mass?: number;
  hold?: number;
  how: string;
}
/**
 * An element of the Unity game's spell radial (groups.json): spells name one
 * with "group". UI only; the sandbox carries and checks it, so both engines
 * accept the same patches, but has no radial.
 */
export interface GroupDef {
  name: string;
  /** Lowest first, clockwise from the top; ties by id. */
  order?: number;
  /** "#rrggbb". */
  color?: string;
}
export interface SimParams {
  ticksPerSecond: number;
  fire: { spreadChance: number; heatChance: number; boilChance: number; downwindBonus: number; smokeChance: number };
  water: { maxLevel: number; unitsPerStep: number };
  /** Range of Cell.h in steps; from blockAt up a cell is a wall. */
  height: { min: number; max: number; blockAt: number };
  gas: { decayChance: number; maxAmount: number };
  status: { burnDamageEvery: number; scaldEvery: number };
  push: { slideCells: number; collisionDamage: number };
  fallDamage: number;
  zapDamage: number;
  conductLimit: number;
  /** How long a day lasts in real minutes, and the hour the world starts at (sky.ts). */
  day: { lengthMinutes: number; startHour: number };
  /** The weather (sky.ts picks it, tickWeather in sim.ts applies it). */
  weather: {
    periodMinutes: number;
    rampMinutes: number;
    calmPeriods: number;
    weights: Partial<Record<"clear" | "cloudy" | "windy" | "fog" | "rain" | "storm", number>>;
    rain: { douseChance: number; dampen: number; fillEvery: number; fillChance: number; dryEvery: number; dryChance: number };
    storm: { strikeEvery: number; strikeChance: number };
    /** A clear stretch's chance of a meteor shower; in one, every `every` ticks the chance a meteor appears, landing fallTicks later. */
    meteors: { showerChance: number; every: number; chance: number; fallTicks: number };
  };
}

export interface Rules {
  /** Every element name a rule, spell or critter may use. Mods can add more. */
  elements: string[];
  terrain: Record<string, TerrainDef>;
  covers: Record<string, CoverDef>;
  gases: Record<string, GasDef>;
  cellRules: CellRule[];
  entityRules: EntityRule[];
  spells: SpellDef[];
  critters: CritterDef[];
  entities: Record<string, EntityDef>;
  items: Record<string, ItemDef>;
  machines: Record<string, MachineDef>;
  sim: SimParams;
  groups: Record<string, GroupDef>;
}

export const BASE_ELEMENTS = ["fire", "cold", "water", "air", "earth", "lightning", "grease", "oil", "thunder"];

const clone = <T>(v: T): T => JSON.parse(JSON.stringify(v)) as T;

/** The rule tables exactly as committed in ../../rules/. Never mutated. */
export const BASE_RULES: Readonly<Rules> = Object.freeze({
  elements: BASE_ELEMENTS,
  terrain: materials.terrain as Record<string, TerrainDef>,
  covers: materials.covers as Record<string, CoverDef>,
  gases: materials.gases as Record<string, GasDef>,
  cellRules: reactions.cell as CellRule[],
  entityRules: reactions.entity as EntityRule[],
  spells: spells as SpellDef[],
  critters: critters as CritterDef[],
  entities: entities as unknown as Record<string, EntityDef>,
  items: items as Record<string, ItemDef>,
  machines: machines as Record<string, MachineDef>,
  sim: sim as SimParams,
  groups: groups as Record<string, GroupDef>,
});

/**
 * The rules in force right now: the base tables plus whatever mods are on.
 * Everything reads through this object; installRules swaps its contents.
 */
export const RULES: Rules = clone(BASE_RULES as Rules);

/**
 * A mod: a data-only patch over the rule tables. Anything it names by id is
 * added, or replaces the existing entry with that id. See docs/MODDING.md.
 */
export interface ModPatch {
  elements?: string[];
  terrain?: Record<string, TerrainDef>;
  covers?: Record<string, CoverDef>;
  gases?: Record<string, GasDef>;
  entities?: Record<string, EntityDef>;
  items?: Record<string, ItemDef>;
  machines?: Record<string, MachineDef>;
  groups?: Record<string, GroupDef>;
  /** New or replaced reaction rules. New ones go to the top of their element's list, so they win. */
  reactions?: { add?: CellRule[]; addEntity?: EntityRule[]; remove?: string[] };
  spells?: SpellDef[];
  critters?: CritterDef[];
  /** Deep-merged over sim.json. */
  sim?: Record<string, unknown>;
}

function mergeDeep(into: Record<string, unknown>, from: Record<string, unknown>) {
  for (const [k, v] of Object.entries(from)) {
    const cur = into[k];
    if (v && typeof v === "object" && !Array.isArray(v) && cur && typeof cur === "object" && !Array.isArray(cur)) {
      mergeDeep(cur as Record<string, unknown>, v as Record<string, unknown>);
    } else {
      into[k] = v;
    }
  }
}

function upsertRules<T extends { id: string; element: string }>(list: T[], add: T[]): T[] {
  const out = [...list];
  for (const rule of add) {
    const i = out.findIndex((r) => r.id === rule.id);
    if (i >= 0) {
      out[i] = rule;
      continue;
    }
    const first = out.findIndex((r) => r.element === rule.element);
    out.splice(first >= 0 ? first : out.length, 0, rule);
  }
  return out;
}

function upsertById<T extends { id: string }>(list: T[], add: T[]): T[] {
  const out = [...list];
  for (const item of add) {
    const i = out.findIndex((x) => x.id === item.id);
    if (i >= 0) out[i] = item;
    else out.push(item);
  }
  return out;
}

/** Base tables with each patch applied in order. Pure: never touches RULES. */
export function composeRules(patches: ModPatch[], base: Readonly<Rules> = BASE_RULES): Rules {
  const r = clone(base as Rules);
  for (const p of patches) {
    if (p.elements) r.elements = [...new Set([...r.elements, ...p.elements])];
    Object.assign(r.terrain, clone(p.terrain ?? {}));
    Object.assign(r.covers, clone(p.covers ?? {}));
    Object.assign(r.gases, clone(p.gases ?? {}));
    Object.assign(r.entities, clone(p.entities ?? {}));
    Object.assign(r.items, clone(p.items ?? {}));
    Object.assign(r.machines, clone(p.machines ?? {}));
    Object.assign(r.groups, clone(p.groups ?? {}));
    if (p.reactions?.remove) {
      const drop = new Set(p.reactions.remove);
      r.cellRules = r.cellRules.filter((x) => !drop.has(x.id));
      r.entityRules = r.entityRules.filter((x) => !drop.has(x.id));
    }
    if (p.reactions?.add) r.cellRules = upsertRules(r.cellRules, clone(p.reactions.add));
    if (p.reactions?.addEntity) r.entityRules = upsertRules(r.entityRules, clone(p.reactions.addEntity));
    if (p.spells) r.spells = upsertById(r.spells, clone(p.spells));
    if (p.critters) r.critters = upsertById(r.critters, clone(p.critters));
    if (p.sim) mergeDeep(r.sim as unknown as Record<string, unknown>, clone(p.sim));
  }
  return r;
}

/** Swaps the live rules. Callers validate first. */
export function installRules(r: Rules) {
  Object.assign(RULES, clone(r));
}

/** Kept for older imports: the base element list. */
export const ELEMENTS = BASE_ELEMENTS;

const COND_KEYS = new Set(["cover", "fire", "fireBelow", "water", "frozen", "gas", "terrainTag", "notTerrainTag", "conductive", "status", "flammable"]);
const EFFECT_KEYS = new Set([
  "ignite", "boil", "melt", "freeze", "extinguish", "condense", "addWater", "setCover", "spreadCover", "break",
  "setTerrain", "raise", "conduct", "fan", "pushGas", "clearGas", "mark", "gas", "n", "status", "clear", "damageMul",
]);
const SHAPES = new Set(["point", "line", "square", "burst", "target", "cone"]);
const SPELL_EFFECTS = new Set(["levitate", "carry", "teleport", "terraform"]);

function terraformErrors(r: Rules, s: SpellDef): string[] {
  const errors: string[] = [];
  const { min, max } = r.sim.height;
  const given = [s.raise, s.dig, s.height].filter((n) => n !== undefined).length;
  if (given !== 1) errors.push(`spell ${s.id}: terraform needs exactly one of raise, dig or height`);
  for (const key of ["raise", "dig"] as const) {
    const n = s[key];
    if (n !== undefined && (!Number.isInteger(n) || n < 1)) errors.push(`spell ${s.id}: ${key} must be a whole number of steps from 1`);
  }
  if (s.height !== undefined && (!Number.isInteger(s.height) || s.height < min || s.height > max)) {
    errors.push(`spell ${s.id}: height must be a whole number from ${min} to ${max} (sim.height)`);
  }
  if (s.terrain !== undefined && !Object.keys(r.terrain).includes(s.terrain)) errors.push(`spell ${s.id}: terrain ${s.terrain} unknown`);
  else if (s.terrain !== undefined && r.terrain[s.terrain]!.tags.includes("blocking")) {
    errors.push(`spell ${s.id}: terrain ${s.terrain} is blocking (walls come from raise)`);
  }
  if (s.taper !== undefined && typeof s.taper !== "boolean") errors.push(`spell ${s.id}: taper must be true or false`);
  return errors;
}

/** No crater, or one with a whole depth from 1 and (if given) a whole radius from 0. */
function isCrater(c: CraterDef | undefined): boolean {
  if (c === undefined) return true;
  return typeof c === "object" && c !== null && Number.isInteger(c.depth) && c.depth >= 1
    && (c.radius === undefined || (Number.isInteger(c.radius) && c.radius >= 0));
}

const ITEM_USES = new Set(["light", "zoom", "send", "point"]);
const isNumber = (v: unknown): v is number => typeof v === "number";
const absent = (v: unknown) => v === undefined || v === null;

/** A number from min (or above it), up to max, when present. Same wording as Rules.CheckItems.cs. */
function rangeError(v: unknown, what: string, min: number, opts: { above?: boolean; max?: number } = {}): string[] {
  if (absent(v)) return [];
  const n = isNumber(v) ? v : NaN;
  if (n >= min && (!opts.above || n > min) && n <= (opts.max ?? Infinity)) return [];
  const range = opts.above ? `above ${min}` : opts.max !== undefined ? `from ${min} to ${opts.max}` : `from ${min}`;
  return [`${what} must be a number ${range}`];
}

/** An optional sub-object; anything else there is an error. */
function section(errors: string[], parent: Record<string, unknown>, key: string, what: string): Record<string, unknown> | undefined {
  const v = parent[key];
  if (absent(v)) return undefined;
  if (typeof v === "object" && !Array.isArray(v)) return v as Record<string, unknown>;
  errors.push(`${what}: ${key} must be an object`);
  return undefined;
}

function itemErrors(r: Rules, id: string, it: ItemDef): string[] {
  const errors: string[] = [];
  const what = `item ${id}`;
  const i = it as unknown as Record<string, unknown>;
  if (!it.name) errors.push(`${what}: needs a name`);
  if (id in r.entities) errors.push(`${what}: id is taken by an entity`);
  errors.push(...rangeError(i.mass, `${what}: mass`, 0, { above: true }));
  errors.push(...rangeError(i.zoom, `${what}: zoom`, 1));
  errors.push(...rangeError(i.reach, `${what}: reach`, 0));
  const light = section(errors, i, "light", what);
  const breaks = section(errors, i, "breaks", what);
  if (light) {
    errors.push(...rangeError(light.bright, `${what}: light.bright`, 0));
    errors.push(...rangeError(light.dim, `${what}: light.dim`, 0));
    errors.push(...rangeError(light.cone, `${what}: light.cone`, 0, { max: 179 }));
    if (light.color !== undefined && !(typeof light.color === "string" && /^#[0-9a-fA-F]{6}$/.test(light.color))) errors.push(`${what}: light.color must be #rrggbb`);
  }
  if (breaks) {
    if (absent(breaks.speed)) errors.push(`${what}: breaks.speed must be a number above 0`);
    else errors.push(...rangeError(breaks.speed, `${what}: breaks.speed`, 0, { above: true }));
    const spill = section(errors, breaks, "spill", `${what}: breaks`);
    if (spill) {
      if (!(typeof spill.cover === "string" && spill.cover in r.covers)) errors.push(`${what}: breaks.spill.cover ${spill.cover} unknown`);
      errors.push(...rangeError(spill.radius, `${what}: breaks.spill.radius`, 0));
      errors.push(...rangeError(spill.fire, `${what}: breaks.spill.fire`, 0));
    }
  }
  if (it.use === undefined) return errors;
  if (!ITEM_USES.has(it.use)) errors.push(`${what}: unknown use ${it.use}`);
  else if (it.use === "light" && !light) errors.push(`${what}: use light needs a light`);
  else if (it.use === "zoom" && !(isNumber(i.zoom) && i.zoom > 1)) errors.push(`${what}: use zoom needs a zoom above 1`);
  else if (it.use === "send" && !it.message) errors.push(`${what}: use send needs a message`);
  else if (it.use === "point") {
    if (!light) errors.push(`${what}: use point needs a light`);
    if (!(isNumber(i.reach) && i.reach > 0)) errors.push(`${what}: use point needs a reach above 0`);
  }
  return errors;
}

/** The spell groups and the spells' groups. Same wording as SpellGroups.cs. */
function groupErrors(r: Rules): string[] {
  const errors: string[] = [];
  for (const [id, g] of Object.entries(r.groups)) {
    if (!g || typeof g !== "object" || Array.isArray(g)) { errors.push(`group ${id}: must be an object`); continue; }
    if (!g.name) errors.push(`group ${id}: needs a name`);
    if (!absent(g.order) && !isNumber(g.order)) errors.push(`group ${id}: order must be a number`);
    if (g.color !== undefined && !(typeof g.color === "string" && /^#[0-9a-fA-F]{6}$/.test(g.color))) errors.push(`group ${id}: color must be #rrggbb`);
  }
  for (const s of r.spells) {
    if (s.group !== undefined && !(typeof s.group === "string" && Object.hasOwn(r.groups, s.group))) errors.push(`spell ${s.id}: unknown group ${s.group}`);
  }
  return errors;
}

/** Cross-checks the tables against each other. Empty means consistent. */
export function validateRules(r: Rules): string[] {
  const errors: string[] = [];
  const el = new Set<string>(r.elements);
  const terrainIds = new Set(Object.keys(r.terrain));
  const coverIds = new Set(Object.keys(r.covers));
  for (const [id, t] of Object.entries(r.terrain)) {
    if (t.burnsTo && !terrainIds.has(t.burnsTo)) errors.push(`terrain ${id}: burnsTo ${t.burnsTo} unknown`);
    if (t.breaksTo && !terrainIds.has(t.breaksTo)) errors.push(`terrain ${id}: breaksTo ${t.breaksTo} unknown`);
    if (t.dugTo && !terrainIds.has(t.dugTo)) errors.push(`terrain ${id}: dugTo ${t.dugTo} unknown`);
    if (t.uprootsTo && !terrainIds.has(t.uprootsTo)) errors.push(`terrain ${id}: uprootsTo ${t.uprootsTo} unknown`);
    else if (t.uprootsTo && r.terrain[t.uprootsTo]!.tags.includes("blocking")) errors.push(`terrain ${id}: uprootsTo ${t.uprootsTo} is blocking`);
    if (t.tags.includes("flammable") && !(t.fuel && t.flammability)) errors.push(`terrain ${id}: flammable without fuel/flammability`);
  }
  const seen = new Set<string>();
  for (const rule of [...r.cellRules, ...r.entityRules]) {
    if (!rule || typeof rule.id !== "string") { errors.push("a reaction rule has no id"); continue; }
    if (seen.has(rule.id)) errors.push(`rule ${rule.id}: duplicate id`);
    seen.add(rule.id);
    if (!el.has(rule.element)) errors.push(`rule ${rule.id}: unknown element ${rule.element}`);
    if (!Array.isArray(rule.do)) { errors.push(`rule ${rule.id}: "do" must be a list`); continue; }
    for (const k of Object.keys(rule.if ?? {})) if (!COND_KEYS.has(k)) errors.push(`rule ${rule.id}: unknown condition "${k}"`);
    for (const e of rule.do) for (const k of Object.keys(e)) if (!EFFECT_KEYS.has(k)) errors.push(`rule ${rule.id}: unknown effect "${k}" (new verbs need code; see docs/MODDING.md)`);
    for (const e of rule.do) {
      if (e.setTerrain && !terrainIds.has(e.setTerrain)) errors.push(`rule ${rule.id}: setTerrain ${e.setTerrain} unknown`);
      if (e.raise && !terrainIds.has(e.raise)) errors.push(`rule ${rule.id}: raise ${e.raise} unknown`);
      if (typeof e.setCover === "string" && !coverIds.has(e.setCover)) errors.push(`rule ${rule.id}: cover ${e.setCover} unknown`);
      if (e.gas && !(e.gas in r.gases)) errors.push(`rule ${rule.id}: gas ${e.gas} unknown`);
    }
    const c = (rule as CellRule).if;
    for (const cv of c?.cover ?? []) if (!coverIds.has(cv)) errors.push(`rule ${rule.id}: cover ${cv} unknown`);
  }
  for (const s of r.spells) {
    if (s.element && !el.has(s.element)) errors.push(`spell ${s.id}: unknown element ${s.element}`);
    if (!SHAPES.has(s.shape)) errors.push(`spell ${s.id}: unknown shape ${s.shape}`);
    if (!s.name) errors.push(`spell ${s.id}: needs a name`);
    if (s.effect !== undefined && !SPELL_EFFECTS.has(s.effect)) errors.push(`spell ${s.id}: unknown effect ${s.effect}`);
    if (s.effect === "terraform") errors.push(...terraformErrors(r, s));
    if (!isCrater(s.crater)) errors.push(`spell ${s.id}: crater needs a whole depth from 1 and radius from 0`);
  }
  for (const c of r.critters) if (!el.has(c.element)) errors.push(`critter ${c.id}: unknown element ${c.element}`);
  for (const id of Object.keys(r.machines)) {
    if (!terrainIds.has(id)) errors.push(`machine ${id}: no terrain with that id`);
    else if (!r.terrain[id]!.tags.includes("machine")) errors.push(`machine ${id}: terrain lacks the machine tag`);
  }
  for (const [id, e] of Object.entries(r.entities)) {
    if (e.explodes && !coverIds.has(e.explodes.cover)) errors.push(`entity ${id}: explodes into unknown cover`);
    if (e.explodes && !isCrater(e.explodes.crater)) errors.push(`entity ${id}: crater needs a whole depth from 1 and radius from 0`);
  }
  for (const [id, it] of Object.entries(r.items)) errors.push(...itemErrors(r, id, it));
  errors.push(...groupErrors(r));
  return errors;
}
