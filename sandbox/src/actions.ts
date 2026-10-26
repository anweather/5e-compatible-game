// What the player does: cast a spell, place a critter, paint the map.
import { RULES, type SpellDef } from "./rules.ts";
import { applyElement, applyEntityElement, displaceWater } from "./react.ts";
import { awayDir, damage, defOf, destroy, isLevitating, massOf, push, spawn } from "./entities.ts";
import { emitCritter } from "./sim.ts";
import { crater, terraform } from "./terraform.ts";
import {
  type Dir, type Entity, type World,
  DX, DY, cellAt, critterAt, dirFrom, entityAt, inBounds, isBlocking, lineOfSight, log, newCell, playerOf, roll,
} from "./world.ts";

export const spellById = (id: string) => RULES.spells.find((s) => s.id === id);

/** Cells a spell would touch, in the order it touches them (for previews too). */
/** The most a cast can be charged (held before release): 1 is a tap. */
export const MAX_CHARGE = 3;
export const clampCharge = (charge: number) => Math.max(1, Math.min(MAX_CHARGE, Math.floor(charge)));
/** A line spell's length at a charge: half again for each level past a tap. */
export const chargedLength = (length: number, charge: number) => length + Math.floor((length * (clampCharge(charge) - 1)) / 2);

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
  const add = (cx: number, cy: number) => inBounds(w, cx, cy) && out.push([cx, cy]);
  const [ax, ay] = snapAim(aim?.[0] ?? 0, aim?.[1] ?? 0);
  const slant = ax !== 0 && ay !== 0;
  if (slant && s.shape === "line") return slantCells(w, s, x, y, dir, chargedLength(s.length ?? 5, charge), ax, ay);
  if (slant && s.shape === "cone") return slantCone(w, x, y, chargedLength(s.length ?? 5, charge), ax, ay);
  switch (s.shape) {
    case "point":
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
      const n = s.size ?? 2, off = Math.floor((n - 1) / 2);
      for (let dy = 0; dy < n; dy++) for (let dx = 0; dx < n; dx++) add(x - off + dx, y - off + dy);
      break;
    }
    case "burst": {
      const r = s.radius ?? 1;
      for (let dy = -r; dy <= r; dy++) for (let dx = -r; dx <= r; dx++) add(x + dx, y + dy);
      break;
    }
  }
  return out;
}

/**
 * A line along a slanted aim: from (x, y) (a trench from the caster's square
 * behind it first) out to length squares as the crow flies, up to its first
 * blocking square. Each square shares a side with the one before, turning a
 * corner through the square along the aim's longer part (World.SlantCells).
 */
function slantCells(w: World, s: SpellDef, x: number, y: number, dir: Dir, length: number, ax: number, ay: number): [number, number][] {
  const out: [number, number][] = [];
  const visit = (cx: number, cy: number): boolean => {
    const c = cellAt(w, cx, cy);
    if (!c) return false;
    out.push([cx, cy]);
    return !isBlocking(c);
  };
  if (s.effect === "terraform" && s.origin === "self" && cellAt(w, x - DX[dir], y - DY[dir]) && !visit(x - DX[dir], y - DY[dir])) return out;
  const alongX = Math.abs(ax) >= Math.abs(ay), major = Math.max(Math.abs(ax), Math.abs(ay));
  const reach = length * length * major * major, size = ax * ax + ay * ay;
  let px = x, py = y;
  for (let k = 0; k * k * size < reach; k++) {
    const cx = x + roundDiv(k * ax, major), cy = y + roundDiv(k * ay, major);
    if (k > 0 && cx !== px && cy !== py && !visit(alongX ? cx : px, alongX ? py : cy)) return out;
    if (!visit(cx, cy)) return out;
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
 * that many times the steps, and a line reaches half again as far per level
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
  landSpell(w, s, x, y, dir, casterId, clampCharge(charge), aim);
}

/** What a spell does where it lands, without the cast's log line (a meteor's landing too; World.Land). */
export function landSpell(w: World, s: SpellDef, x: number, y: number, dir: Dir, casterId: number | undefined, charge: number, aim?: [number, number]): void {
  const cells = spellCells(w, s, x, y, dir, charge, aim);
  const ctx = { power: (s.power ?? 1) * charge, dir, direct: true };
  if (s.element === "air" && s.wind) w.wind = { dir, ticks: s.wind };
  if (s.effect === "terraform") terraform(w, s, cells, x, y, charge);
  crater(w, x, y, s.crater);

  // Shove first (furthest first, so a line of goblins doesn't jam), then react.
  if (s.push) {
    const hit = cells
      .map(([cx, cy]) => entityAt(w, cx, cy))
      .filter((e): e is Entity => e !== undefined && e.id !== casterId)
      .sort((a, b) => (b.x - x) ** 2 + (b.y - y) ** 2 - ((a.x - x) ** 2 + (a.y - y) ** 2) || a.id - b.id);
    for (const e of hit) {
      if (s.damage) damage(w, e, roll(w, s.damage), s.name.toLowerCase());
      if (!w.entities.includes(e)) continue;
      const d = s.shape === "burst" ? awayDir(e.x - x, e.y - y, dir) : dir;
      push(w, e, d, s.push, true);
    }
  }
  for (const [cx, cy] of cells) {
    if (casterId !== undefined && s.shape === "burst" && cx === x && cy === y) continue;
    if (s.element) applyElement(w, cx, cy, s.element, ctx);
    if (!s.push && s.damage) {
      const e = entityAt(w, cx, cy);
      if (e && e.id === casterId) continue;
      if (e && defOf(e).creature) {
        const mul = s.element === "lightning" ? applyEntityElement(w, e, "lightning", { ...ctx, direct: false }) : 1;
        damage(w, e, roll(w, s.damage) * mul, s.name.toLowerCase());
      } else if (e && s.element === "fire" && defOf(e).flammable) {
        damage(w, e, roll(w, s.damage), "fire");
      }
    }
  }
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
  if (!def.tags.includes("flammable") && !c.cover) {
    c.fire = 0;
  }
}

export { isLevitating };
