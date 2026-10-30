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
import components from "../../rules/components.json" with { type: "json" };
import { materialGroupErrors, ungroupMaterials } from "./materials.ts";

export interface TerrainDef {
  name: string;
  tags: string[];
  flammability?: number;
  /** How hot it burns (1 when absent): a burning cell of it is this many times as likely to light its neighbours. */
  heat?: number;
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
  /** How hard it is to dig (1 when absent): a dig needs at least this strength. */
  hardness?: number;
  /** What it grows into once its cell's life runs out (green shoots into grass, a sapling into a tree), and the line logged. */
  growsTo?: string;
  growLog?: string;
  /** Thin water on it soaks in and dries up a unit every this many ticks (soak in sim.ts), on dry ground and on damp; unset never. */
  soakEvery?: number;
  soakEveryWet?: number;
  /** How much of its catch (flammability) it loses while damp (soak.dampens, 0 to 1, 0 when absent): a damp cell of it (raining, or dampUntil) or a wet thing of it catches with flammability * (1 - dampens). */
  dampens?: number;
  /** Wet (water on it, raining, or damp) this many ticks in all, it rusts through into rustsTo (rust.every, rust.leaves; rust in sim.ts); unset never. */
  rustEvery?: number;
  rustsTo?: string;
  /** What a cell of it leaves when it shatters (shatter.leaves: a window pane to an open gap; shatter in react.ts). */
  shattersTo?: string;
  /** Fire reaching it leaves it hot this many ticks (hot.for; cool in sim.ts), and each unit of water it boils off takes hot.quench of them; unset never hot. */
  hotFor?: number;
  hotQuench?: number;
  /** What water soaking into it turns it into (dirt to mud: soak.leaves) and what it dries back to once dry again (mud to dirt: soak.dries, dryOut in sim.ts). */
  soaksTo?: string;
  /** The ticks of burning, frozen and wet a creature of it takes (burn.status, freeze.status, soak.status: the entity rules' statusOf); unset none. */
  burnStatus?: number;
  freezeStatus?: number;
  soakStatus?: number;
  /** A wet creature of it is shocked by charged water next to it, not only under it (conduct.chainsWet; tickEntities). */
  chainsWet?: boolean;
  driesTo?: string;
  /** How fast a creature walks across it (mud 0.5: a step takes 1/speed as long, thorns in entities.ts); 1 when unset. */
  speed?: number;
  /** The element that dissolves it (materials.json dissolve.by, acid) and the terrain it leaves. */
  dissolvedBy?: string;
  dissolvesTo?: string;
  /** What water on it turns into, any depth: ice (arctic ground) or steam (lava). */
  waterTo?: "ice" | "steam";
  /** How its fire looks (flare, wood, oil); the view's only. */
  fireLook?: string;
}

/** The fire looks a material can name (fireLook); Rules.Check.cs's FireLooks. */
export const FIRE_LOOKS = ["flare", "wood", "oil"];
export interface CoverDef {
  name: string;
  tags: string[];
  /** How its fire looks (flare, wood, oil); the view's only. */
  fireLook?: string;
  flammability?: number;
  fuel?: number;
  burnIntensity?: number;
  /** Ticks it lasts once laid (Cell.life) before it withers away (thorns), and the line logged then. */
  lasts?: number;
  witherLog?: string;
  /** What it does to a creature walking through: [dice, sides] damage a square, and its speed (1 unhindered). */
  damage?: [number, number];
  speed?: number;
  /** The element that dissolves it (materials.json dissolve.by, acid) and the cover it leaves (unset, none). */
  dissolvedBy?: string;
  dissolvesTo?: string;
  /** On open water it spreads to the open water next to it every this many ticks (float.every; float in sim.ts); unset sinks or stays. */
  floatEvery?: number;
}
export interface GasDef {
  name: string;
  scald?: number;
  /** Its own chance per tick a unit decays (fog lingers); unset uses sim.gas.decayChance. */
  decay?: number;
  /** From how many units a square of it hides what's behind (lineOfSight); unset never does. */
  blocksSightAt?: number;
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
  /** The least power the element must arrive with (a charged cast's). */
  power?: number;
  /** The chance the rule applies, rolled last, once everything else holds. */
  chance?: number;
}
export interface EntityCond {
  status?: string;
  /** Components it must have, and must lack (a name or a list; components.json). */
  has?: string | string[];
  lacks?: string | string[];
  /** Old shorthand: true is has flammable, false is lacks flammable. */
  flammable?: boolean;
  /** Its material gives a status by this verb (burn, freeze, soak: burn.status and so on). */
  verb?: string;
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
  /** A terrain that gets hot (hot.for) is left hot for that long (cool in sim.ts). */
  heat?: boolean;
  /** Eats away the cell's cover or terrain into its dissolvesTo, when the rule's element is its dissolvedBy. */
  dissolve?: "cover" | "terrain";
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
  /** The status a verb gives (burn burning, freeze frozen, soak wet), for its material's ticks of it; none without. */
  statusOf?: string;
  clear?: string;
  damageMul?: number;
  /** The terrain the cell becomes, its life a roll within ticks [min, max] plus wave ticks a square from where the spell landed. */
  grow?: string;
  ticks?: [number, number];
  wave?: number;
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
  /** Squares a burst's radius grows by for each level of charge past a tap. */
  radiusPerCharge?: number;
  push?: number;
  wind?: number;
  effect?: "levitate" | "carry" | "teleport" | "terraform" | "haste" | "weather" | "shapeshift";
  /** haste (Hasten Time): how many times as fast the day clock runs. */
  rate?: number;
  /**
   * terraform: steps to raise or to dig, or a height to raise the cells to (exactly one of
   * the three), the terrain the cells become, and taper (a hill or crater over radius).
   */
  raise?: number;
  dig?: number;
  /** A digging spell's strength (1 when absent): it digs terrain no harder than this. */
  strength?: number;
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
  /** [from, to]: the hours it is out (across midnight when from is the later); it does nothing outside them. Always out without. */
  hours?: [number, number];
  blurb: string;
}
export interface EntityDef {
  name: string;
  hp: number;
  mass: number;
  /** What it's made of: a terrain id (a crate is wood). */
  material?: string;
  /** What it is made of or does (components.json); read through hasComponent. */
  components?: string[];
  /** Old shorthand for the flammable and creature components. */
  flammable?: boolean;
  creature?: boolean;
  player?: boolean;
  explodes?: { cover: string; radius: number; fire: number; push: number; damage: [number, number]; crater?: CraterDef };
  /** It raids the defense field (defense.ts): ticks a square takes, and elements it takes double or half (rounded up) damage from. */
  raider?: RaiderDef;
}
export interface RaiderDef {
  ticksPerSquare?: number;
  weak?: string[];
  resist?: string[];
}
/** sim.json's defense (defense.ts; C#: DefenseParams in Defense.cs). */
export interface DefenseParams {
  lives: number; waves: number; first: number; growth: number; hpPerWave: number;
  warmup: number; spawnEvery: number; betweenWaves: number;
  margin: number; wallCost: number; roughCost: number; roughTicks: number; batterTicks: number; deepWater: number;
  kinds: string[];
}
/** Ground a blast digs out: depth steps in the middle, tapering to none past radius (default 1) squares. */
export interface CraterDef {
  radius?: number;
  depth: number;
  /** How hard a ground the blast can dig (1 when absent). */
  strength?: number;
}
/**
 * A thing to carry (items.json). The Unity game picks these up, throws them and
 * uses them; the sandbox has no items on its board but carries the table through
 * mods and checks it, so both engines accept the same patches (Items.cs).
 */
export interface ItemDef {
  name: string;
  /** What it's made of: a terrain id (a lantern is metal). */
  material?: string;
  /** What it is made of or does (components.json). */
  components?: string[];
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
  water: { maxLevel: number; unitsPerStep: number; thin: number; soakPass: number; dampTicks: number };
  /** Range of Cell.h in steps; from blockAt up a cell is a wall. */
  height: { min: number; max: number; blockAt: number };
  gas: { decayChance: number; maxAmount: number };
  /** shatter: a thing with status `when` (frozen) shatters when hurt by any element but `notBy` (cold); World.Sim ShatterWhen, ShatterNotBy. */
  status: { burnDamageEvery: number; scaldEvery: number; shatter?: { when?: string; notBy?: string } };
  push: { slideCells: number; collisionDamage: number };
  fallDamage: number;
  zapDamage: number;
  conductLimit: number;
  defense: DefenseParams;
  /** How long a day lasts in real minutes, and the hour the world starts at (sky.ts). */
  day: {
    lengthMinutes: number; startHour: number;
    /** [from, to] hours: dim light from the first to the second (night.ts, sky.ts daylight). */
    dawn: [number, number]; dusk: [number, number];
    /** How many times as fast snow freezes standing water at night. */
    nightFreeze: number;
    /** The light a burning cell sheds, in feet as items.json lights are. */
    fireLight: { bright: number; dim: number };
  };
  /** The weather (sky.ts picks it, tickWeather in sim.ts applies it). */
  weather: {
    periodMinutes: number;
    rampMinutes: number;
    calmPeriods: number;
    weights: Partial<Record<"clear" | "cloudy" | "windy" | "fog" | "rain" | "storm" | "snow", number>>;
    rain: { douseChance: number; dampen: number; fillEvery: number; fillChance: number; dryEvery: number; dryChance: number };
    storm: { strikeEvery: number; strikeChance: number };
    /** Every freezeEvery ticks of snow, a shallow square's chance its water freezes; dry spells thaw it at thawChance. */
    /** Also on that beat: open ground's chance of a snow cover, and a sheltered snowy square's chance to drift into a bank. */
    snow: { freezeEvery: number; freezeChance: number; thawChance: number; coverChance: number; bankChance: number };
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
  /** Component names and what each means (components.json; C#: Components.cs). */
  components: Record<string, string>;
}

export const BASE_ELEMENTS = ["fire", "cold", "water", "air", "earth", "lightning", "grease", "oil", "thunder", "fog", "growth", "thorns", "acid"];

const clone = <T>(v: T): T => JSON.parse(JSON.stringify(v)) as T;

// materials.json with any verb groups turned into the flat keys (materials.ts).
const baseMaterials = (() => {
  const m = clone(materials) as unknown as { terrain: Record<string, unknown>; covers: Record<string, unknown>; gases: unknown };
  ungroupMaterials(m);
  return m;
})();

/** The rule tables exactly as committed in ../../rules/ (verb groups read as flat keys). Never mutated. */
export const BASE_RULES: Readonly<Rules> = Object.freeze({
  elements: BASE_ELEMENTS,
  terrain: baseMaterials.terrain as Record<string, TerrainDef>,
  covers: baseMaterials.covers as unknown as Record<string, CoverDef>,
  gases: materials.gases as Record<string, GasDef>,
  cellRules: reactions.cell as CellRule[],
  entityRules: reactions.entity as EntityRule[],
  spells: spells as SpellDef[],
  critters: critters as CritterDef[],
  entities: entities as unknown as Record<string, EntityDef>,
  items: items as Record<string, ItemDef>,
  machines: machines as Record<string, MachineDef>,
  sim: sim as unknown as SimParams,
  groups: groups as Record<string, GroupDef>,
  components: components as Record<string, string>,
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
  components?: Record<string, string>;
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
    Object.assign(r.components, clone(p.components ?? {}));
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
  // A mod may spell a material as verb groups too.
  ungroupMaterials(r as unknown as { terrain: Record<string, unknown>; covers: Record<string, unknown> });
  return r;
}

/** Swaps the live rules. Callers validate first. */
export function installRules(r: Rules) {
  Object.assign(RULES, clone(r));
}

/** Kept for older imports: the base element list. */
export const ELEMENTS = BASE_ELEMENTS;

const COND_KEYS = new Set(["cover", "fire", "fireBelow", "water", "frozen", "gas", "terrainTag", "notTerrainTag", "conductive", "status", "flammable", "has", "lacks", "power", "chance", "verb"]);
const EFFECT_KEYS = new Set([
  "ignite", "boil", "melt", "freeze", "extinguish", "condense", "addWater", "setCover", "spreadCover", "break", "heat", "dissolve",
  "setTerrain", "raise", "conduct", "fan", "pushGas", "clearGas", "mark", "gas", "n", "status", "statusOf", "clear", "damageMul", "grow", "ticks", "wave",
]);
const SHAPES = new Set(["point", "line", "square", "burst", "target", "cone"]);
const SPELL_EFFECTS = new Set(["levitate", "carry", "teleport", "terraform", "haste", "weather", "shapeshift"]);

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
  if (s.strength !== undefined && (!Number.isInteger(s.strength) || s.strength < 1)) errors.push(`spell ${s.id}: strength must be a whole number from 1`);
  return errors;
}

/** No crater, or one with a whole depth from 1 and (if given) a whole radius from 0 and strength from 1. */
function isCrater(c: CraterDef | undefined): boolean {
  if (c === undefined) return true;
  return typeof c === "object" && c !== null && Number.isInteger(c.depth) && c.depth >= 1
    && (c.radius === undefined || (Number.isInteger(c.radius) && c.radius >= 0))
    && (c.strength === undefined || (Number.isInteger(c.strength) && c.strength >= 1));
}

/** A pair of whole numbers, the first from min0 and the second from min1 (dice, a tick range). */
function isWholeRange(v: unknown, min0: number, min1: number): v is [number, number] {
  return Array.isArray(v) && v.length === 2 && Number.isInteger(v[0]) && Number.isInteger(v[1]) && v[0] >= min0 && v[1] >= min1;
}

const ITEM_USES =new Set(["light", "zoom", "send", "point"]);
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
  if (it.material !== undefined && !(it.material in r.terrain)) errors.push(`${what}: material ${it.material} unknown`);
  errors.push(...rangeError(i.mass, `${what}: mass`, 0, { above: true }));
  errors.push(...rangeError(i.zoom, `${what}: zoom`, 1));
  errors.push(...rangeError(i.reach, `${what}: reach`, 0));
  errors.push(...componentListErrors(r, what, i.components));
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

/** A raider's pace and the elements it's weak to or resists (C#: RaiderRefs in Rules.Check.cs). */
function raiderErrors(el: Set<string>, id: string, rd: RaiderDef): string[] {
  const errors: string[] = [];
  const pace = rd.ticksPerSquare;
  if (pace !== undefined && !(Number.isInteger(pace) && pace >= 1)) errors.push(`entity ${id}: raider ticksPerSquare must be a whole number from 1`);
  for (const e of (rd.weak ?? []).filter((e) => typeof e === "string" && !el.has(e))) errors.push(`entity ${id}: raider is weak to unknown element ${e}`);
  for (const e of (rd.resist ?? []).filter((e) => typeof e === "string" && !el.has(e))) errors.push(`entity ${id}: raider resists unknown element ${e}`);
  return errors;
}

// ---------- components (C#: Components.cs, Rules.CheckComponents.cs) ----------

/** The components the engine itself reads, there whatever components.json says. */
export const ENGINE_COMPONENTS = ["flammable", "creature"];
/** At most this many components (the C# keeps a thing's as one 64-bit set). */
export const MAX_COMPONENTS = 64;

/** Every component name the rules know, sorted (the C# numbers them in this order). */
export function componentNames(r: Rules = RULES): string[] {
  return [...new Set([...Object.keys(r.components ?? {}), ...ENGINE_COMPONENTS])].sort();
}

/** Whether an entity or item has a component, honouring the old flammable / creature keys. */
/** Each component a material's verb gives a thing, and the verb's tag (Rules.MaterialComponents). */
export const MATERIAL_COMPONENTS: Record<string, string> = { flammable: "flammable", breakable: "breakable", conducts: "conductive", brittle: "brittle" };

export function hasComponent(def: { components?: string[]; flammable?: boolean; creature?: boolean; material?: string }, name: string): boolean {
  if ((def.components ?? []).includes(name) || (name === "flammable" && !!def.flammable) || (name === "creature" && !!def.creature)) return true;
  // What its material makes it (docs/MATERIALS.md, step 4).
  const tag = MATERIAL_COMPONENTS[name];
  const m = def.material !== undefined ? RULES.terrain[def.material] : undefined;
  return tag !== undefined && m !== undefined && m.tags.includes(tag);
}

const namesOf = (v: string | string[] | undefined): string[] => (v === undefined ? [] : typeof v === "string" ? [v] : v);

/** Whether an entity rule's "if" holds for this entity: its status, and the components it has and lacks. */
/** The verbs a material gives a creature a status by, and that status (TerrainDef.StatusOfVerb). */
export const VERB_STATUS: Record<string, string> = { burn: "burning", freeze: "frozen", soak: "wet" };

/** A verb's status ticks on a material: burn, freeze or soak (TerrainDef.StatusTicks); 0 for none. */
export function statusTicks(m: TerrainDef | undefined, verb: string): number {
  if (!m) return 0;
  return (verb === "burn" ? m.burnStatus : verb === "freeze" ? m.freezeStatus : verb === "soak" ? m.soakStatus : 0) ?? 0;
}

export function entityCondHolds(cond: EntityCond, def: EntityDef, status: Record<string, number>): boolean {
  if (cond.status && !(status[cond.status]! > 0)) return false;
  if (cond.verb && statusTicks(def.material ? RULES.terrain[def.material] : undefined, cond.verb) <= 0) return false;
  const has = [...namesOf(cond.has), ...(cond.flammable === true ? ["flammable"] : [])];
  const lacks = [...namesOf(cond.lacks), ...(cond.flammable === false ? ["flammable"] : [])];
  return has.every((n) => hasComponent(def, n)) && !lacks.some((n) => hasComponent(def, n));
}

function componentListErrors(r: Rules, what: string, list: unknown): string[] {
  if (list === undefined) return [];
  if (!(Array.isArray(list) && list.every((n) => typeof n === "string"))) return [`${what}: components must be a list of component names`];
  const known = new Set(componentNames(r));
  return list.filter((n) => !known.has(n)).map((n) => `${what}: unknown component ${n}`);
}

function componentCondErrors(r: Rules, id: string, cond: EntityCond | undefined, key: "has" | "lacks"): string[] {
  const v: unknown = cond?.[key];
  if (v === undefined) return [];
  const names = typeof v === "string" ? [v] : Array.isArray(v) ? v : undefined;
  if (!names || names.some((n) => typeof n !== "string")) return [`rule ${id}: ${key} must be a component name or a list of them`];
  const known = new Set(componentNames(r));
  return names.filter((n) => !known.has(n as string)).map((n) => `rule ${id}: unknown component ${n}`);
}

function componentTableErrors(r: Rules): string[] {
  const errors: string[] = [];
  for (const [name, d] of Object.entries(r.components ?? {})) if (!(typeof d === "string" && d)) errors.push(`component ${name}: needs a description`);
  const n = componentNames(r).length;
  if (n > MAX_COMPONENTS) errors.push(`components: at most ${MAX_COMPONENTS}, not ${n}`);
  return errors;
}

/** [from, to]: two hours of the day, 0 to 24 (sky.ts inHours). */
const isHours = (v: unknown) => Array.isArray(v) && v.length === 2 && v.every((h) => typeof h === "number" && h >= 0 && h <= 24);

/** What dissolves a terrain or cover (an element) and what it leaves (one of its own kind): Rules.Check.cs's Dissolves. */
function dissolveErrors(what: string, t: { dissolvedBy?: string; dissolvesTo?: string }, elements: Set<string>, kind: Set<string>): string[] {
  const errors: string[] = [];
  if (t.dissolvedBy && !elements.has(t.dissolvedBy)) errors.push(`${what}: dissolves by unknown element ${t.dissolvedBy}`);
  if (t.dissolvesTo && !kind.has(t.dissolvesTo)) errors.push(`${what}: dissolvesTo ${t.dissolvesTo} unknown`);
  return errors;
}

/** Cross-checks the tables against each other. Empty means consistent. */
export function validateRules(r: Rules): string[] {
  const errors: string[] = materialGroupErrors(r as unknown as { terrain: Record<string, unknown>; covers: Record<string, unknown> });
  const el = new Set<string>(r.elements);
  const terrainIds = new Set(Object.keys(r.terrain));
  const coverIds = new Set(Object.keys(r.covers));
  for (const [id, t] of Object.entries(r.terrain)) {
    if (t.burnsTo && !terrainIds.has(t.burnsTo)) errors.push(`terrain ${id}: burnsTo ${t.burnsTo} unknown`);
    if (t.breaksTo && !terrainIds.has(t.breaksTo)) errors.push(`terrain ${id}: breaksTo ${t.breaksTo} unknown`);
    if (t.dugTo && !terrainIds.has(t.dugTo)) errors.push(`terrain ${id}: dugTo ${t.dugTo} unknown`);
    if (t.rustsTo && !terrainIds.has(t.rustsTo)) errors.push(`terrain ${id}: rustsTo ${t.rustsTo} unknown`);
    for (const key of ["burnStatus", "freezeStatus", "soakStatus"] as const)
      if (t[key] !== undefined && (!Number.isInteger(t[key]) || t[key]! < 1)) errors.push(`terrain ${id}: ${key} must be a whole number from 1`);
    if (t.chainsWet !== undefined && typeof t.chainsWet !== "boolean") errors.push(`terrain ${id}: conduct.chainsWet must be true or false`);
    for (const key of ["soaksTo", "driesTo", "shattersTo"] as const) if (t[key] && !terrainIds.has(t[key])) errors.push(`terrain ${id}: ${key} ${t[key]} unknown`);
    if (t.hardness !== undefined && (!Number.isInteger(t.hardness) || t.hardness < 1)) errors.push(`terrain ${id}: hardness must be a whole number from 1`);
    for (const key of ["soakEvery", "soakEveryWet", "rustEvery", "hotFor", "hotQuench"] as const)
      if (t[key] !== undefined && (!Number.isInteger(t[key]) || t[key]! < 1)) errors.push(`terrain ${id}: ${key} must be a whole number from 1`);
    if (t.dampens !== undefined && !(typeof t.dampens === "number" && t.dampens >= 0 && t.dampens <= 1)) errors.push(`terrain ${id}: dampens must be a number from 0 to 1`);
    if (t.waterTo !== undefined && t.waterTo !== "ice" && t.waterTo !== "steam") errors.push(`terrain ${id}: waterTo must be ice or steam`);
    if (t.fireLook !== undefined && !FIRE_LOOKS.includes(t.fireLook)) errors.push(`terrain ${id}: fireLook must be flare, wood or oil`);
    if (t.uprootsTo && !terrainIds.has(t.uprootsTo)) errors.push(`terrain ${id}: uprootsTo ${t.uprootsTo} unknown`);
    else if (t.uprootsTo && r.terrain[t.uprootsTo]!.tags.includes("blocking")) errors.push(`terrain ${id}: uprootsTo ${t.uprootsTo} is blocking`);
    if (t.growsTo && !terrainIds.has(t.growsTo)) errors.push(`terrain ${id}: growsTo ${t.growsTo} unknown`);
    errors.push(...dissolveErrors(`terrain ${id}`, t, el, terrainIds));
    if (t.dissolvedBy && !t.dissolvesTo) errors.push(`terrain ${id}: dissolve without leaves`);
    if (t.tags.includes("flammable") && !(t.fuel && t.flammability)) errors.push(`terrain ${id}: flammable without fuel/flammability`);
  }
  for (const [id, cv] of Object.entries(r.covers)) {
    if (cv.fireLook !== undefined && !FIRE_LOOKS.includes(cv.fireLook)) errors.push(`cover ${id}: fireLook must be flare, wood or oil`);
    if (cv.damage !== undefined && !isWholeRange(cv.damage, 1, 1)) errors.push(`cover ${id}: damage must be [dice, sides]`);
    if (cv.floatEvery !== undefined && (!Number.isInteger(cv.floatEvery) || cv.floatEvery < 1)) errors.push(`cover ${id}: floatEvery must be a whole number from 1`);
    errors.push(...dissolveErrors(`cover ${id}`, cv, el, coverIds));
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
      if (e.dissolve !== undefined && e.dissolve !== "cover" && e.dissolve !== "terrain") errors.push(`rule ${rule.id}: dissolve must be cover or terrain`);
      if (e.statusOf !== undefined && !(e.statusOf in VERB_STATUS)) errors.push(`rule ${rule.id}: statusOf must be burn, freeze or soak`);
      if (e.raise && !terrainIds.has(e.raise)) errors.push(`rule ${rule.id}: raise ${e.raise} unknown`);
      if (typeof e.setCover === "string" && !coverIds.has(e.setCover)) errors.push(`rule ${rule.id}: cover ${e.setCover} unknown`);
      if (e.gas && !(e.gas in r.gases)) errors.push(`rule ${rule.id}: gas ${e.gas} unknown`);
      if (e.grow && !terrainIds.has(e.grow)) errors.push(`rule ${rule.id}: grow ${e.grow} unknown`);
      else if (e.grow && r.terrain[e.grow]!.tags.includes("blocking")) errors.push(`rule ${rule.id}: grow ${e.grow} is blocking (let it growsTo one)`);
      if (e.ticks !== undefined && !(isWholeRange(e.ticks, 1, 1) && e.ticks[1] >= e.ticks[0])) errors.push(`rule ${rule.id}: ticks must be [min, max], whole numbers from 1`);
    }
    errors.push(...componentCondErrors(r, rule.id, (rule as EntityRule).if, "has"), ...componentCondErrors(r, rule.id, (rule as EntityRule).if, "lacks"));
    const verb = (rule as EntityRule).if?.verb;
    if (verb !== undefined && !(verb in VERB_STATUS)) errors.push(`rule ${rule.id}: verb must be burn, freeze or soak`);
    const c = (rule as CellRule).if;
    for (const cv of c?.cover ?? []) if (!coverIds.has(cv)) errors.push(`rule ${rule.id}: cover ${cv} unknown`);
  }
  for (const s of r.spells) {
    if (s.element && !el.has(s.element)) errors.push(`spell ${s.id}: unknown element ${s.element}`);
    if (!SHAPES.has(s.shape)) errors.push(`spell ${s.id}: unknown shape ${s.shape}`);
    if (!s.name) errors.push(`spell ${s.id}: needs a name`);
    if (s.effect !== undefined && !SPELL_EFFECTS.has(s.effect)) errors.push(`spell ${s.id}: unknown effect ${s.effect}`);
    if (s.effect === "terraform") errors.push(...terraformErrors(r, s));
    if (!isCrater(s.crater)) errors.push(`spell ${s.id}: crater needs a whole depth from 1, radius from 0 and strength from 1`);
  }
  for (const c of r.critters) if (!el.has(c.element)) errors.push(`critter ${c.id}: unknown element ${c.element}`);
  for (const c of r.critters) if (c.hours !== undefined && !isHours(c.hours)) errors.push(`critter ${c.id}: hours must be [from, to], hours from 0 to 24`);
  for (const id of Object.keys(r.machines)) {
    if (!terrainIds.has(id)) errors.push(`machine ${id}: no terrain with that id`);
    else if (!r.terrain[id]!.tags.includes("machine")) errors.push(`machine ${id}: terrain lacks the machine tag`);
  }
  for (const [id, e] of Object.entries(r.entities)) {
    if (e.raider) errors.push(...raiderErrors(el, id, e.raider));
    errors.push(...componentListErrors(r, `entity ${id}`, e.components));
    if (e.material !== undefined && !(e.material in r.terrain)) errors.push(`entity ${id}: material ${e.material} unknown`);
    if (e.explodes && !coverIds.has(e.explodes.cover)) errors.push(`entity ${id}: explodes into unknown cover`);
    if (e.explodes && !isCrater(e.explodes.crater)) errors.push(`entity ${id}: crater needs a whole depth from 1, radius from 0 and strength from 1`);
  }
  for (const k of r.sim.defense?.kinds ?? []) if (!r.entities[k]?.raider) errors.push(`sim defense: kind ${k} is not a raider`);
  for (const k of ["when", "notBy"] as const)
    if (r.sim.status.shatter?.[k] != null && typeof r.sim.status.shatter[k] !== "string") errors.push(`sim status.shatter.${k} must be a name`);
  for (const [id, it] of Object.entries(r.items)) errors.push(...itemErrors(r, id, it));
  errors.push(...componentTableErrors(r));
  errors.push(...groupErrors(r));
  return errors;
}
