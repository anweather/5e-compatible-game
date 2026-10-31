// The Dragon (docs/BACKLOG.md 26): the world reset as an event. summonDragon, or the changed-cell budget passed, starts
// a flight: a warning, long passes over the board, then leaving. Each pass sweeps a strip of chunks, and every chunk its
// breath reaches goes back to the starting state, a few a tick. Whoever the breath catches is downed unless sheltered
// (caughtByBreath). The C# core's World.Dragon.cs ports this line for line; the board has no chunk store, so here a
// chunk is just its 64 × 64 cells, and all are active. The path comes from the seed and the flight's number through
// sky.ts's hash, never from the world's rand, so a world keeps its random numbers whether the dragon comes or not.
//
// The starting state (keepStart) is a palette of the distinct cells the world was built with and, per cell, its index
// in the palette. The changed-cell count is an audit: each tick one chunk is compared with the starting state (the next
// in row-major order), and the total is the sum of what each chunk last counted.
import { RULES, hasComponent } from "./rules.ts";
import { hash } from "./sky.ts";
import {
  type Cell, type Dragon, type World,
  cellAt, entityAt, idx, isBlocking, log, logOnce, sanitizeCell, terrainDef,
} from "./world.ts";

export const CHUNK_SIZE = 64;
export const DOWNED_STATUS = "downed";
const PATH_SALT = 17;
const WARNING = 1, PASSES = 2, LEAVING = 3; // C#: DragonPhase

/** sim.json's dragon (C#: DragonParams): the flight's timing and shape, the changed-cell budget and the shelter rule. */
export interface DragonParams {
  warningTicks: number;
  turnTicks: number;
  leavingTicks: number;
  speed: number;
  depth: number;
  breathChunks: number;
  overlapChunks: number;
  chunksPerTick: number;
  budget: number;
  shelterWater: number;
  wallReach: number;
  wallTerrains: string[];
}

const params = (): DragonParams => ({
  warningTicks: 180, turnTicks: 24, leavingTicks: 120, speed: 6, depth: 12, breathChunks: 3, overlapChunks: 1, chunksPerTick: 1,
  budget: 100000, shelterWater: 8, wallReach: 2, wallTerrains: ["stone", "rock"], ...RULES.sim.dragon,
});

const chunkOf = (cell: number) => cell >> 6;
const chunksAcross = (cells: number) => chunkOf(cells - 1) + 1;

/**
 * Takes the world as it stands as its starting state, for the dragon to put back (World.KeepStart). A world that never
 * calls this behaves as if there were no dragon.
 */
export function keepStart(w: World): void {
  const shapes: Cell[] = [], known = new Map<string, number>();
  const at = w.cells.map((c) => {
    const key = JSON.stringify(c);
    let shape = known.get(key);
    if (shape === undefined) {
      shape = shapes.length;
      shapes.push({ ...c });
      known.set(key, shape);
    }
    return shape;
  });
  const props = w.entities.filter((e) => !hasComponent(RULES.entities[e.kind]!, "creature")).map((e) => ({ ...e, status: { ...e.status } }));
  w.start = { shapes, at, golf: w.golf ? { ...w.golf } : null, props, changed: new Array<number>(chunksAcross(w.w) * chunksAcross(w.h)).fill(0), total: 0, auditAt: 0, flights: 0 };
  for (let k = 0; k < w.start.changed.length; k++) recount(w, k);
}

/** The dragon's summons (World.ApplyDragon): the warning starts now, unless one is flying or the world keeps no starting state. */
export function summonDragon(w: World): void {
  if (!w.start) {
    log(w, "Nothing answers: this world keeps no starting state");
    return;
  }
  if (w.dragon) {
    log(w, "The dragon is already coming");
    return;
  }
  summon(w);
}

function summon(w: World) {
  const p = params(), s = w.start!;
  const h = hash(w.seed, s.flights, PATH_SALT);
  s.flights++;
  const d: Dragon = { phase: WARNING, until: w.tick + p.warningTicks, alongX: h >>> 31 === 1, fromFar: ((h >>> 30) & 1) === 1, back: ((h >>> 29) & 1) === 1, pass: 0, passes: 0, travelled: 0, done: 0 };
  w.dragon = d;
  const rows = rowsOf(w, d), width = Math.min(p.breathChunks, rows), step = Math.max(1, width - p.overlapChunks);
  d.passes = 1 + Math.floor((rows - width + step - 1) / step);
  log(w, "The sky darkens: a shadow crosses the valley", "notable");
}

// ---------- the flight's shape: pure sums on the world's size and the state ----------

const alongOf = (w: World, d: Dragon) => (d.alongX ? w.w : w.h);
const acrossOf = (w: World, d: Dragon) => (d.alongX ? w.h : w.w);
const colsOf = (w: World, d: Dragon) => chunksAcross(alongOf(w, d));
const rowsOf = (w: World, d: Dragon) => chunksAcross(acrossOf(w, d));
const widthOf = (w: World, d: Dragon) => Math.min(params().breathChunks, rowsOf(w, d));

/** The first chunk row of a pass's strip. */
function stripOf(w: World, d: Dragon): number {
  const rows = rowsOf(w, d), width = widthOf(w, d), step = Math.max(1, width - params().overlapChunks);
  const at = Math.min(d.pass * step, rows - width);
  return d.fromFar ? rows - width - at : at;
}

/** This pass runs toward 0: passes go there and back. */
const backward = (d: Dragon) => d.back !== (d.pass % 2 === 1);

/** The cells under the breath this tick, [x0, x1) × [y0, y1), clipped to the board, or null when it isn't out (World.BreathBox). */
export function breathBox(w: World): [number, number, number, number] | null {
  const d = w.dragon;
  if (!d || d.phase !== PASSES || d.until !== 0 || d.travelled <= 0) return null;
  const depth = params().depth, along = alongOf(w, d), across = acrossOf(w, d);
  const from = Math.max(0, d.travelled - depth), to = Math.min(along, d.travelled);
  if (from >= to) return null;
  const a0 = backward(d) ? along - to : from, a1 = backward(d) ? along - from : to;
  const b0 = stripOf(w, d) * CHUNK_SIZE, b1 = Math.min(across, b0 + widthOf(w, d) * CHUNK_SIZE);
  return d.alongX ? [a0, b0, a1, b1] : [b0, a0, b1, a1];
}

/** Whether the breath catches what stands at (x, y) this tick: it's in the breath's cells and not sheltered (World.CaughtByBreath). */
export function caughtByBreath(w: World, x: number, y: number): boolean {
  const box = breathBox(w);
  return box !== null && x >= box[0] && x < box[2] && y >= box[1] && y < box[3] && !shelteredFromBreath(w, x, y);
}

/**
 * The shelter rule (World.ShelteredFromBreath): water at least shelterWater deep (not frozen) on the cell, or a wall on the
 * breath's side: a blocking cell of wallTerrains within wallReach cells straight toward where the dragon flies from.
 */
export function shelteredFromBreath(w: World, x: number, y: number): boolean {
  const p = params(), c = cellAt(w, x, y);
  if (!c) return false;
  if (c.water >= p.shelterWater && !c.frozen) return true;
  const d = w.dragon;
  if (!d) return false;
  const back = backward(d) ? 1 : -1;
  for (let k = 1; k <= p.wallReach; k++) {
    const n = d.alongX ? cellAt(w, x + back * k, y) : cellAt(w, x, y + back * k);
    if (n && isBlocking(n) && p.wallTerrains.includes(n.t)) return true;
  }
  return false;
}

// ---------- the tick ----------

/** One tick of the dragon, after the timers: the flight, the chunks its breath reached, the audit (World.RunDragon). */
export function tickDragon(w: World): void {
  if (!w.start) return;
  const p = params();
  flyDragon(w, p);
  for (let k = 0; k < p.chunksPerTick && freshenNext(w); k++);
  endPass(w, p);
  auditNext(w);
  if (!w.dragon && p.budget > 0 && w.start.total > p.budget) summon(w);
}

function flyDragon(w: World, p: DragonParams) {
  const d = w.dragon;
  if (!d) return;
  if (d.phase === WARNING) {
    if (w.tick < d.until) return;
    d.phase = PASSES;
    d.until = 0;
    log(w, "The dragon dives out of the clouds, breathing fire", "notable");
    snuffFires(w);
  } else if (d.phase === PASSES) {
    if (d.until > 0) {
      if (w.tick >= d.until) d.until = 0;
      return;
    }
    d.travelled = Math.min(d.travelled + p.speed, alongOf(w, d) + p.depth);
    knockDown(w);
  } else if (w.tick >= d.until) {
    w.dragon = null;
    log(w, "The dragon wheels away: the valley is fresh", "notable");
  }
}

/** As it dives every fire dies, on all the chunks at once: a wildfire left burning would walk back over the chunks already reset (World.SnuffFires). */
function snuffFires(w: World) {
  for (const c of w.cells) {
    if (c.fire === 0) continue;
    c.fire = 0;
    c.fuel = 0;
  }
}

/** Everyone the breath catches goes down (sim.json downed), unless they already are. */
function knockDown(w: World) {
  const s = RULES.sim, ticks = Math.floor(Math.max(0.1, s.downed?.seconds ?? 3) * s.ticksPerSecond);
  for (const e of w.entities) {
    if (!hasComponent(RULES.entities[e.kind]!, "creature") || (e.status[DOWNED_STATUS] ?? 0) > 0 || !caughtByBreath(w, e.x, e.y)) continue;
    e.status[DOWNED_STATUS] = ticks;
    logOnce(w, "dragon-downed", "The dragon's breath knocks them down", "hurt");
  }
}

/** The next chunk the breath has reached on this pass, reset; false if there's none yet. */
function freshenNext(w: World): boolean {
  const d = w.dragon;
  if (!d || d.phase !== PASSES || d.until !== 0 || d.done >= chunksReached(w, d)) return false;
  const width = widthOf(w, d), row = stripOf(w, d) + d.done % width;
  let col = Math.floor(d.done / width);
  if (backward(d)) col = colsOf(w, d) - 1 - col;
  d.done++;
  freshen(w, d.alongX ? col : row, d.alongX ? row : col);
  return true;
}

/** The chunks of this pass the breath's lead has reached, column by column along the pass. */
function chunksReached(w: World, d: Dragon): number {
  const along = alongOf(w, d), lead = Math.min(d.travelled, along);
  if (lead <= 0) return 0;
  const cols = backward(d) ? colsOf(w, d) - chunkOf(along - lead) : chunkOf(lead - 1) + 1;
  return cols * widthOf(w, d);
}

/** A pass ends when its breath is off the far edge and all its chunks are done: the next, or leaving. */
function endPass(w: World, p: DragonParams) {
  const d = w.dragon;
  if (!d || d.phase !== PASSES || d.until !== 0) return;
  if (d.travelled < alongOf(w, d) + p.depth || d.done < colsOf(w, d) * widthOf(w, d)) return;
  if (d.pass + 1 < d.passes) {
    d.pass++;
    d.travelled = 0;
    d.done = 0;
    d.until = w.tick + Math.max(1, p.turnTicks);
    return;
  }
  d.phase = LEAVING;
  d.until = w.tick + Math.max(1, p.leavingTicks);
  d.travelled = 0;
  log(w, "The dragon lands on the mountain and roars", "notable");
}

/** The cells' indices of a chunk, row by row, those inside the board. */
function* chunkCells(w: World, cx: number, cy: number): Generator<number> {
  const x1 = Math.min(w.w, (cx + 1) * CHUNK_SIZE), y1 = Math.min(w.h, (cy + 1) * CHUNK_SIZE);
  for (let y = cy * CHUNK_SIZE; y < y1; y++) for (let x = cx * CHUNK_SIZE; x < x1; x++) yield idx(w, x, y);
}

const inChunk = (w: World, cx: number, cy: number, index: number) => chunkOf(index % w.w) === cx && chunkOf(Math.floor(index / w.w)) === cy;

/** A chunk back as it started (World.Freshen): its cells, its machines and linked gates, its props, and the ball if its tee is here. */
function freshen(w: World, cx: number, cy: number) {
  const s = w.start!;
  for (const i of chunkCells(w, cx, cy)) {
    w.cells[i] = { ...s.shapes[s.at[i]!]! };
    sanitizeCell(w.cells[i]!);
    w.marks.flow[i] = 0;
    w.marks.steam[i] = 0;
    w.marks.wind[i] = 0;
  }
  const key = cy * chunksAcross(w.w) + cx;
  s.total -= s.changed[key]!;
  s.changed[key] = 0;
  for (const m of Object.keys(w.machines).map(Number)) if (inChunk(w, cx, cy, m)) delete w.machines[m];
  for (const l of w.gateLinks) if (inChunk(w, cx, cy, l.gate)) l.open = s.shapes[s.at[l.gate]!]!.t === "gate_open";
  freshenProps(w, cx, cy);
  const tee = w.golf ?? s.golf;
  if (tee && chunkOf(tee.teeX) === cx && chunkOf(tee.teeY) === cy) w.golf = s.golf ? { ...s.golf } : null;
}

/** The chunk's things as built: what's on it now goes, as do those that started there, and those come back. (The C# core's are the view's: ChunkFreshened.) */
function freshenProps(w: World, cx: number, cy: number) {
  const home = w.start!.props.filter((e) => RULES.entities[e.kind] && chunkOf(e.x) === cx && chunkOf(e.y) === cy);
  w.entities = w.entities.filter((e) => hasComponent(RULES.entities[e.kind]!, "creature") || !(home.some((h) => h.id === e.id) || (chunkOf(e.x) === cx && chunkOf(e.y) === cy)));
  for (const e of home) if (!entityAt(w, e.x, e.y)) w.entities.push({ ...e, status: { ...e.status } });
}

// ---------- the changed-cell count ----------

/** The next chunk's count, afresh: at most one chunk a tick, however many have changed. */
function auditNext(w: World) {
  const s = w.start!;
  const k = s.auditAt;
  s.auditAt = (k + 1) % s.changed.length;
  recount(w, k);
}

function recount(w: World, key: number) {
  const s = w.start!, cols = chunksAcross(w.w);
  let count = 0;
  for (const i of chunkCells(w, key % cols, Math.floor(key / cols))) if (differs(w.cells[i]!, s.shapes[s.at[i]!]!)) count++;
  s.total += count - s.changed[key]!;
  s.changed[key] = count;
}

/**
 * What lasts: the ground, its height and its cover. What goes by itself doesn't count: fire, gas, charge and timers,
 * but also water, ice, snow and mud that will dry, or the weather alone would bring the dragon (rain wets 40% of
 * the cells and muddies the dirt, snow lies on all of them).
 */
const differs = (c: Cell, start: Cell) =>
  c.h !== start.h || (c.t !== start.t && (terrainDef(c).driesTo ?? c.t) !== start.t)
  || (c.cover !== start.cover && lastingCover(c) !== lastingCover(start));

const lastingCover = (c: Cell) => (c.cover !== null && (RULES.covers[c.cover]?.meltWater ?? 0) > 0 ? null : c.cover);
