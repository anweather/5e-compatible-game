// World state and the small helpers every system shares.
import { RULES } from "./rules.ts";
import { type WeatherNow, weatherAt } from "./sky.ts";

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
    nextId: 1,
    wind: null,
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
export const elevation = (c: Cell) => ((terrainDef(c).elevation ?? 0) + c.h) * RULES.sim.water.unitsPerStep;
export const coverHasTag = (c: Cell, tag: string) => c.cover !== null && (RULES.covers[c.cover]?.tags.includes(tag) ?? false);
export const isSlippery = (c: Cell) => coverHasTag(c, "slippery") || (c.frozen && c.water > 0);
export const isConductive = (c: Cell) => (c.water > 0 && !c.frozen) || hasTag(c, "conductive");
/** The wind fire and gas follow: Gust of Wind's while it blows, else the weather's; -1 for none. */
export const windDir = (w: World): number => (w.wind ? w.wind.dir : w.weather.windDir);

export function flammability(c: Cell): number {
  if (coverHasTag(c, "flammable")) return RULES.covers[c.cover!]!.flammability ?? 0;
  const t = terrainDef(c);
  return t.tags.includes("flammable") ? (t.flammability ?? 0) : 0;
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
  const s = JSON.stringify([w.cells, w.entities, w.critters, w.rng, w.gatesOpen, w.mods, w.ctl, w.tick]);
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
export function lineOfSight(w: World, x0: number, y0: number, x1: number, y1: number): boolean {
  const dx = Math.abs(x1 - x0), dy = -Math.abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
  let err = dx + dy, x = x0, y = y0;
  for (;;) {
    if (x === x1 && y === y1) return true;
    const e2 = 2 * err;
    if (e2 >= dy) { err += dy; x += sx; }
    if (e2 <= dx) { err += dx; y += sy; }
    if (x === x1 && y === y1) return true;
    const c = cellAt(w, x, y)!;
    if (isBlocking(c) && !hasTag(c, "seeThrough")) return false;
  }
}

// ---------- snapshots ----------

/** Everything the simulation needs, as plain JSON (per-tick scratch left out). */
export type WorldSnapshot = Omit<World, "marks" | "logged">;

export function serializeWorld(w: World): WorldSnapshot {
  const { marks: _m, logged: _l, ...rest } = w;
  return JSON.parse(JSON.stringify({ ...rest, log: w.log.slice(-40) })) as WorldSnapshot;
}

export function deserializeWorld(s: WorldSnapshot): World {
  const n = s.w * s.h;
  const copy = JSON.parse(JSON.stringify(s)) as WorldSnapshot;
  // Snapshots from before the weather have no seed: worlds were all made with 1.
  const seed = copy.seed ?? 1;
  return { ...copy, seed, weather: copy.weather ?? weatherAt(RULES.sim, seed, copy.tick), strikes: copy.strikes ?? [], falling: copy.falling ?? [], newMeteors: copy.newMeteors ?? [], marks: { flow: new Uint8Array(n), steam: new Uint8Array(n), wind: new Uint8Array(n) }, logged: new Set() };
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
