// World state and the small helpers every system shares.
import type { Golf } from "./golf.ts";
import { RULES } from "./rules.ts";
import { type WeatherKind, type WeatherNow, weatherAt } from "./sky.ts";

export type Dir = 0 | 1 | 2 | 3; // N E S W
export const DX = [0, 1, 0, -1] as const;
export const DY = [-1, 0, 1, 0] as const;
export const DIR_NAMES = ["north", "east", "south", "west"] as const;

export interface Cell {
  t: string; // terrain id
  water: number; // units, 0..maxLevel (unitsPerStep units = one step of depth)
  frozen: boolean; // the water here is ice
  cover: string | null; // oil, grease, ash
  fire: number; // 0..3
  fuel: number; // ticks of burning left
  gas: string | null;
  gasAmt: number;
  charge: number; // 3 on the tick lightning arrives, then decays
  h: number; // steps raised (+) or dug (-) by terraform spells, on top of the terrain's elevation
  dampUntil?: number; // the tick until which the ground is damp from water soaked in (soak in sim.ts)
  rust?: number; // ticks it has been wet in all, toward its terrain's rust (rust in sim.ts); unset or 0 for none
  heat?: number; // ticks it stays hot (cool in sim.ts): metal fire reached burns creatures on it; unset or 0 cold
  lit?: number; // ticks it still sheds the Light spell's light (lightUp in actions.ts); unset or 0 unlit
  cracked?: number; // its ice or terrain is cracked (shatter in react.ts): the tick it cracked, plus 1; unset whole
  star?: number; // how many falling stars are aimed here (aimStar in actions.ts): the target mark; unset none
  slick?: number; // how many more cells the floating cover here may spread (float.reach, float in sim.ts); unset or 0 none or no end
  life: number; // ticks until what grows or lasts here ripens (ripen in sim.ts): thorns wither, shoots and saplings grow; 0 for nothing
}

export interface Entity {
  id: number;
  kind: string;
  x: number;
  y: number;
  hp: number;
  status: Record<string, number>; // ticks remaining
  /** For a player's wizard: the peer that controls it. */
  owner?: string;
  /** For a raider (defense.ts): the HP it came with, and ticks until it may take its next step. */
  maxHp?: number;
  wait?: number;
}

export type DefensePhase = 0 | 1 | 2 | 3; // idle, running, won, lost (C#: DefensePhase)
/** One player's cast state and grimoire (combos.ts; C#: PlayerCast). Integers and ids only. */
export interface PlayerCast {
  player: string;
  /** The loaded elements, in order; null for none. */
  first: string | null;
  second: string | null;
  /** Ticks until the cast goes off, the meter's full length, and ticks left to load a second element. */
  meter: number;
  total: number;
  window: number;
  /** The d-pad's selected slot, and the slot being cast, or -1. */
  slot: number;
  dpadCasting: number;
  /** The last element's button is down: the meter stops at its last tick until a release. */
  holding: boolean;
  /** The charge the release gave; 1 until then. */
  charge: number;
  /** The ids of the combos this player has cast, sorted: the grimoire. */
  known: string[];
}

/** A meter that reached zero this tick (C#: ComboFired); combo is null for a d-pad spell. */
export interface ComboFired {
  player: string;
  combo: string | null;
  name: string;
  spell: string;
  shape: string | undefined;
  /** This was the player's first cast of the combo: it just joined the grimoire. */
  discovered: boolean;
  /** The strength it goes off at. */
  charge: number;
}

/** The defense field's waves (defense.ts; C#: DefenseState). */
export interface DefenseState {
  phase: DefensePhase;
  wave: number;
  lives: number;
  toSpawn: number;
  nextSpawn: number;
  spawned: number;
  gate: [number, number];
  flag: [number, number];
  /** The squares the raiders may walk in: [x0, x1] × [y0, y1]. */
  x0: number;
  y0: number;
  x1: number;
  y1: number;
}

export interface Critter {
  id: number;
  kind: string;
  x: number;
  y: number;
  timer: number;
  dir: Dir;
}

export interface MachineState {
  level: number;
  hold: number;
  powered: boolean;
}

export type LogKind = "info" | "notable" | "power" | "hurt";
export interface LogEntry {
  tick: number;
  text: string;
  kind: LogKind;
  count: number;
}

/** Shared controls. They live in the world so every player agrees on them. */
export interface Controls {
  playing: boolean;
  speed: number;
}

export interface World {
  scenarioId: string;
  /** Ids of the mods in force, in order. Their patches decide RULES. */
  mods: string[];
  ctl: Controls;
  w: number;
  h: number;
  cells: Cell[];
  /** The land under each cell in water units (hills the cells don't hold), NO_LAND for a water body's cell; absent for flat land. Only the water reads it (World.SetLand). */
  land?: number[] | null;
  entities: Entity[];
  critters: Critter[];
  machines: Record<number, MachineState>;
  gatesOpen: boolean;
  openedBy: string | null;
  tick: number;
  rng: number;
  /** The seed the world was made with: the weather's (sky.ts), as rng is the cells'. */
  seed: number;
  /** This tick's weather (weatherAt), set at the start of each step. */
  weather: WeatherNow;
  /** Squares lightning struck this tick. */
  strikes: [number, number][];
  /** Meteors falling: where, and the tick they land on (tickWeather). */
  falling: [number, number, number][];
  /** Meteors that appeared this tick, for a view. */
  newMeteors: [number, number, number][];
  nextId: number;
  wind: { dir: Dir; ticks: number } | null;
  /** The delayed casts on their way down, in the order cast (World.Stars). */
  stars: Star[];
  /** The whirlwinds still whirling, in the order cast (World.Whirls): middle, radius, ticks it lasts and has run, how often it pulls, the heaviest creature it drags. */
  whirls: Whirl[];
  /** The waves still rolling, in the order cast (World.Waves). */
  waves: Wave[];
  /** The boulders still rolling, in the order cast (World.Boulders). */
  boulders: Boulder[];
  /** Spell golf's hole being played, null before anyone teed off (golf.ts; World.Golf). */
  golf: Golf | null;
  /** Hasten Time: how many times as fast the day clock runs, and the ticks it has left (C#: World.Haste). */
  haste: { rate: number; ticks: number } | null;
  /** Ticks of day the clock has run ahead of tick (Hasten Time); the clock reads tick + clockAhead. */
  clockAhead: number;
  /** Control Weather: the weather held, the tick it was cast and the tick it ends (C#: World.WeatherSet). */
  weatherSet: { kind: WeatherKind; from: number; until: number } | null;
  /** Each player's cast state and grimoire, by player (combos.ts). */
  casts: Record<string, PlayerCast>;
  /** The casts that went off in the last tick. */
  combosFired: ComboFired[];
  /** The defense field's waves, or null before anyone started them (defense.ts). */
  defense: DefenseState | null;
  log: LogEntry[];
  /** Per-tick marks machines read; cleared at the end of each tick. */
  marks: { flow: Uint8Array; steam: Uint8Array; wind: Uint8Array };
  logged: Set<string>;
}

export function newCell(t = "dirt"): Cell {
  return { t, water: 0, frozen: false, cover: null, fire: 0, fuel: 0, gas: null, gasAmt: 0, charge: 0, h: 0, life: 0 };
}

export function createWorld(w: number, h: number, seed = 1): World {
  const n = w * h;
  return {
    scenarioId: "",
    mods: [],
    ctl: { playing: true, speed: 1 },
    w,
    h,
    cells: Array.from({ length: n }, () => newCell()),
    entities: [],
    critters: [],
    machines: {},
    gatesOpen: false,
    openedBy: null,
    tick: 0,
    rng: seed >>> 0,
    seed: seed >>> 0,
    weather: weatherAt(RULES.sim, seed >>> 0, 0),
    strikes: [],
    falling: [],
    newMeteors: [],
    casts: {},
    combosFired: [],
    nextId: 1,
    wind: null,
    stars: [],
    whirls: [],
    waves: [],
    boulders: [],
    golf: null,
    haste: null,
    clockAhead: 0,
    weatherSet: null,
    defense: null,
    log: [],
    marks: { flow: new Uint8Array(n), steam: new Uint8Array(n), wind: new Uint8Array(n) },
    logged: new Set(),
  };
}

export const inBounds = (w: World, x: number, y: number) => x >= 0 && y >= 0 && x < w.w && y < w.h;
export const idx = (w: World, x: number, y: number) => y * w.w + x;
export const cellAt = (w: World, x: number, y: number): Cell | undefined =>
  inBounds(w, x, y) ? w.cells[idx(w, x, y)] : undefined;

export const terrainDef = (c: Cell) => RULES.terrain[c.t]!;
export const hasTag = (c: Cell, tag: string) => terrainDef(c).tags.includes(tag);
/** Raised sim.height.blockAt steps or more: a wall of earth, whatever the terrain. */
export const isRaisedWall = (c: Cell) => c.h >= RULES.sim.height.blockAt;
export const isBlocking = (c: Cell) => hasTag(c, "blocking") || isRaisedWall(c);
/** Terrain elevation plus raised or dug height, in water units, so it compares directly with Cell.water. */
export const NO_LAND = -2147483648;

/** World.SetLand: the land copied, with each cell that has water or a spring now (a lake, the river, the sea) marked NO_LAND; null for flat land. */
export function setLand(w: World, land: number[] | null) {
  w.land = land ? w.cells.map((c, i) => (c.water > 0 || (terrainDef(c).spring ?? 0) > 0 ? NO_LAND : land[i]!)) : null;
}

export const elevation = (c: Cell) => ((terrainDef(c).elevation ?? 0) + c.h) * RULES.sim.water.unitsPerStep;
/** A material's verbs (docs/MATERIALS.md): what code asks instead of the tags (TerrainDef.Burns, Breaks, Conducts). */
export const burns = (m: { tags: string[] }) => m.tags.includes("flammable");
export const breaks = (m: { tags: string[] }) => m.tags.includes("breakable");
export const conducts = (m: { tags: string[] }) => m.tags.includes("conductive");
/** Whether the cell's cover burns (oil, grease, thorns): World.CoverBurns. */
export const coverBurns = (c: Cell) => c.cover !== null && RULES.covers[c.cover] !== undefined && burns(RULES.covers[c.cover]!);

export const coverHasTag = (c: Cell, tag: string) => c.cover !== null && (RULES.covers[c.cover]?.tags.includes(tag) ?? false);
export const isSlippery = (c: Cell) => coverHasTag(c, "slippery") || (c.frozen && c.water > 0);
/** Whether walkers can climb or cross a cell on its cover (vines: up a wall, over a pit or a stream), for a view (World.IsClimbable). */
export const isClimbable = (c: Cell) => coverHasTag(c, "climbable");
export const isConductive = (c: Cell) => (c.water > 0 && !c.frozen) || conducts(terrainDef(c));
/** The wind fire and gas follow: Gust of Wind's while it blows, else the weather's; -1 for none. */
/** A delayed cast on its way down (spells.json delay: Falling Star; World.Star): what lands, where, how charged, the tick it lands. */
export interface Star {
  spell: string;
  x: number;
  y: number;
  dir: Dir;
  charge: number;
  radius: number;
  lands: number;
}

/** A boulder still rolling (Rolling Boulder; World.Boulder): the cell it's on, the way it rolls, the cells it may still roll, how often it rolls one and its age. */
export interface Boulder extends Mover {
  spell: string;
  left: number;
  every: number;
  age: number;
}

/**
 * Something rolling along an aim (World.Mover): the cell it's on, the grid way nearest its aim (what its force pushes
 * along), and the line it follows: from (ox, oy) along the whole-number aim (ax, ay), step cells so far.
 */
export interface Mover {
  x: number;
  y: number;
  dir: Dir;
  ox: number;
  oy: number;
  ax: number;
  ay: number;
  step: number;
}

/**
 * A wave still rolling (Wave; World.Wave): its front's middle, the way it rolls, how far the front reaches to each side,
 * the cells it may still roll, how often it rolls one, its age, the power its element lands with and the heaviest thing it knocks back.
 */
export interface Wave extends Mover {
  spell: string;
  width: number;
  left: number;
  every: number;
  age: number;
  power: number;
  maxMass: number;
}

/** A whirlwind still whirling (spells.json pullEvery: Whirlwind; World.Whirl). */
export interface Whirl {
  x: number;
  y: number;
  radius: number;
  ticks: number;
  age: number;
  every: number;
  maxMass: number;
  /** The spell it came from, the way it was cast, how often it moves, and what it carries (a CarryDef's id, or null). */
  spell: string;
  dir: Dir;
  travel: number;
  carrying: string | null;
}

export const windDir = (w: World): number => (w.wind ? w.wind.dir : w.weather.windDir);

export function flammability(c: Cell): number {
  if (coverBurns(c)) return RULES.covers[c.cover!]!.flammability ?? 0;
  const t = terrainDef(c);
  return burns(t) ? (t.flammability ?? 0) : 0;
}

/** mulberry32: small, seedable, and its whole state is one uint32 in the world. */
export function rand(w: World): number {
  let t = (w.rng = (w.rng + 0x6d2b79f5) >>> 0);
  t = Math.imul(t ^ (t >>> 15), t | 1);
  t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}
export const roll = (w: World, [n, sides]: [number, number]) => {
  let total = 0;
  for (let i = 0; i < n; i++) total += 1 + Math.floor(rand(w) * sides);
  return total;
};

export function entityAt(w: World, x: number, y: number): Entity | undefined {
  return w.entities.find((e) => e.x === x && e.y === y);
}
export function critterAt(w: World, x: number, y: number): Critter | undefined {
  return w.critters.find((c) => c.x === x && c.y === y);
}

export function log(w: World, text: string, kind: LogKind = "info") {
  const last = w.log[w.log.length - 1];
  if (last && last.text === text && w.tick - last.tick < 40) {
    last.count++;
    last.tick = w.tick;
    return;
  }
  w.log.push({ tick: w.tick, text, kind, count: 1 });
  if (w.log.length > 200) w.log.shift();
}

/** Log once per tick per key, so spreading fire doesn't flood the log. */
export function logOnce(w: World, key: string, text: string, kind: LogKind = "info") {
  if (w.logged.has(key)) return;
  w.logged.add(key);
  log(w, text, kind);
}

export function addGas(c: Cell, id: string, n: number) {
  const max = RULES.sim.gas.maxAmount;
  if (c.gas === null || c.gas === id) {
    c.gas = id;
    c.gasAmt = Math.min(max, c.gasAmt + n);
  } else if (n > c.gasAmt) {
    c.gas = id;
    c.gasAmt = Math.min(max, n);
  }
}

/** Neighbour order is fixed so the simulation stays deterministic. */
export function* neighbours(w: World, x: number, y: number): Generator<[number, number, Dir]> {
  for (let d = 0 as Dir; d < 4; d = (d + 1) as Dir) {
    const nx = x + DX[d], ny = y + DY[d];
    if (inBounds(w, nx, ny)) yield [nx, ny, d];
  }
}

/** Stable fingerprint of the simulated state, for determinism tests. */
export function hashWorld(w: World): string {
  // The defense, the casts, the whirlwinds, the stars, the waves, the boulders and the golf ball join only once there are any, so older worlds hash as they did.
  const s = JSON.stringify([w.cells, w.entities, w.critters, w.rng, w.gatesOpen, w.mods, w.ctl, w.tick, w.clockAhead, w.haste, w.weatherSet, ...(w.defense ? [w.defense] : []), ...(Object.keys(w.casts).length ? [w.casts] : []), ...(w.whirls.length ? [w.whirls] : []), ...(w.stars.length ? [w.stars] : []), ...(w.waves.length ? [w.waves] : []), ...(w.boulders.length ? [w.boulders] : []), ...(w.golf ? [w.golf] : [])]);
  return fnv1a(s);
}

/** The FNV-1a of a string, in hex (C#: Fnv1a.Hash). */
export function fnv1a(s: string): string {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 0x01000193);
  return (h >>> 0).toString(16);
}

/** A player's wizard, if still standing. With no owner given: the first one. */
export const playerOf = (w: World, owner?: string): Entity | undefined =>
  w.entities.find((e) => RULES.entities[e.kind]?.player && (owner === undefined || e.owner === owner));

/** Dominant-axis direction from a to b, or null if they're the same square. */
export function dirFrom(ax: number, ay: number, bx: number, by: number): Dir | null {
  const dx = bx - ax, dy = by - ay;
  if (dx === 0 && dy === 0) return null;
  return Math.abs(dx) >= Math.abs(dy) ? (dx > 0 ? 1 : 3) : dy > 0 ? 2 : 0;
}

/** Bresenham sight line. Blocking terrain stops it unless tagged seeThrough (gate bars). */
/** Counts a square of thin fog on a sight line; true once the line has crossed its gas's hidesAfter of them (C#: World.Veiled). */
function veiled(c: Cell, seen: { thin: number }): boolean {
  const after = c.gas ? RULES.gases[c.gas]?.hidesAfter ?? 0 : 0;
  return after > 0 && ++seen.thin >= after;
}

/** Whether a square's gas is thick enough to hide what's behind it (a gas's blocksSightAt: Fog Cloud). */
export function fogged(c: Cell): boolean {
  const at = c.gas ? RULES.gases[c.gas]?.blocksSightAt ?? 0 : 0;
  return at > 0 && c.gasAmt >= at;
}

/**
 * Whether (x1, y1) can be seen from (x0, y0): no blocking square between them (bar see-through ones,
 * gate bars), no thick fog, nor a long reach of thin fog (a gas's hidesAfter: fog weather). The ends don't count. With fogOnly, only the fog counts (C#: FogBetween).
 */
export function lineOfSight(w: World, x0: number, y0: number, x1: number, y1: number, fogOnly = false): boolean {
  const dx = Math.abs(x1 - x0), dy = -Math.abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
  let err = dx + dy, x = x0, y = y0;
  const seen = { thin: 0 };
  for (;;) {
    if (x === x1 && y === y1) return true;
    const e2 = 2 * err;
    if (e2 >= dy) { err += dy; x += sx; }
    if (e2 <= dx) { err += dx; y += sy; }
    if (x === x1 && y === y1) return true;
    const c = cellAt(w, x, y);
    if (!c) return true;
    if (fogged(c) || (!fogOnly && isBlocking(c) && !hasTag(c, "seeThrough"))) return false;
    if (veiled(c, seen)) return false;
  }
}

/** The tick the day clock reads: Hasten Time runs it ahead of the world's (C#: World.ClockTick). */
export const clockTick = (w: World) => w.tick + w.clockAhead;

// ---------- snapshots ----------

/** Everything the simulation needs, as plain JSON (per-tick scratch left out). */
export type WorldSnapshot = Omit<World, "marks" | "logged">;

export function serializeWorld(w: World): WorldSnapshot {
  const { marks: _m, logged: _l, ...rest } = w;
  return JSON.parse(JSON.stringify({ ...rest, log: w.log.slice(-40) })) as WorldSnapshot;
}

// No button is down on a loaded game, so a held cast goes off as a tap.
const unheld = (casts: Record<string, PlayerCast>) => Object.fromEntries(Object.entries(casts).map(([p, c]) => [p, { ...c, holding: false, charge: c.charge ?? 1 }]));

export function deserializeWorld(s: WorldSnapshot): World {
  const n = s.w * s.h;
  const copy = JSON.parse(JSON.stringify(s)) as WorldSnapshot;
  // Snapshots from before the weather have no seed: worlds were all made with 1.
  const seed = copy.seed ?? 1;
  // Snapshots from before the time spells have no clock lead, haste or held weather.
  return { ...copy, seed, haste: copy.haste ?? null, clockAhead: copy.clockAhead ?? 0, weatherSet: copy.weatherSet ?? null, defense: copy.defense ?? null, weather: copy.weather ?? weatherAt(RULES.sim, seed, copy.tick), strikes: copy.strikes ?? [], falling: copy.falling ?? [], newMeteors: copy.newMeteors ?? [], casts: unheld(copy.casts ?? {}), combosFired: copy.combosFired ?? [], whirls: copy.whirls ?? [], waves: copy.waves ?? [], boulders: copy.boulders ?? [], golf: copy.golf ?? null, stars: copy.stars ?? [], marks: { flow: new Uint8Array(n), steam: new Uint8Array(n), wind: new Uint8Array(n) }, logged: new Set() };
}

/**
 * After a mod is switched off, the board may name things the rules no longer
 * define. Replace them with something harmless, the same way on every peer.
 */
export function sanitizeWorld(w: World) {
  const { min, max } = RULES.sim.height;
  for (const c of w.cells) {
    c.h = Math.max(min, Math.min(max, c.h));
    if (!RULES.terrain[c.t]) c.t = "dirt";
    if (c.cover && !RULES.covers[c.cover]) c.cover = null;
    if (c.gas && !RULES.gases[c.gas]) { c.gas = null; c.gasAmt = 0; }
  }
  w.entities = w.entities.filter((e) => RULES.entities[e.kind]);
  w.critters = w.critters.filter((c) => RULES.critters.some((d) => d.id === c.kind));
}
