// The defense field's waves (docs/BACKLOG.md 17); the C# port is
// Chemistry/World.Defense.cs, line for line. startDefense begins them; each
// tick (after tickEntities) the next raider of the wave comes through the gate
// on its schedule, every raider takes a step toward the flag along the
// cheapest way, and a raider reaching the flag costs a life. Raiders are
// entities with a "raider" row in entities.json, so spells, burning, shocks
// and thorns hurt them as they hurt any creature. The way is a cost field from
// the flag (Dijkstra), worked out afresh each tick: open ground 1, rough going
// (deep water, a climb or drop of more than a step) more and slower, a raised
// wall a lot (battered down a step at a time when there's no better way),
// blocking terrain never. Ties go N, E, S, W.
import { RULES, type DefenseParams, type EntityDef } from "./rules.ts";
import { thorns } from "./entities.ts";
import { reshape } from "./terraform.ts";
import {
  type Cell, type DefenseState, type Entity, type World,
  DX, DY, cellAt, elevation, entityAt, hasTag, inBounds, isRaisedWall, log, logOnce,
} from "./world.ts";

const UNREACHED = Number.MAX_SAFE_INTEGER;
export const RUNNING = 1, WON = 2, LOST = 3;

/** How many raiders a wave brings: first, and growth more for each wave after. */
export const waveSize = (p: DefenseParams, wave: number) => p.first + p.growth * (wave - 1);

const raiders = (w: World) => w.entities.filter((e) => RULES.entities[e.kind]?.raider);

/** Raiders still to beat in the wave under way: on the board and still to come (C#: World.DefenseLeft). */
export const defenseLeft = (w: World) => (w.defense ? raiders(w).length + w.defense.toSpawn : 0);

/**
 * The start command (C#: World.ApplyDefense with DefenseKind.Start): wave 1 with full
 * lives (again, after a win or a loss), unless waves are already coming. Between ticks, like a cast.
 */
export function startDefense(w: World, gate: [number, number], flag: [number, number]): void {
  if (w.defense && w.defense.phase === RUNNING) {
    log(w, "The raiders are already coming");
    return;
  }
  if (!inBounds(w, gate[0], gate[1]) || !inBounds(w, flag[0], flag[1])) return;
  const p = RULES.sim.defense;
  const m = Math.max(0, p.margin);
  w.entities = w.entities.filter((e) => !RULES.entities[e.kind]?.raider);
  w.defense = {
    phase: RUNNING, wave: 1, lives: p.lives, toSpawn: waveSize(p, 1), nextSpawn: w.tick + p.warmup, spawned: 0,
    gate: [gate[0], gate[1]], flag: [flag[0], flag[1]],
    x0: Math.max(0, Math.min(gate[0], flag[0]) - m), x1: Math.min(w.w - 1, Math.max(gate[0], flag[0]) + m),
    y0: Math.max(0, Math.min(gate[1], flag[1]) - m), y1: Math.min(w.h - 1, Math.max(gate[1], flag[1]) + m),
  };
  log(w, `Wave 1 of ${p.waves} is coming! Hold the flag`, "notable");
}

/** One tick of the waves (C#: World.TickDefense), after tickEntities. */
export function tickDefense(w: World): void {
  const d = w.defense;
  if (!d || d.phase !== RUNNING) return;
  const p = RULES.sim.defense;
  spawnNext(w, d, p);
  march(w, d, p);
  if (d.phase === RUNNING) nextWave(w, d, p);
}

const paceOf = (def: EntityDef) => def.raider?.ticksPerSquare ?? 4;

function spawnNext(w: World, d: DefenseState, p: DefenseParams) {
  if (d.toSpawn <= 0 || w.tick < d.nextSpawn) return;
  const kind = p.kinds.length > 0 ? p.kinds[d.spawned % p.kinds.length]! : undefined;
  const def = kind !== undefined ? RULES.entities[kind] : undefined;
  if (!kind || !def) {
    d.toSpawn = 0;
    return;
  }
  const [x, y] = d.gate;
  if (entityAt(w, x, y)) return; // the gate is crowded: next tick
  const hp = def.hp + p.hpPerWave * (d.wave - 1);
  w.entities.push({ id: w.nextId++, kind, x, y, hp, status: {}, maxHp: hp, wait: paceOf(def) - 1 });
  d.toSpawn--;
  d.spawned++;
  d.nextSpawn = w.tick + p.spawnEvery;
}

function march(w: World, d: DefenseState, p: DefenseParams) {
  const list = raiders(w);
  if (list.length === 0) return;
  const cost = costField(w, d, p);
  for (const r of list) {
    if (!w.entities.includes(r)) continue;
    if ((r.wait ?? 0) > 0) {
      r.wait = r.wait! - 1;
      continue;
    }
    if ((r.status.frozen ?? 0) > 0 || (r.status.snagged ?? 0) > 0) continue;
    advance(w, d, p, r, cost);
    if (d.phase !== RUNNING) return;
  }
}

function advance(w: World, d: DefenseState, p: DefenseParams, r: Entity, cost: number[]) {
  const dir = bestStep(w, d, p, cost, r.x, r.y);
  if (dir < 0) return;
  const nx = r.x + DX[dir], ny = r.y + DY[dir];
  const to = cellAt(w, nx, ny)!;
  const def = RULES.entities[r.kind]!;
  if (isRaisedWall(to)) {
    reshape(w, nx, ny, -1);
    logOnce(w, `batter#${r.id}`, `The ${def.name.toLowerCase()} batters at the wall`);
    r.wait = p.batterTicks;
    return;
  }
  if (entityAt(w, nx, ny)) return; // wait for the one ahead
  const isRough = rough(p, cellAt(w, r.x, r.y)!, to);
  r.x = nx;
  r.y = ny;
  r.wait = paceOf(def) - 1 + (isRough ? p.roughTicks : 0);
  thorns(w, r, to);
  if (w.entities.includes(r) && r.x === d.flag[0] && r.y === d.flag[1]) reachFlag(w, d, r, def);
}

function reachFlag(w: World, d: DefenseState, r: Entity, def: EntityDef) {
  w.entities.splice(w.entities.indexOf(r), 1);
  d.lives--;
  log(w, `A ${def.name.toLowerCase()} reaches the flag! ${Math.max(0, d.lives)} lives left`, "hurt");
  if (d.lives > 0) return;
  d.phase = LOST;
  d.toSpawn = 0;
  w.entities = w.entities.filter((e) => !RULES.entities[e.kind]?.raider);
  log(w, "The flag falls: the raiders win this time", "notable");
}

function nextWave(w: World, d: DefenseState, p: DefenseParams) {
  if (d.toSpawn > 0 || raiders(w).length > 0) return;
  if (d.wave >= p.waves) {
    d.phase = WON;
    log(w, `All ${p.waves} waves beaten: the flag stands!`, "notable");
    return;
  }
  d.wave++;
  d.toSpawn = waveSize(p, d.wave);
  d.nextSpawn = w.tick + p.betweenWaves;
  log(w, `Wave ${d.wave} of ${p.waves} is coming!`, "notable");
}

// ---------- the way to the flag ----------

const inField = (d: DefenseState, x: number, y: number) => x >= d.x0 && x <= d.x1 && y >= d.y0 && y <= d.y1;
const fieldIndex = (d: DefenseState, x: number, y: number) => (y - d.y0) * (d.x1 - d.x0 + 1) + (x - d.x0);

const rough = (p: DefenseParams, from: Cell, to: Cell) =>
  (to.water >= p.deepWater && !to.frozen) || Math.abs(elevation(to) - elevation(from)) > RULES.sim.water.unitsPerStep;

/** The cost of stepping from a square onto a neighbour: -1 if it can't be done (C#: World.StepCost). */
function stepCost(p: DefenseParams, from: Cell, to: Cell | undefined): number {
  if (!to) return -1;
  if (hasTag(to, "blocking")) return -1;
  if (isRaisedWall(to)) return p.wallCost;
  return rough(p, from, to) ? p.roughCost : 1;
}

/** The cheapest way to the flag from every square of the field (UNREACHED where there is none). */
export function costField(w: World, d: DefenseState, p: DefenseParams): number[] {
  const width = d.x1 - d.x0 + 1, n = width * (d.y1 - d.y0 + 1);
  const cost = new Array<number>(n).fill(UNREACHED);
  const heap: number[] = [];
  const pushKey = (i: number) => {
    heap.push(cost[i]! * n + i);
    let k = heap.length - 1;
    while (k > 0) {
      const parent = (k - 1) >> 1;
      if (heap[parent]! <= heap[k]!) break;
      [heap[parent], heap[k]] = [heap[k]!, heap[parent]!];
      k = parent;
    }
  };
  const popKey = (): number => {
    const top = heap[0]!;
    const last = heap.pop()!;
    if (heap.length > 0) {
      heap[0] = last;
      let k = 0;
      for (;;) {
        const a = 2 * k + 1, b = a + 1;
        let least = k;
        if (a < heap.length && heap[a]! < heap[least]!) least = a;
        if (b < heap.length && heap[b]! < heap[least]!) least = b;
        if (least === k) break;
        [heap[least], heap[k]] = [heap[k]!, heap[least]!];
        k = least;
      }
    }
    return top;
  };
  const start = fieldIndex(d, d.flag[0], d.flag[1]);
  cost[start] = 0;
  pushKey(start);
  while (heap.length > 0) {
    const top = popKey();
    const i = top % n, c = Math.floor(top / n);
    if (c > cost[i]!) continue;
    const x = d.x0 + (i % width), y = d.y0 + Math.floor(i / width);
    const here = cellAt(w, x, y);
    for (let dir = 0; dir < 4; dir++) {
      const nx = x + DX[dir]!, ny = y + DY[dir]!;
      if (!inField(d, nx, ny)) continue;
      const step = stepCost(p, cellAt(w, nx, ny)!, here);
      if (step < 0) continue;
      const ni = fieldIndex(d, nx, ny), total = c + step;
      if (total >= cost[ni]!) continue;
      cost[ni] = total;
      pushKey(ni);
    }
  }
  return cost;
}

/** The way (0..3) a raider at (x, y) steps toward the flag, or -1 to stay (C#: World.BestStep). */
function bestStep(w: World, d: DefenseState, p: DefenseParams, cost: number[], x: number, y: number): number {
  if (!inField(d, x, y) || cost[fieldIndex(d, x, y)] === UNREACHED) return -1;
  const here = cellAt(w, x, y)!;
  let best = -1, bestCost = Infinity;
  for (let dir = 0; dir < 4; dir++) {
    const nx = x + DX[dir]!, ny = y + DY[dir]!;
    if (!inField(d, nx, ny)) continue;
    const on = cost[fieldIndex(d, nx, ny)]!;
    if (on === UNREACHED) continue;
    const step = stepCost(p, here, cellAt(w, nx, ny));
    if (step < 0 || on + step >= bestCost) continue;
    bestCost = on + step;
    best = dir;
  }
  return best;
}

// ---------- the shared golden ----------

const HASHED_STATUSES = ["burning", "wet", "frozen", "snagged"];

/** The defense as text, the same as C#'s World.DefenseText: its FNV-1a is the shared golden and part of the checksum. */
export function defenseText(w: World): string {
  const d = w.defense;
  if (!d) return "-";
  let s = `${d.phase}/${d.wave}/${d.lives}/${d.toSpawn}/${d.nextSpawn}/${d.spawned}|`;
  for (const r of raiders(w)) {
    s += `${r.id},${r.kind},${r.x},${r.y},${r.hp},${r.wait ?? 0}`;
    for (const k of HASHED_STATUSES) s += `,${r.status[k] ?? 0}`;
    s += ";";
  }
  return s;
}
