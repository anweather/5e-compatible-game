// Earth shaping: terraform spells raise and dig cells (Cell.h), and raised
// ground sheds its water. Unity's port is Chemistry/World.Terraform.cs.
import { type CraterDef, RULES, type SpellDef } from "./rules.ts";
import { shatter, wallLost } from "./react.ts";
import { type World, burns, cellAt, entityAt, idx, isBlocking, logOnce, neighbours, terrainDef } from "./world.ts";

/**
 * Raises or digs every cell of a terraform spell aimed at (x, y): by raise or dig
 * steps, or (height) up to that height, leaving higher cells alone.
 */
export function terraform(w: World, s: SpellDef, cells: [number, number][], x: number, y: number, charge = 1): void {
  const toHeight = s.height !== undefined;
  // Charged: that many times the steps, or 2 more height per level.
  const steps = toHeight ? s.height! + 2 * (charge - 1) : (s.raise ? s.raise : -(s.dig ?? 0)) * charge;
  for (const [cx, cy] of cells) {
    const d = Math.max(Math.abs(cx - x), Math.abs(cy - y)), radius = s.radius ?? 1;
    if (s.taper && d > radius) continue;
    let n = s.taper ? tapered(steps, d, radius) : steps;
    if (toHeight) n = Math.max(0, n - cellAt(w, cx, cy)!.h);
    if (n !== 0) reshape(w, cx, cy, n, s.terrain, s.strength ?? 1);
  }
}

/**
 * A blast's crater at (x, y): the ground dug depth steps in the middle,
 * tapering to none past radius, in row order. Unity's is World.Crater.
 */
export function crater(w: World, x: number, y: number, c: CraterDef | undefined): void {
  if (!c) return;
  const r = c.radius ?? 1;
  for (let dy = -r; dy <= r; dy++) {
    for (let dx = -r; dx <= r; dx++) {
      const n = tapered(-c.depth, Math.max(Math.abs(dx), Math.abs(dy)), r);
      if (n !== 0) reshape(w, x + dx, y + dy, n, undefined, c.strength ?? 1);
    }
  }
}

/**
 * Steps at distance d (in squares) from the middle of a hill or crater of the
 * given radius: all of them in the middle, rounding up toward the edge, none past it.
 */
function tapered(steps: number, d: number, radius: number): number {
  if (d > radius) return 0;
  return Math.sign(steps) * Math.floor((Math.abs(steps) * (radius + 1 - d) + radius) / (radius + 1));
}

/**
 * Raises (n > 0) or digs (n < 0) one cell by n steps, clamped to the sim's
 * height range. A terrain that uproots (a tree, a cabin's log wall) turns to
 * its uprootsTo first, and one that shatters (a window pane) shatters. Blocking terrain and machines can't be shaped, and nothing
 * rises into a wall under a creature or object. Digging strips the cover and
 * turns the terrain to its dugTo; raised ground spills its water onto its
 * neighbours. A cell that ends up blocking loses its cover and fire.
 */
export function reshape(w: World, x: number, y: number, n: number, terrain?: string, strength = 1): boolean {
  const c = cellAt(w, x, y);
  if (!c) return false;
  const shattered = shatter(w, x, y);
  let t = terrainDef(c);
  const uprooted = t.uprootsTo !== undefined || shattered;
  if (t.uprootsTo !== undefined) {
    if (t.uprootLog) logOnce(w, `uproot#${c.t}#${w.tick}`, t.uprootLog, "notable");
    c.t = t.uprootsTo;
    t = terrainDef(c);
  }
  if (uprooted) wallLost(w, x, y);
  if (t.tags.includes("blocking") || t.tags.includes("machine")) return uprooted;
  if (n < 0 && strength < (t.hardness ?? 1)) {
    logOnce(w, `hard#${c.t}#${w.tick}`, `The ${t.name.toLowerCase()} is too hard to dig`);
    return uprooted;
  }
  const { min, max, blockAt } = RULES.sim.height;
  const h = Math.max(min, Math.min(max, c.h + n));
  if (h === c.h || (h >= blockAt && entityAt(w, x, y))) return uprooted;
  c.h = h;
  if (n < 0) c.cover = null;
  const to = terrain ?? (n < 0 ? t.dugTo : undefined);
  if (to !== undefined) c.t = to;
  if (n > 0) spillWater(w, x, y);
  if (isBlocking(c)) c.cover = null;
  if (isBlocking(c) || (!burns(terrainDef(c)) && c.cover === null)) {
    c.fire = 0;
    c.fuel = 0;
  }
  return true;
}

/**
 * Moves all of a cell's water (ice melts) onto the open cells around it: one
 * unit at a time round each ring of neighbours, nearest ring first, in N, E,
 * S, W order. Nothing is lost unless the whole world is full.
 */
function spillWater(w: World, x: number, y: number) {
  const c = cellAt(w, x, y)!;
  const max = RULES.sim.water.maxLevel;
  let units = c.water;
  c.water = 0;
  c.frozen = false;
  const seen = new Set([idx(w, x, y)]);
  let ring = [idx(w, x, y)];
  while (units > 0 && ring.length) {
    const next: number[] = [];
    for (const i of ring) {
      for (const [nx, ny] of neighbours(w, i % w.w, Math.floor(i / w.w))) {
        const k = idx(w, nx, ny), nc = w.cells[k]!;
        if (seen.has(k)) continue;
        seen.add(k);
        if (!isBlocking(nc) && !(nc.frozen && nc.water > 0)) next.push(k);
      }
    }
    for (let moved = true; units > 0 && moved;) {
      moved = false;
      for (const k of next) {
        if (units === 0) break;
        if (w.cells[k]!.water >= max) continue;
        w.cells[k]!.water++;
        units--;
        moved = true;
      }
    }
    ring = next;
  }
}
