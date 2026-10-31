// What the player does: cast a spell, place a critter, paint the map.
import { RULES, hasComponent, type CellCond, type SpellDef } from "./rules.ts";
import { applyElement, applyEntityElement, displaceWater, ignite, matches, putCover, shatter, shoveGas } from "./react.ts";
import { awayDir, damage, defOf, destroy, isLevitating, massOf, push, spawn } from "./entities.ts";
import { emitCritter } from "./sim.ts";
import type { WeatherKind } from "./sky.ts";
import { hitBall } from "./golf.ts";
import { crater, terraform } from "./terraform.ts";
import {
  type Boulder, type Dir, type Entity, type Star, type Wave, type Whirl, type World,
  DX, DY, addGas, burns, cellAt, elevation, critterAt, dirFrom, entityAt, idx, inBounds, isBlocking, lineOfSight, log, logOnce, newCell, playerOf, rand, roll, windDir,
} from "./world.ts";

export const spellById = (id: string) => RULES.spells.find((s) => s.id === id);

/** Cells a spell would touch, in the order it touches them (for previews too). */
/** The most a cast can be charged (held before release): 1 is a tap. */
export const MAX_CHARGE = 3;
export const clampCharge = (charge: number) => Math.max(1, Math.min(MAX_CHARGE, Math.floor(charge)));
/** A line spell's length at a charge: half again for each level past a tap. */
export const chargedLength = (length: number, charge: number) => length + Math.floor((length * (clampCharge(charge) - 1)) / 2);
/**
 * How many squares a charged spot, square or burst grows by: one for each level past a tap.
 * Earth shaping digs or raises more steps instead, so its patch stays the same (World.ChargedGrowth).
 */
export const chargedGrowth = (s: SpellDef, charge: number) => (s.effect === "terraform" ? 0 : clampCharge(charge) - 1);

/**
 * An aim (a whole-number vector, x east and y south) snapped to the four ways
 * when it's within about 5 degrees of one, so a nearly straight line comes
 * out straight (World.SnapAim).
 */
export function snapAim(ax: number, ay: number): [number, number] {
  if (Math.abs(ay) * 11 < Math.abs(ax)) return [Math.sign(ax), 0];
  if (Math.abs(ax) * 11 < Math.abs(ay)) return [0, Math.sign(ay)];
  return [ax, ay];
}

/**
 * Squares a spell touches. A line or cone points along dir, or along an aim off
 * the four ways when one is given (World.SpellCells).
 */
export function spellCells(w: World, s: SpellDef, x: number, y: number, dir: Dir, charge = 1, aim?: [number, number]): [number, number][] {
  const out: [number, number][] = [];
  // The time spells (Hasten Time, Control Weather) change the sky, not squares.
  if (isSkySpell(s)) return out;
  const add = (cx: number, cy: number) => inBounds(w, cx, cy) && out.push([cx, cy]);
  const [ax, ay] = snapAim(aim?.[0] ?? 0, aim?.[1] ?? 0);
  const slant = ax !== 0 && ay !== 0;
  if (slant && s.shape === "line") return slantCells(w, s, x, y, dir, chargedLength(s.length ?? 5, charge), ax, ay);
  if (slant && s.shape === "cone") return slantCone(w, x, y, chargedLength(s.length ?? 5, charge), ax, ay);
  switch (s.shape) {
    case "point": {
      // Charged, it spreads to every square within a step (or two) of it, a diamond, row by row.
      const r = chargedGrowth(s, charge);
      for (let dy = -r; dy <= r; dy++) {
        const span = r - Math.abs(dy);
        for (let dx = -span; dx <= span; dx++) add(x + dx, y + dy);
      }
      break;
    }
    case "target":
      add(x, y);
      break;
    case "line": {
      // A terraform line from the caster (Earthen Trench) starts under their feet, one
      // square behind the aim, so a trench cut from a pond's bank opens into the pond.
      const from = s.effect === "terraform" && s.origin === "self" ? -1 : 0;
      for (let k = from; k < from + chargedLength(s.length ?? 5, charge); k++) {
        const cx = x + DX[dir] * k, cy = y + DY[dir] * k;
        const c = cellAt(w, cx, cy);
        if (!c && k < 0) continue;
        if (!c) break;
        out.push([cx, cy]);
        if (isBlocking(c)) break;
      }
      break;
    }
    case "cone": {
      // From the square in front of the caster, widening a square each side per square out
      // (a 15 ft cone: 1, 3, then 5 squares); it washes over walls.
      const len = chargedLength(s.length ?? 5, charge), px = DY[dir], py = DX[dir];
      for (let k = 0; k < len; k++) for (let o = -k; o <= k; o++) add(x + DX[dir] * k + px * o, y + DY[dir] * k + py * o);
      break;
    }
    case "square": {
      const n = (s.size ?? 2) + chargedGrowth(s, charge), off = Math.floor((n - 1) / 2);
      for (let dy = 0; dy < n; dy++) for (let dx = 0; dx < n; dx++) add(x - off + dx, y - off + dy);
      break;
    }
    case "burst": {
      const r = (s.radius ?? 1) + chargedGrowth(s, charge);
      for (let dy = -r; dy <= r; dy++) for (let dx = -r; dx <= r; dx++) add(x + dx, y + dy);
      break;
    }
  }
  return out;
}

/** A slanted line's next cell: added to out if it is in the world; true while the line goes on (it isn't blocking) (World.VisitSlant). */
function visitSlant(w: World, cx: number, cy: number, out: [number, number][]): boolean {
  const c = cellAt(w, cx, cy);
  if (!c) return false;
  out.push([cx, cy]);
  return !isBlocking(c);
}

/**
 * A line along a slanted aim: from (x, y) (a trench from the caster's square
 * behind it first) out to length squares as the crow flies, up to its first
 * blocking square. Each square shares a side with the one before, turning a
 * corner through the square along the aim's longer part (World.SlantCells).
 */
function slantCells(w: World, s: SpellDef, x: number, y: number, dir: Dir, length: number, ax: number, ay: number): [number, number][] {
  const out: [number, number][] = [];
  if (s.effect === "terraform" && s.origin === "self" && cellAt(w, x - DX[dir], y - DY[dir]) && !visitSlant(w, x - DX[dir], y - DY[dir], out)) return out;
  const alongX = Math.abs(ax) >= Math.abs(ay), major = Math.max(Math.abs(ax), Math.abs(ay));
  const reach = length * length * major * major, size = ax * ax + ay * ay;
  let px = x, py = y;
  for (let k = 0; k * k * size < reach; k++) {
    const cx = x + roundDiv(k * ax, major), cy = y + roundDiv(k * ay, major);
    if (k > 0 && cx !== px && cy !== py && !visitSlant(w, alongX ? cx : px, alongX ? py : cy, out)) return out;
    if (!visitSlant(w, cx, cy, out)) return out;
    [px, py] = [cx, cy];
  }
  return out;
}

/** A cone along a slanted aim from its apex: every square within 45 degrees of it and nearer than length along it, in row order. */
function slantCone(w: World, x: number, y: number, length: number, ax: number, ay: number): [number, number][] {
  const out: [number, number][] = [];
  const reach = length * length * (ax * ax + ay * ay);
  for (let dy = -length; dy <= length; dy++) {
    for (let dx = -length; dx <= length; dx++) {
      const along = dx * ax + dy * ay, side = Math.abs(dx * ay - dy * ax);
      if (along >= 0 && side <= along && along * along < reach && inBounds(w, x + dx, y + dy)) out.push([x + dx, y + dy]);
    }
  }
  return out;
}

/** n / d (d > 0) rounded to the nearest whole number, halves up. */
const roundDiv = (n: number, d: number) => Math.floor((2 * n + d) / (2 * d));

/**
 * The player's cast: a click on (tx,ty) with the wizard as the caster.
 * Beams and Gust start next to the wizard and point at the click; Thunderwave
 * goes off around the wizard; everything else lands on the clicked square.
 * Returns a message when the cast can't happen.
 */
export function castAt(w: World, id: string, tx: number, ty: number, owner?: string): string | null {
  const s = spellById(id);
  if (!s) return null;
  const aim = aimSpell(w, id, tx, ty, owner);
  if ("error" in aim) return aim.error;
  if (s.effect === "teleport") return mistyStep(w, s, tx, ty, owner);
  castSpell(w, id, aim.x, aim.y, aim.dir, aim.casterId, 1, aim.aim);
  return null;
}

type Aim = { x: number; y: number; dir: Dir; casterId?: number; aim?: [number, number] } | { error: string };

/** Where a click would put the spell: shared by castAt and the hover preview. */
export function aimSpell(w: World, id: string, tx: number, ty: number, owner?: string): Aim {
  const s = spellById(id);
  if (!s) return { error: "That spell isn't in the rules any more." };
  const p = playerOf(w, owner);
  // A line or cone points from the wizard toward the click, off the four ways if the click is.
  const aim: [number, number] | undefined = p && (s.shape === "line" || s.shape === "cone") ? [tx - p.x, ty - p.y] : undefined;
  if (s.origin !== "self" && s.effect !== "teleport") return { x: tx, y: ty, dir: p ? (dirFrom(p.x, p.y, tx, ty) ?? 1) : 1, aim };
  if (!p) return { error: "You're down. Press Reset to try again." };
  if (s.effect === "teleport") return { x: tx, y: ty, dir: 1 };
  if (s.shape === "burst") return { x: p.x, y: p.y, dir: dirFrom(p.x, p.y, tx, ty) ?? 1, casterId: p.id };
  const dir = dirFrom(p.x, p.y, tx, ty);
  if (dir === null) return { error: `Click a square away from the wizard to point ${s.name} that way.` };
  return { x: p.x + DX[dir], y: p.y + DY[dir], dir, casterId: p.id, aim };
}

/** Squares a click would touch, for the hover preview. */
export function previewCells(w: World, id: string, tx: number, ty: number, owner?: string): [number, number][] {
  const s = spellById(id);
  if (!s) return [];
  const aim = aimSpell(w, id, tx, ty, owner);
  if ("error" in aim) return [];
  const cells = spellCells(w, s, aim.x, aim.y, aim.dir, 1, aim.aim);
  return s.shape === "burst" && s.origin === "self" ? cells.filter(([x, y]) => x !== aim.x || y !== aim.y) : cells;
}

function mistyStep(w: World, s: SpellDef, tx: number, ty: number, owner?: string): string | null {
  const p = playerOf(w, owner)!;
  const range = s.range ?? 6;
  if (Math.max(Math.abs(tx - p.x), Math.abs(ty - p.y)) > range) return `Too far. Misty Step reaches ${range * 5} ft (${range} squares).`;
  const c = cellAt(w, tx, ty);
  if (!c || isBlocking(c) || entityAt(w, tx, ty)) return "Misty Step needs an empty square to land on.";
  if (!lineOfSight(w, p.x, p.y, tx, ty)) return "You can't see that square. Walls block sight; gate bars don't.";
  p.x = tx;
  p.y = ty;
  delete p.status.fallen;
  log(w, "You cast Misty Step and reappear in a swirl of silver mist", "notable");
  return null;
}

/**
 * A charged cast (held: charge 2 or 3) is that many times as strong, raises or digs
 * that many times the steps, a line reaches half again as far per level, and a
 * spot, square or burst grows a square (chargedGrowth)
 * (World.Cast in the C# core).
 */
/** The spell a meteor lands as (World.MeteorSpell). */
export const METEOR_SPELL = "meteor";

export function castSpell(w: World, id: string, x: number, y: number, dir: Dir, casterId?: number, charge = 1, aim?: [number, number]): void {
  const s = spellById(id);
  if (!s || !inBounds(w, x, y)) return;
  if (s.effect === "levitate") return levitate(w, s, x, y);
  if (s.effect === "carry") return; // two taps; see carry()
  log(w, `You cast ${s.name}`, "info");
  if (s.effect === "haste") hastenTime(w, s, clampCharge(charge));
  else if (s.effect === "weather") controlWeather(w, s, clampCharge(charge));
  else if (s.effect === "light") lightUp(w, s, x, y);
  else if (s.delay) aimStar(w, s, x, y, dir, clampCharge(charge));
  else if (isWave(s)) startWave(w, s, x, y, dir, clampCharge(charge));
  else if (isBoulder(s)) startBoulder(w, s, x, y, dir, clampCharge(charge));
  else landSpell(w, s, x, y, dir, casterId, clampCharge(charge), aim);
}

/**
 * A delayed cast (Falling Star, World.AimStar): the cells round where it will land wear a target mark (Cell star)
 * until it lands, delay ticks on (landStars).
 */
function aimStar(w: World, s: SpellDef, x: number, y: number, dir: Dir, charge: number): void {
  const star: Star = { spell: s.id, x, y, dir, charge, radius: (s.radius ?? 1) + chargedGrowth(s, charge), lands: w.tick + s.delay! };
  w.stars.push(star);
  markStar(w, star, 1);
}

function markStar(w: World, star: Star, by: number): void {
  const r = star.radius;
  for (let y = star.y - r; y <= star.y + r; y++) {
    for (let x = star.x - r; x <= star.x + r; x++) {
      const c = cellAt(w, x, y);
      if (!c) continue;
      const n = Math.max(0, (c.star ?? 0) + by);
      if (n > 0) c.star = n;
      else delete c.star;
    }
  }
}

/** Stars whose tick has come land, in the order cast (World.LandStars). */
export function landStars(w: World): void {
  if (!w.stars.length) return;
  for (const star of w.stars) {
    if (star.lands > w.tick) continue;
    markStar(w, star, -1);
    const s = spellById(star.spell);
    if (!s) continue;
    log(w, `${s.name} strikes`, "notable");
    landSpell(w, s, star.x, star.y, star.dir, undefined, star.charge);
  }
  w.stars = w.stars.filter((star) => star.lands > w.tick);
}

/** A spell's burning rock (World.Scatter): each try lays the cover, alight, on open dry ground off the middle. */
function scatter(w: World, sc: SpellDef["scatter"], x: number, y: number): void {
  if (!sc) return;
  const span = sc.reach * 2 + 1;
  for (let i = 0; i < sc.count; i++) {
    const cx = x + Math.floor(rand(w) * span) - sc.reach;
    const cy = y + Math.floor(rand(w) * span) - sc.reach;
    const c = cellAt(w, cx, cy);
    if ((cx === x && cy === y) || !c || isBlocking(c) || c.water > 0) continue;
    putCover(c, sc.cover);
    ignite(c, "cover");
  }
}

/**
 * Light (SRD 5.2.1): the object touched sheds bright light for the spell's duration. The core keeps the ticks on
 * its cell (lit, counting down each tick); the view draws the spell's light { bright, dim } round it (World.LightUp).
 */
function lightUp(w: World, s: SpellDef, x: number, y: number) {
  const c = cellAt(w, x, y)!;
  c.lit = Math.max(c.lit ?? 0, s.duration ?? 40);
}

/** Whether a spell changes the sky for everyone (Hasten Time, Control Weather) rather than squares. */
export const isSkySpell = (s: SpellDef) => s.effect === "haste" || s.effect === "weather";

/** The weathers Control Weather steps through, a step a cast; from anything else it clears first. */
export const WEATHER_CYCLE: WeatherKind[] = ["clear", "rain", "snow", "storm"];

/**
 * Hasten Time (World.Hasten). While it runs, casting it again ends it, keeping the hours gained.
 * Otherwise the clock runs `rate` times as fast for `duration` ticks, half again per charge level.
 */
function hastenTime(w: World, s: SpellDef, charge: number) {
  if (w.haste) {
    w.haste = null;
    log(w, "Time settles back to its own pace", "notable");
    return;
  }
  w.haste = { rate: Math.max(1, s.rate ?? 1), ticks: chargedLength(Math.max(1, s.duration ?? 40), charge) };
  log(w, "Time races: the sun hurries across the sky", "notable");
}

/** Control Weather (World.HoldWeather): the next weather in WEATHER_CYCLE after the one in force, held for `duration` ticks, half again per charge level. */
function controlWeather(w: World, s: SpellDef, charge: number) {
  const now = w.weatherSet ? w.weatherSet.kind : w.weather.kind;
  const next = WEATHER_CYCLE[(WEATHER_CYCLE.indexOf(now) + 1) % WEATHER_CYCLE.length]!;
  w.weatherSet = { kind: next, from: w.tick, until: w.tick + chargedLength(Math.max(1, s.duration ?? 40), charge) };
  log(w, next === "clear" ? "The sky clears at your word" : `The sky turns to ${next} at your word`, "notable");
}

/** The direction from (dx, dy) off a burst's middle back toward it, none at the middle: along the longer axis, east or west on a tie (World.InwardDir). */
function inwardDir(dx: number, dy: number): Dir | undefined {
  if (dx === 0 && dy === 0) return undefined;
  if (Math.abs(dx) >= Math.abs(dy)) return dx > 0 ? 3 : 1;
  return dy > 0 ? 0 : 2;
}

/**
 * The next cell clockwise round the square ring d out from a whirlwind's middle, from (dx, dy) on it (World.RingNext):
 * east along the top, south down the right, west along the bottom, north up the left.
 */
function ringNext(dx: number, dy: number, d: number): Dir {
  if (dy === -d && dx < d) return 1;
  if (dx === d && dy < d) return 2;
  if (dy === d && dx > -d) return 3;
  return 0;
}

/**
 * Each whirlwind, in the order cast (World.TickWhirlwinds): every so many ticks it whirls once (spin), every so many it
 * moves a cell (travel), and once its ticks are up it drops what it carries and is gone.
 */
export function tickWhirlwinds(w: World): void {
  for (const ww of w.whirls) {
    if (++ww.age % ww.every === 0) spin(w, ww);
    if (ww.travel > 0 && ww.age % ww.travel === 0) travel(w, ww);
  }
  for (const ww of w.whirls) if (ww.age >= ww.ticks) dropCarried(w, ww);
  w.whirls = w.whirls.filter((ww) => ww.age < ww.ticks);
}

/** A cell with the wind, or in still air the way it was cast; a wall holds it up (World.Travel). */
function travel(w: World, ww: Whirl): void {
  const wind = windDir(w), dir = wind >= 0 ? (wind as Dir) : ww.dir;
  const n = cellAt(w, ww.x + DX[dir], ww.y + DY[dir]);
  if (!n || isBlocking(n)) return;
  ww.x += DX[dir];
  ww.y += DY[dir];
}

/** How far from its eye a whirlwind takes things on and sheds what it carries, in cells (World.WhirlEye). */
const WHIRL_EYE = 1;

const carriedBy = (ww: Whirl) => (ww.carrying === null ? undefined : spellById(ww.spell)?.carries?.find((c) => c.id === ww.carrying));

/**
 * The first of its spell's carries (strongest first) that the eye is over, stopping at what it already carries:
 * a fire's smoke reaches it first, and the fire still makes it a fire tornado (World.TakeOn).
 */
function takeOn(w: World, ww: Whirl): void {
  for (const carry of spellById(ww.spell)?.carries ?? []) {
    if (carry.id === ww.carrying) return;
    if (!eyeOver(w, ww, carry.if)) continue;
    ww.carrying = carry.id;
    if (carry.log) logOnce(w, `carry#${carry.id}`, carry.log, "notable");
    return;
  }
}

function eyeOver(w: World, ww: Whirl, cond: CellCond | undefined): boolean {
  for (let dy = -WHIRL_EYE; dy <= WHIRL_EYE; dy++) {
    for (let dx = -WHIRL_EYE; dx <= WHIRL_EYE; dx++) {
      const c = cellAt(w, ww.x + dx, ww.y + dy);
      if (c && matches(c, cond)) return true;
    }
  }
  return false;
}

/** Applies what it carries over the square of cells within reach of its eye, row by row (World.Shed). */
function shed(w: World, ww: Whirl, element: string, reach: number, power: number): void {
  for (let dy = -reach; dy <= reach; dy++) {
    for (let dx = -reach; dx <= reach; dx++) applyElement(w, ww.x + dx, ww.y + dy, element, { power, direct: false });
  }
}

function dropCarried(w: World, ww: Whirl): void {
  const carry = carriedBy(ww);
  if (!carry) return;
  logOnce(w, `drop#${carry.id}`, `The ${carry.name} dies out and drops what it carried`, "notable");
  shed(w, ww, carry.sheds, ww.radius, spellById(ww.spell)?.dropPower ?? 1);
}

/**
 * One turn of a whirlwind (World.Spin), over the square of cells round its middle: fire burning on a ring lights the
 * next cell clockwise round it; gas moves a cell in toward the middle, the inner rings first, each gas once;
 * entities of maxMass or less, nearest first, step a cell in where it's open. Loose props move here but in the view
 * in the Unity game, where the C# core only drags creatures; the player, who that core doesn't hold, stays put.
 */
function spin(w: World, ww: Whirl): void {
  const r = ww.radius;
  const lit: [number, number, Dir][] = [];
  for (let dy = -r; dy <= r; dy++) {
    for (let dx = -r; dx <= r; dx++) {
      const d = Math.max(Math.abs(dx), Math.abs(dy));
      if (d > 0 && (cellAt(w, ww.x + dx, ww.y + dy)?.fire ?? 0) > 0) lit.push([ww.x + dx, ww.y + dy, ringNext(dx, dy, d)]);
    }
  }
  for (const [bx, by, dir] of lit) applyElement(w, bx + DX[dir], by + DY[dir], "fire", { power: 1, direct: false });
  whirlGas(w, ww);
  whirlEntities(w, ww);
  takeOn(w, ww);
  const carry = carriedBy(ww);
  if (carry) shed(w, ww, carry.sheds, WHIRL_EYE, 1);
}

function whirlGas(w: World, ww: Whirl): void {
  const moved = new Set<number>();
  for (let d = 1; d <= ww.radius; d++) {
    for (let dy = -d; dy <= d; dy++) {
      for (let dx = -d; dx <= d; dx++) {
        const cx = ww.x + dx, cy = ww.y + dy;
        if (Math.max(Math.abs(dx), Math.abs(dy)) !== d || !inBounds(w, cx, cy) || cellAt(w, cx, cy)!.gas === null || moved.has(idx(w, cx, cy))) continue;
        const dir = inwardDir(dx, dy)!;
        if (shoveGas(w, cx, cy, dir)) moved.add(idx(w, cx + DX[dir], cy + DY[dir]));
      }
    }
  }
}

function whirlEntities(w: World, ww: Whirl): void {
  const ring = (e: Entity) => Math.max(Math.abs(e.x - ww.x), Math.abs(e.y - ww.y));
  const pulled = w.entities
    .filter((e) => ring(e) > 0 && ring(e) <= ww.radius && !defOf(e).player && massOf(e) <= ww.maxMass)
    .sort((a, b) => ring(a) - ring(b) || a.id - b.id);
  for (const e of pulled) {
    const dir = inwardDir(e.x - ww.x, e.y - ww.y)!;
    const nx = e.x + DX[dir], ny = e.y + DY[dir];
    const n = cellAt(w, nx, ny);
    if (!n || isBlocking(n) || entityAt(w, nx, ny)) continue;
    e.x = nx;
    e.y = ny;
  }
}

/** Whether a spell rolls out as a wave (a line with travelEvery that doesn't whirl) rather than landing at once (World.IsWave). */
export const isWave = (s: SpellDef) => !!s.travelEvery && !s.inward && !s.rolls && s.shape === "line";

/**
 * A wave's cast (World.StartWave): the front starts on the cast's square, a line across the way it rolls, width cells
 * to each side, and washes over it; it rolls one cell every travelEvery ticks, to the line's length in all (tickWaves).
 */
function startWave(w: World, s: SpellDef, x: number, y: number, dir: Dir, charge: number): void {
  const wv: Wave = {
    spell: s.id, x, y, dir, width: (s.radius ?? 1) + chargedGrowth(s, charge), left: chargedLength(s.length ?? 5, charge) - 1,
    every: s.travelEvery!, age: 0, power: (s.power ?? 1) * charge, maxMass: s.maxMass ?? 1,
  };
  wash(w, wv, s);
  if (wv.left > 0) w.waves.push(wv);
  else breakWave(w, wv, s);
}

/** Each wave, in the order cast, rolls a cell when its time comes; against a wall or its length's end it breaks and is gone (World.TickWaves). */
export function tickWaves(w: World): void {
  if (!w.waves.length) return;
  for (const wv of w.waves) {
    if (++wv.age % wv.every !== 0) continue;
    const s = spellById(wv.spell);
    const nx = wv.x + DX[wv.dir], ny = wv.y + DY[wv.dir];
    const n = cellAt(w, nx, ny);
    if (s && n && !isBlocking(n)) {
      wv.x = nx;
      wv.y = ny;
      wv.left--;
      wash(w, wv, s);
    } else wv.left = 0;
    if (wv.left <= 0) breakWave(w, wv, s);
  }
  w.waves = w.waves.filter((wv) => wv.left > 0);
}

/** The open cells of a wave's front, from one side to the other (World.Front). */
function front(w: World, wv: Wave): [number, number][] {
  const out: [number, number][] = [];
  const px = DY[wv.dir], py = DX[wv.dir];
  for (let o = -wv.width; o <= wv.width; o++) {
    const cx = wv.x + px * o, cy = wv.y + py * o;
    const c = cellAt(w, cx, cy);
    if (c && !isBlocking(c)) out.push([cx, cy]);
  }
  return out;
}

/**
 * The wave's element lands on its front (water at power 0: it douses fires and soaks what it meets but leaves no
 * water), then its force knocks each thing there light enough on ahead of it, where that's open (World.Wash).
 */
function wash(w: World, wv: Wave, s: SpellDef): void {
  const cells = front(w, wv);
  for (const [cx, cy] of cells) applyElement(w, cx, cy, s.element!, { power: wv.power, dir: wv.dir, direct: true });
  for (const [cx, cy] of cells) force(w, s, cx, cy, wv.dir);
}

/** Where it stops it breaks, and its leaves (mud) lands over the front (World.Break). */
function breakWave(w: World, wv: Wave, s: SpellDef | undefined): void {
  wv.left = 0;
  if (!s) return;
  logOnce(w, `wave#${s.id}`, `The ${s.name.toLowerCase()} breaks`, "info");
  if (!s.leaves) return;
  for (const [cx, cy] of front(w, wv)) applyElement(w, cx, cy, s.leaves, { power: 1, direct: true });
}

/** Whether a spell rolls out as a boulder (a line with travelEvery that rolls) rather than landing at once (World.IsBoulder). */
export const isBoulder = (s: SpellDef) => !!s.travelEvery && !!s.rolls && s.shape === "line";

/**
 * A boulder's cast (World.StartBoulder): it starts on the cast's square and strikes it, then rolls a cell every
 * travelEvery ticks, to the line's length in all (tickBoulders).
 */
function startBoulder(w: World, s: SpellDef, x: number, y: number, dir: Dir, charge: number): void {
  const c = cellAt(w, x, y);
  if (!c || isBlocking(c)) return;
  const b: Boulder = { spell: s.id, x, y, dir, left: chargedLength(s.length ?? 5, charge) - 1, every: s.travelEvery!, age: 0 };
  strike(w, b, s);
  if (b.left > 0 && !inWater(c)) w.boulders.push(b);
  else rest(w, b, s);
}

/**
 * Each boulder, in the order cast, rolls a cell when its time comes, the way the ground falls (rollTo), and strikes
 * it; once it can't roll on, reaches water or its length's end, it comes to rest and is gone (World.TickBoulders).
 */
export function tickBoulders(w: World): void {
  if (!w.boulders.length) return;
  for (const b of w.boulders) {
    if (++b.age % b.every !== 0) continue;
    const s = spellById(b.spell);
    const dir = s ? rollTo(w, b) : -1;
    if (dir >= 0) {
      b.dir = dir as Dir;
      b.x += DX[b.dir];
      b.y += DY[b.dir];
      b.left--;
      strike(w, b, s!);
      if (inWater(cellAt(w, b.x, b.y)!)) b.left = 0;
    } else b.left = 0;
    if (b.left <= 0) rest(w, b, s);
  }
  w.boulders = w.boulders.filter((b) => b.left > 0);
}

const inWater = (c: { water: number; frozen: boolean }) => c.water > 0 && !c.frozen;

/**
 * Where a boulder rolls next: of the open cells ahead, to its left and to its right (in that order), the lowest that
 * isn't higher than where it is; -1 when every way is uphill or shut (World.RollTo).
 */
function rollTo(w: World, b: Boulder): number {
  let best = -1, bestLevel = elevation(cellAt(w, b.x, b.y)!);
  for (const dir of [b.dir, (b.dir + 3) % 4, (b.dir + 1) % 4] as Dir[]) {
    const n = cellAt(w, b.x + DX[dir], b.y + DY[dir]);
    if (!n || isBlocking(n)) continue;
    const level = elevation(n);
    if (level < bestLevel || (level === bestLevel && best < 0)) {
      best = dir;
      bestLevel = level;
    }
  }
  return best;
}

/**
 * The boulder strikes the cell it rolls onto: what is brittle there cracks or shatters, it leaves its trail of gas,
 * and the thing there (not the player, whom the C# core doesn't hold) takes its damage and is knocked over (World.Strike).
 */
function strike(w: World, b: Boulder, s: SpellDef): void {
  shatter(w, b.x, b.y);
  if (s.trail) addGas(cellAt(w, b.x, b.y)!, s.trail, 2);
  const e = entityAt(w, b.x, b.y);
  if (!e || defOf(e).player) return;
  if (s.damage) damage(w, e, roll(w, s.damage), s.name.toLowerCase(), s.element);
  force(w, s, b.x, b.y, b.dir);
}

/**
 * A spell's force on what stands at (x, y), not the player (World.Force; spells.json force): pushed force.push cells
 * along dir where that's open, if it's maxMass or lighter (a wave knocks it back), and thrown off its feet for
 * force.throw ticks (a geyser's jet, a boulder).
 */
function force(w: World, s: SpellDef, x: number, y: number, dir: Dir | undefined, once?: Set<number>): void {
  const f = s.force;
  if (!f) return;
  hitBall(w, s, x, y, dir);
  const e = entityAt(w, x, y);
  if (!e || defOf(e).player || !w.entities.includes(e)) return;
  if (once) {
    if (once.has(e.id)) return;
    once.add(e.id);
  }
  const by = s.name.toLowerCase(), name = defOf(e).name;
  // One push each: a spell's force.push knocks back raiders (the C# core's only push), its push shoves everything
  // else (push() leaves raiders standing), so a thing Gust of Wind shoved isn't knocked back again.
  if ((f.push ?? 0) > 0 && (!s.push || defOf(e).raider) && dir !== undefined && massOf(e) <= (s.maxMass ?? 1)) {
    for (let k = 0; k < f.push!; k++) {
      const n = cellAt(w, e.x + DX[dir], e.y + DY[dir]);
      if (!n || isBlocking(n) || entityAt(w, e.x + DX[dir], e.y + DY[dir])) break;
      e.x += DX[dir];
      e.y += DY[dir];
    }
    logOnce(w, `pushed#${e.id}`, `The ${by} knocks ${name} back`, "notable");
  }
  if (!f.throw) return;
  e.status.thrown = Math.max(e.status.thrown ?? 0, f.throw);
  logOnce(w, `thrown#${e.id}`, `The ${by} throws ${name} off its feet`, "notable");
}

/** Where it stops it comes to rest, and its leaves (earth) lands there: a dam in a stream, a boulder on dry ground (World.Rest). */
function rest(w: World, b: Boulder, s: SpellDef | undefined): void {
  b.left = 0;
  if (!s) return;
  logOnce(w, `rest#${s.id}`, `The ${s.name.toLowerCase()} comes to rest`, "info");
  if (s.leaves) applyElement(w, b.x, b.y, s.leaves, { power: 1, direct: true });
}

/** The nearest cell within range (steps either way) of (x, y) matching if, as the crow flies, the first in row order on a tie (World.Seek). */
function seek(w: World, sk: NonNullable<SpellDef["seeks"]>, x: number, y: number): [number, number] | undefined {
  let best: [number, number] | undefined, bestD = Infinity;
  for (let dy = -sk.range; dy <= sk.range; dy++) {
    for (let dx = -sk.range; dx <= sk.range; dx++) {
      const d = dx * dx + dy * dy, c = cellAt(w, x + dx, y + dy);
      if (d >= bestD || !c || !matches(c, sk.if)) continue;
      best = [x + dx, y + dy];
      bestD = d;
    }
  }
  return best;
}

/** What a spell does where it lands, without the cast's log line (a meteor's landing too; World.Land). */
export function landSpell(w: World, s: SpellDef, x: number, y: number, dir: Dir, casterId: number | undefined, charge: number, aim?: [number, number]): void {
  let cells = spellCells(w, s, x, y, dir, charge, aim);
  // A spell that seeks (Lightning) strikes the nearest cell it seeks near where it's aimed instead of its shape.
  const sought = s.seeks ? seek(w, s.seeks, x, y) : undefined;
  if (sought) cells = [sought];
  // A spell that acts on what's already there (Geyser on water) lands only where that is, and fizzles where it isn't.
  if (s.needs) {
    cells = cells.filter(([cx, cy]) => matches(cellAt(w, cx, cy)!, s.needs));
    if (!cells.length) {
      log(w, s.fizzle ?? `${s.name} fizzles`, "notable");
      return;
    }
  }
  const ctx = { power: (s.power ?? 1) * charge, dir, direct: true, at: [x, y] as [number, number] };
  // A spell with wind (Gust of Wind, Flame Wave) sets the world's wind along its direction (World.Land).
  if (s.wind) w.wind = { dir, ticks: s.wind };
  if (s.inward && s.pullEvery && s.duration) {
    w.whirls.push({ x, y, radius: (s.radius ?? 1) + chargedGrowth(s, charge), ticks: s.duration, age: 0, every: s.pullEvery, maxMass: s.maxMass ?? 1, spell: s.id, dir, travel: s.travelEvery ?? 0, carrying: null });
  }
  if (s.effect === "terraform") terraform(w, s, cells, x, y, charge);
  if (s.crater) for (const [cx, cy] of cells) shatter(w, cx, cy);
  crater(w, x, y, s.crater);

  // Shove first (furthest first, so a line of goblins doesn't jam), then react.
  const forced = new Set<number>(); // each thing forced once a cast (World.Land)
  if (s.push) {
    const hit = cells
      .map(([cx, cy]) => entityAt(w, cx, cy))
      .filter((e): e is Entity => e !== undefined && e.id !== casterId)
      .sort((a, b) => (b.x - x) ** 2 + (b.y - y) ** 2 - ((a.x - x) ** 2 + (a.y - y) ** 2) || a.id - b.id);
    for (const e of hit) {
      if (s.damage) damage(w, e, roll(w, s.damage), s.name.toLowerCase(), s.element);
      if (!w.entities.includes(e)) continue;
      const d = s.shape === "burst" ? awayDir(e.x - x, e.y - y, dir) : dir;
      push(w, e, d, s.push, true);
    }
  }
  for (const [cx, cy] of cells) {
    if (casterId !== undefined && s.shape === "burst" && cx === x && cy === y) continue;
    if (s.element) applyElement(w, cx, cy, s.element, s.inward ? { ...ctx, dir: inwardDir(cx - x, cy - y) } : ctx);
    force(w, s, cx, cy, dir, forced);
    if (!s.push && s.damage) {
      const e = entityAt(w, cx, cy);
      if (e && e.id === casterId) continue;
      if (e && hasComponent(defOf(e), "creature")) {
        const mul = s.element === "lightning" ? applyEntityElement(w, e, "lightning", { ...ctx, direct: false }) : 1;
        damage(w, e, roll(w, s.damage) * mul, s.name.toLowerCase(), s.element);
      } else if (e && s.element === "fire" && hasComponent(defOf(e), "flammable")) {
        damage(w, e, roll(w, s.damage), "fire", "fire");
      }
    }
  }
  scatter(w, s.scatter, x, y);
  if (s.core) applyElement(w, x, y, s.core, { power: 1, direct: true });
}

function levitate(w: World, s: SpellDef, x: number, y: number) {
  const e = entityAt(w, x, y);
  if (!e) {
    log(w, "Levitate needs a target. Tap a creature or object.", "info");
    return;
  }
  e.status.levitate = s.duration ?? 40;
  delete e.status.fallen;
  log(w, `You cast Levitate. The ${defOf(e).name.toLowerCase()} floats up`, "info");
}

/** Mage Hand: lift a light thing at (fx,fy) and set it down at (tx,ty). */
export function carry(w: World, fx: number, fy: number, tx: number, ty: number): string | null {
  const s = spellById("mage-hand")!;
  const e = entityAt(w, fx, fy);
  if (!e) return "Nothing there to lift.";
  if (massOf(e) > (s.maxMass ?? 1)) return `The ${defOf(e).name.toLowerCase()} is too heavy for Mage Hand.`;
  if (Math.max(Math.abs(tx - fx), Math.abs(ty - fy)) > (s.range ?? 6)) return "Too far. Mage Hand reaches 30 ft (6 squares).";
  const c = cellAt(w, tx, ty);
  if (!c || isBlocking(c) || entityAt(w, tx, ty)) return "Can't set it down there.";
  e.x = tx;
  e.y = ty;
  delete e.status.fallen;
  log(w, `Mage Hand sets the ${defOf(e).name.toLowerCase()} down`, "info");
  return null;
}

/** Places (or moves) the one critter of a kind; tapping it again picks it up. */
export function placeCritter(w: World, kind: string, x: number, y: number, dir: Dir): void {
  const def = RULES.critters.find((c) => c.id === kind);
  if (!def || !inBounds(w, x, y)) return;
  if (def.mode === "act") {
    log(w, `The ${def.name.toLowerCase()} gets to work`, "info");
    applyElement(w, x, y, def.element, { power: def.power ?? 1, dir, direct: true });
    return;
  }
  const here = critterAt(w, x, y);
  if (here && here.kind === kind) {
    w.critters.splice(w.critters.indexOf(here), 1);
    log(w, `The ${def.name.toLowerCase()} hops back into your satchel`, "info");
    return;
  }
  if (here) w.critters.splice(w.critters.indexOf(here), 1);
  const existing = w.critters.find((c) => c.kind === kind);
  if (existing) {
    existing.x = x;
    existing.y = y;
    existing.dir = dir;
    existing.timer = 1;
  } else {
    w.critters.push({ id: w.nextId++, kind, x, y, timer: 1, dir });
  }
  log(w, `You set down the ${def.name.toLowerCase()}`, "info");
  emitCritter(w, kind, x, y, dir);
}

export const PAINT_TERRAIN = ["dirt", "grass", "brush", "tree", "stone", "wood", "pit", "metal", "gate", "boiler", "plate", "wheel", "coil", "windmill"];
export const PAINT_OTHER = ["water", "oil", "erase"];

export function paint(w: World, brush: string, x: number, y: number): void {
  const c = cellAt(w, x, y);
  if (!c) return;
  if (brush in RULES.entities) {
    spawn(w, brush, x, y);
    return;
  }
  if (brush === "erase") {
    const e = entityAt(w, x, y);
    if (e) w.entities.splice(w.entities.indexOf(e), 1);
    const cr = critterAt(w, x, y);
    if (cr) w.critters.splice(w.critters.indexOf(cr), 1);
    Object.assign(c, newCell());
    return;
  }
  if (brush === "water") {
    if (!isBlocking(c)) c.water = Math.min(RULES.sim.water.maxLevel, c.water + 4);
    return;
  }
  if (brush === "oil") {
    if (!isBlocking(c)) c.cover = "oil";
    return;
  }
  if (!(brush in RULES.terrain)) return;
  const def = RULES.terrain[brush]!;
  if (def.tags.includes("blocking")) {
    const e = entityAt(w, x, y);
    if (e) destroy(w, e);
    if (!def.tags.includes("vessel")) displaceWater(w, x, y);
    c.cover = null;
  }
  c.t = brush;
  if (brush === "gate" && w.gatesOpen) c.t = "gate_open";
  if (!burns(def) && !c.cover) {
    c.fire = 0;
  }
}

export { isLevitating };
