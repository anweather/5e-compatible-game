// What the player does: cast a spell, place a critter, paint the map.
import { RULES, type SpellDef } from "./rules.ts";
import { applyElement, applyEntityElement, displaceWater } from "./react.ts";
import { awayDir, damage, defOf, destroy, isLevitating, massOf, push, spawn } from "./entities.ts";
import { emitCritter } from "./sim.ts";
import {
  type Dir, type Entity, type World,
  DX, DY, cellAt, critterAt, dirFrom, entityAt, inBounds, isBlocking, lineOfSight, log, newCell, playerOf, roll,
} from "./world.ts";

export const spellById = (id: string) => RULES.spells.find((s) => s.id === id);

/** Cells a spell would touch, in the order it touches them (for previews too). */
export function spellCells(w: World, s: SpellDef, x: number, y: number, dir: Dir): [number, number][] {
  const out: [number, number][] = [];
  const add = (cx: number, cy: number) => inBounds(w, cx, cy) && out.push([cx, cy]);
  switch (s.shape) {
    case "point":
    case "target":
      add(x, y);
      break;
    case "line":
      for (let k = 0; k < (s.length ?? 5); k++) {
        const cx = x + DX[dir] * k, cy = y + DY[dir] * k;
        const c = cellAt(w, cx, cy);
        if (!c) break;
        out.push([cx, cy]);
        if (isBlocking(c)) break;
      }
      break;
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
 * The player's cast: a click on (tx,ty) with the wizard as the caster.
 * Beams and Gust start next to the wizard and point at the click; Thunderwave
 * goes off around the wizard; everything else lands on the clicked square.
 * Returns a message when the cast can't happen.
 */
export function castAt(w: World, id: string, tx: number, ty: number): string | null {
  const s = spellById(id);
  if (!s) return null;
  const aim = aimSpell(w, id, tx, ty);
  if ("error" in aim) return aim.error;
  if (s.effect === "teleport") return mistyStep(w, s, tx, ty);
  castSpell(w, id, aim.x, aim.y, aim.dir, aim.casterId);
  return null;
}

type Aim = { x: number; y: number; dir: Dir; casterId?: number } | { error: string };

/** Where a click would put the spell: shared by castAt and the hover preview. */
export function aimSpell(w: World, id: string, tx: number, ty: number): Aim {
  const s = spellById(id)!;
  const p = playerOf(w);
  if (s.origin !== "self" && s.effect !== "teleport") return { x: tx, y: ty, dir: p ? (dirFrom(p.x, p.y, tx, ty) ?? 1) : 1 };
  if (!p) return { error: "You're down. Press Reset to try again." };
  if (s.effect === "teleport") return { x: tx, y: ty, dir: 1 };
  if (s.shape === "burst") return { x: p.x, y: p.y, dir: dirFrom(p.x, p.y, tx, ty) ?? 1, casterId: p.id };
  const dir = dirFrom(p.x, p.y, tx, ty);
  if (dir === null) return { error: `Click a square away from the wizard to point ${s.name} that way.` };
  return { x: p.x + DX[dir], y: p.y + DY[dir], dir, casterId: p.id };
}

/** Squares a click would touch, for the hover preview. */
export function previewCells(w: World, id: string, tx: number, ty: number): [number, number][] {
  const s = spellById(id)!;
  const aim = aimSpell(w, id, tx, ty);
  if ("error" in aim) return [];
  const cells = spellCells(w, s, aim.x, aim.y, aim.dir);
  return s.shape === "burst" && s.origin === "self" ? cells.filter(([x, y]) => x !== aim.x || y !== aim.y) : cells;
}

function mistyStep(w: World, s: SpellDef, tx: number, ty: number): string | null {
  const p = playerOf(w)!;
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

export function castSpell(w: World, id: string, x: number, y: number, dir: Dir, casterId?: number): void {
  const s = spellById(id);
  if (!s || !inBounds(w, x, y)) return;
  if (s.effect === "levitate") return levitate(w, s, x, y);
  if (s.effect === "carry") return; // two taps; see carry()
  const cells = spellCells(w, s, x, y, dir);
  const ctx = { power: s.power ?? 1, dir, direct: true };
  log(w, `You cast ${s.name}`, "info");
  if (s.element === "air" && s.wind) w.wind = { dir, ticks: s.wind };

  // Shove first (furthest first, so a line of goblins doesn't jam), then react.
  if (s.push) {
    const hit = cells
      .map(([cx, cy]) => entityAt(w, cx, cy))
      .filter((e): e is Entity => e !== undefined && e.id !== casterId)
      .sort((a, b) => Math.hypot(b.x - x, b.y - y) - Math.hypot(a.x - x, a.y - y));
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
