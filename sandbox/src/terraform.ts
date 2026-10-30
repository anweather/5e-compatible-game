// Earth shaping: terraform spells raise and dig cells (Cell.h), and raised
// ground sheds its water. Unity's port is Chemistry/World.Terraform.cs.
import { type CraterDef, type ItemDef, RULES, type SpellDef, type TerrainDef } from "./rules.ts";
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
    if (n !== 0) reshape(w, cx, cy, n, s.terrain, s.strength ?? 1, s.name);
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
  const mid = c.leaves ? cellAt(w, x, y) : undefined;
  if (mid && !isBlocking(mid)) mid.t = c.leaves!;
}

/**
 * A held tool's swing at (x, y): one step down, at the tool's strength (items.json
 * "use": "dig"). Nothing for an item that doesn't dig. Unity's is World.Dig.
 */
export function dig(w: World, x: number, y: number, tool: ItemDef | undefined): void {
  if (tool?.use === "dig") reshape(w, x, y, -1, undefined, tool.strength ?? 1, tool.name);
}

/**
 * Steps at distance d (in squares) from the middle of a hill or crater of the
 * given radius: all of them in the middle, rounding up toward the edge, none past it.
 */
function tapered(steps: number, d: number, radius: number): number {
  if (d > radius) return 0;
  return Math.sign(steps) * Math.floor((Math.abs(steps) * (radius + 1 - d) + radius) / (radius + 1));
}

const unshapeable = (t: TerrainDef) => t.tags.includes("blocking") || t.tags.includes("machine");

/**
 * Digging (n below 0) harder ground than the dig's strength, or what never digs (hardness 0, walls, machines):
 * refused, and said once a tick, naming the spell or tool that tried (by) when there is one (World.TooHard).
 */
function tooHard(w: World, id: string, t: TerrainDef, n: number, strength: number, by?: string): boolean {
  const hardness = t.hardness ?? 1;
  const never = hardness <= 0 || unshapeable(t);
  if (n >= 0 || (!never && strength >= hardness)) return false;
  const name = t.name.toLowerCase();
  let why: string;
  if (never) why = `The ${name} can't be dug`;
  else if (by === undefined) why = `The ${name} is too hard to dig`;
  else why = `The ${name} is too hard for ${by}`;
  logOnce(w, `hard#${id}#${w.tick}`, why);
  return true;
}

/**
 * Raises (n > 0) or digs (n < 0) one cell by n steps, clamped to the sim's
 * height range. A terrain that uproots (a tree, a cabin's log wall) turns to
 * its uprootsTo first, and one that shatters (a window pane) shatters. Blocking terrain and machines can't be shaped, and nothing
 * rises into a wall under a creature or object. Digging strips the cover and
 * turns the terrain to its dugTo; raised ground spills its water onto its
 * neighbours. A cell that ends up blocking loses its cover and fire.
 */
export function reshape(w: World, x: number, y: number, n: number, terrain?: string, strength = 1, by?: string): boolean {
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
  if (tooHard(w, c.t, t, n, strength, by) || unshapeable(t)) return uprooted;
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
