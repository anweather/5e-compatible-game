// Spell golf (docs/BACKLOG.md 24): one hole, a big ball on a tee, hit with a spell's force (spells.json force: Gust of
// Wind pushes it, Raise Earth under it throws it up and along), no new element. The C# core's World.Golf.cs ports
// this line for line. startGolf puts the ball on the tee; each tick (after the boulders) a ball with way on it flies or
// rolls a cell when its time comes: in the air it carries over water and hills, on the ground it rolls on, harder down
// a slope and slower up one, bounces back off what blocks it (rock, a raised wall) and runs on downhill when it slows.
// Water or leaving the course puts it back on the tee for a stroke's penalty; rolling onto the hole drops it in. Every
// cast that moves it is a stroke. All whole numbers.
import { RULES, type SpellDef } from "./rules.ts";
import { DIR_NAMES, DX, DY, NO_LAND, type World, cellAt, elevation, idx, inBounds, isBlocking, log, logOnce } from "./world.ts";

/** sim.json's golf: the ball moves a cell every `every` ticks, rolls pushRoll cells a point of push, flies throwCarry a point of throw; margin is the course's room round the tee and the hole (C#: GolfParams). */
export interface GolfParams {
  every: number;
  pushRoll: number;
  throwCarry: number;
  margin: number;
}

/** The hole being played (C#: GolfState): tee, hole, the ball, strokes, whether it's in, and the way on it (dir, cells to fly then to roll, age). */
export interface Golf {
  teeX: number;
  teeY: number;
  holeX: number;
  holeY: number;
  x: number;
  y: number;
  strokes: number;
  holed: boolean;
  dir: number;
  roll: number;
  carry: number;
  age: number;
}

const params = (): GolfParams => ({ every: 1, pushRoll: 5, throwCarry: 3, margin: 16, ...RULES.sim.golf });
const moving = (g: Golf) => !g.holed && (g.roll > 0 || g.carry > 0);
const inWater = (c: { water: number; frozen: boolean }) => c.water > 0 && !c.frozen;

/** Puts the ball on the tee (World.ApplyGolf): a new round, again after holing out. */
export function startGolf(w: World, teeX: number, teeY: number, holeX: number, holeY: number): void {
  if (!inBounds(w, teeX, teeY) || !inBounds(w, holeX, holeY)) return;
  w.golf = { teeX, teeY, holeX, holeY, x: teeX, y: teeY, strokes: 0, holed: false, dir: -1, roll: 0, carry: 0, age: 0 };
  log(w, "The ball is on the tee: hole it in as few strokes as you can", "notable");
}

/**
 * A spell's force on the ball at (x, y) (World.HitBall): its push sets it rolling pushRoll cells a point along dir, its
 * throw sends it flying throwCarry cells a point and rolling a cell a point after. A stroke.
 */
export function hitBall(w: World, s: SpellDef, x: number, y: number, dir: number | undefined): void {
  const g = w.golf;
  if (!g || g.holed || g.x !== x || g.y !== y || dir === undefined || dir < 0) return;
  const p = params(), push = s.force?.push ?? 0, thrown = s.force?.throw ?? 0;
  const roll = push * p.pushRoll + thrown, carry = thrown * p.throwCarry;
  if (roll <= 0 && carry <= 0) return;
  Object.assign(g, { dir, roll, carry, age: 0 });
  g.strokes++;
  log(w, `Stroke ${g.strokes}: the ${s.name.toLowerCase()} sends the ball ${DIR_NAMES[dir]}`);
}

/** The ball's tick (World.TickGolf): a cell flown or rolled when its time comes. */
export function tickGolf(w: World): void {
  const g = w.golf;
  if (!g || !moving(g) || ++g.age % Math.max(1, params().every) !== 0) return;
  if (g.carry > 0) fly(w, g);
  else rollBall(w, g);
}

// A cell through the air: over water and hills, back off what blocks it; where it comes down it may land in the water
// or (on the hole) drop in.
function fly(w: World, g: Golf): void {
  const nx = g.x + DX[g.dir]!, ny = g.y + DY[g.dir]!;
  if (!onCourse(w, g, nx, ny)) return loseBall(w, g, "out of bounds");
  if (isBlocking(cellAt(w, nx, ny)!)) g.dir = (g.dir + 2) % 4;
  else {
    g.x = nx;
    g.y = ny;
  }
  if (--g.carry === 0) arrive(w, g);
}

// A cell along the ground: climbing costs a cell of roll more for each step (or part) it rises, a drop gives one back
// for each whole step; what blocks it bounces it back at a cell's cost. Run out on a slope, it rolls on down the
// steepest way.
function rollBall(w: World, g: Golf): void {
  const nx = g.x + DX[g.dir]!, ny = g.y + DY[g.dir]!;
  if (!onCourse(w, g, nx, ny)) return loseBall(w, g, "out of bounds");
  if (isBlocking(cellAt(w, nx, ny)!)) {
    g.dir = (g.dir + 2) % 4;
    g.roll--;
    logOnce(w, `bounce#${nx}:${ny}`, "The ball bounces back off the rock");
  } else {
    const step = RULES.sim.water.unitsPerStep, rise = ground(w, nx, ny) - ground(w, g.x, g.y);
    const cost = rise > 0 ? 1 + Math.floor((rise + step - 1) / step) : 1;
    if (cost > g.roll) g.roll = 0;
    else {
      g.x = nx;
      g.y = ny;
      g.roll += -cost + (rise < 0 ? Math.floor(-rise / step) : 0);
      if (arrive(w, g)) return;
    }
  }
  if (g.roll <= 0) downhill(w, g);
}

// Where the ball comes down or rolls onto: water loses it, the hole holes it. True when either did.
function arrive(w: World, g: Golf): boolean {
  if (inWater(cellAt(w, g.x, g.y)!)) {
    loseBall(w, g, "in the water");
    return true;
  }
  if (g.x !== g.holeX || g.y !== g.holeY) return false;
  g.holed = true;
  g.roll = g.carry = 0;
  log(w, g.strokes === 1 ? "Holed in one!" : `Holed in ${g.strokes}!`, "notable");
  return true;
}

// A ball that slows on a slope rolls on a cell down the steepest way (N, E, S, W on a tie); on the level it rests.
function downhill(w: World, g: Golf): void {
  g.roll = 0;
  let best = -1, bestLevel = ground(w, g.x, g.y);
  for (let dir = 0; dir < 4; dir++) {
    const nx = g.x + DX[dir]!, ny = g.y + DY[dir]!;
    if (!onCourse(w, g, nx, ny) || isBlocking(cellAt(w, nx, ny)!)) continue;
    const level = ground(w, nx, ny);
    if (level >= bestLevel) continue;
    best = dir;
    bestLevel = level;
  }
  if (best < 0) return;
  g.dir = best;
  g.roll = 1;
}

function loseBall(w: World, g: Golf, where: string): void {
  g.x = g.teeX;
  g.y = g.teeY;
  g.roll = g.carry = 0;
  g.strokes++;
  log(w, `The ball is ${where}: back on the tee, a stroke's penalty`, "notable");
}

function onCourse(w: World, g: Golf, x: number, y: number): boolean {
  const m = params().margin;
  return inBounds(w, x, y)
    && x >= Math.min(g.teeX, g.holeX) - m && x <= Math.max(g.teeX, g.holeX) + m
    && y >= Math.min(g.teeY, g.holeY) - m && y <= Math.max(g.teeY, g.holeY) + m;
}

/** The ground's level at (x, y) in water units: the cell's elevation and the land's hills under it (World.GroundUnits). */
export function ground(w: World, x: number, y: number): number {
  const land = w.land?.[idx(w, x, y)];
  return elevation(cellAt(w, x, y)!) + (land !== undefined && land !== NO_LAND ? land : 0);
}

/** The hole as text, "" before anyone teed off (World.GolfText). */
export function golfState(w: World): string {
  const g = w.golf;
  return g ? `|g${[g.x, g.y, g.strokes, g.holed ? 1 : 0, g.dir, g.roll, g.carry, g.age].join(":")}` : "";
}
