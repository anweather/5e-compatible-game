// The rule interpreter: an element arrives at a cell (from a spell, a
// critter, or spreading fire) and reactions.json decides what happens.
import { RULES, entityCondHolds, type CellCond, type CellRule, type Effect, type EntityDef, type EntityRule } from "./rules.ts";
import {
  type Cell, type Dir, type Entity, type World,
  DX, DY, addGas, burns, cellAt, coverBurns, entityAt, hasTag, idx, inBounds, isBlocking, isConductive,
  logOnce, neighbours, rand, terrainDef,
} from "./world.ts";
import { dryness } from "./sim.ts";

export interface Ctx {
  power: number;
  dir?: Dir;
  /** Cast or emitted on purpose. Indirect reactions (spreading) log only notable rules. */
  direct: boolean;
  /** Where the spell landed (landSpell), for a grow effect's wave (World.waveFrom). */
  at?: [number, number];
}

// Rules grouped by element, rebuilt whenever a mod swaps the tables.
let indexedCell: CellRule[] | null = null, indexedEntity: EntityRule[] | null = null;
const byElement = new Map<string, CellRule[]>();
const entityByElement = new Map<string, EntityRule[]>();
function index() {
  if (indexedCell === RULES.cellRules && indexedEntity === RULES.entityRules) return;
  byElement.clear();
  entityByElement.clear();
  for (const r of RULES.cellRules) byElement.set(r.element, [...(byElement.get(r.element) ?? []), r]);
  for (const r of RULES.entityRules) entityByElement.set(r.element, [...(entityByElement.get(r.element) ?? []), r]);
  indexedCell = RULES.cellRules;
  indexedEntity = RULES.entityRules;
}

function matches(c: Cell, cond: CellCond | undefined): boolean {
  if (!cond) return true;
  if (cond.cover && !(c.cover !== null && cond.cover.includes(c.cover))) return false;
  if (cond.fire !== undefined) {
    if (cond.fire === true && c.fire === 0) return false;
    if (cond.fire === false && c.fire > 0) return false;
    if (typeof cond.fire === "number" && c.fire < cond.fire) return false;
  }
  if (cond.fireBelow !== undefined && c.fire >= cond.fireBelow) return false;
  if (cond.water !== undefined && (c.water > 0 && !c.frozen) !== cond.water) return false;
  if (cond.frozen !== undefined && (c.frozen && c.water > 0) !== cond.frozen) return false;
  if (cond.gas !== undefined && (cond.gas === "any" ? c.gas === null : c.gas !== cond.gas)) return false;
  if (cond.terrainTag && !hasTag(c, cond.terrainTag)) return false;
  if (cond.notTerrainTag && hasTag(c, cond.notTerrainTag)) return false;
  if (cond.conductive !== undefined && isConductive(c) !== cond.conductive) return false;
  return true;
}

/**
 * Whether a rule's "if" holds (World.Applies): the cell matches, the element came
 * with at least its power, and its chance comes up, rolled last so the generator
 * only turns once the rule would otherwise run.
 */
function applies(w: World, c: Cell, cond: CellCond | undefined, power: number): boolean {
  if (!cond) return true;
  if (!matches(c, cond)) return false;
  if (cond.power !== undefined && power < cond.power) return false;
  return cond.chance === undefined || rand(w) < cond.chance;
}

/** Lays a cover (or with null clears it); one that lasts (thorns) starts its cell's life (World.PutCover). */
export function putCover(c: Cell, cover: string | null) {
  c.cover = cover;
  const lasts = cover !== null ? (RULES.covers[cover]?.lasts ?? 0) : 0;
  if (lasts > 0) c.life = lasts;
}

/** A grow effect's life: a roll within its ticks, plus its wave per square from where the spell landed (World.GrowTicks). */
function growTicks(w: World, eff: Effect, x: number, y: number, ctx: Ctx): number {
  const [lo, hi] = eff.ticks ?? [1, 1];
  let n = lo;
  if (hi > lo) n += Math.floor(rand(w) * (hi - lo + 1));
  if (ctx.at) n += (eff.wave ?? 0) * Math.max(Math.abs(x - ctx.at[0]), Math.abs(y - ctx.at[1]));
  return Math.max(1, n);
}

function fill(text: string, c: Cell): string {
  return text
    .replace("{cover}", c.cover ? RULES.covers[c.cover]!.name.toLowerCase() : "")
    .replace("{terrain}", terrainDef(c).name.toLowerCase());
}

/**
 * A dissolve effect (World.Dissolve): the cell's cover or terrain, if `element` is what dissolves it, is eaten
 * away into what it leaves (a cover can leave none).
 */
function dissolve(c: Cell, what: "cover" | "terrain", element: string): boolean {
  if (what === "cover") {
    const cv = c.cover !== null ? RULES.covers[c.cover] : undefined;
    if (!cv || cv.dissolvedBy !== element) return false;
    putCover(c, cv.dissolvesTo ?? null);
    return true;
  }
  const t = terrainDef(c);
  if (t.dissolvedBy !== element || !t.dissolvesTo) return false;
  c.t = t.dissolvesTo;
  return true;
}

/** Applies one element to one cell, then to whatever stands there. */
export function applyElement(w: World, x: number, y: number, element: string, ctx: Ctx): void {
  const c = cellAt(w, x, y);
  if (!c) return;
  index();
  const before = fill("{cover}|{terrain}", c);
  for (const rule of byElement.get(element) ?? []) {
    if (!applies(w, c, rule.if, ctx.power)) continue;
    let changed = rule.do.length === 0;
    for (const eff of rule.do) changed = runEffect(w, x, y, c, eff, ctx, element) || changed;
    if (changed && rule.log && (ctx.direct || rule.notable)) {
      const [cover, terrain] = before.split("|");
      const text = rule.log.replace("{cover}", cover!).replace("{terrain}", terrain!);
      logOnce(w, rule.id, text, rule.notable ? "notable" : "info");
    }
    if (rule.stop) break;
  }
  const e = entityAt(w, x, y);
  if (e) applyEntityElement(w, e, element, ctx);
}

/**
 * A status's ticks, but burning on a thing of a flammable material (a wooden crate) catches only with the
 * material's flammability and lasts its fuel, both less its soak.dampens while the thing is wet; 0 when it
 * didn't catch (World.MaterialBurn).
 */
function materialBurn(w: World, e: Entity, def: EntityDef, status: string, ticks: number): number {
  const m = def.material ? RULES.terrain[def.material] : undefined;
  if (status !== "burning" || !m || !burns(m)) return ticks;
  const dryness = (e.status["wet"] ?? 0) > 0 ? 1 - (m.dampens ?? 0) : 1;
  if (rand(w) >= (m.flammability ?? 0) * dryness) return 0;
  return dampFuel(m.fuel ?? ticks, dryness);
}

/** Returns the damage multiplier the entity's state adds (wet + lightning = 2). */
export function applyEntityElement(w: World, e: Entity, element: string, ctx: Ctx): number {
  index();
  const def = RULES.entities[e.kind]!;
  let mul = 1;
  for (const rule of entityByElement.get(element) ?? []) {
    const cond = rule.if;
    if (cond && !entityCondHolds(cond, def, e.status)) continue;
    for (const eff of rule.do) {
      if (eff.status)
        for (const [k, v] of Object.entries(eff.status)) {
          const ticks = materialBurn(w, e, def, k, v);
          if (ticks > 0) e.status[k] = Math.max(e.status[k] ?? 0, ticks);
        }
      if (eff.clear) delete e.status[eff.clear];
      if (eff.damageMul) mul *= eff.damageMul;
    }
    if (rule.log && (ctx.direct || rule.notable)) {
      logOnce(w, `${rule.id}#${e.id}`, rule.log.replace("{name}", def.name), rule.notable ? "notable" : "info");
    }
    if (rule.stop) break;
  }
  return mul;
}

/** Lights flammable terrain or cover; caught at `dryness` (sim.ts dryness), it burns that much of its fuel (World.Ignite). */
export function ignite(c: Cell, intensity: number | "cover", dryness = 1): boolean {
  if (intensity === "cover") {
    if (!coverBurns(c)) return false;
    const cv = RULES.covers[c.cover!]!;
    const n = cv.burnIntensity ?? 2;
    if (c.fire >= n) return false;
    if (c.fire === 0) c.fuel = cv.fuel ?? 5;
    c.fire = n;
    return true;
  }
  const t = terrainDef(c);
  if (!burns(t) && !coverBurns(c)) return false;
  if (c.fire >= intensity) return false;
  if (c.fire === 0) c.fuel = dampFuel(coverBurns(c) ? (RULES.covers[c.cover!]!.fuel ?? 5) : (t.fuel ?? 5), dryness);
  c.fire = intensity;
  return true;
}

/** Fuel caught damp (soaked burns poorly, docs/MATERIALS.md): scaled by the dryness, rounded down, at least 1 (World.DampFuel). */
export const dampFuel = (fuel: number, dryness: number) => (dryness >= 1 ? fuel : Math.max(1, Math.floor(fuel * dryness)));

/** Moves water out of a cell that just became solid. */
export function displaceWater(w: World, x: number, y: number) {
  const c = cellAt(w, x, y)!;
  const max = RULES.sim.water.maxLevel;
  let guard = 16;
  while (c.water > 0 && guard-- > 0) {
    let moved = false;
    for (const [nx, ny] of neighbours(w, x, y)) {
      const n = cellAt(w, nx, ny)!;
      if (c.water === 0) break;
      if (isBlocking(n) || n.water >= max) continue;
      n.water++;
      c.water--;
      moved = true;
    }
    if (!moved) break;
  }
  c.water = 0;
  c.frozen = false;
}

function conduct(w: World, x: number, y: number, charge: number): boolean {
  const start = cellAt(w, x, y)!;
  if (!isConductive(start)) return false;
  const seen = new Set<number>([idx(w, x, y)]);
  const queue: [number, number][] = [[x, y]];
  let count = 0;
  while (queue.length && count < RULES.sim.conductLimit) {
    const [cx, cy] = queue.shift()!;
    const c = cellAt(w, cx, cy)!;
    c.charge = Math.max(c.charge, charge);
    if (coverBurns(c)) ignite(c, "cover");
    count++;
    for (const [nx, ny] of neighbours(w, cx, cy)) {
      const k = idx(w, nx, ny);
      if (seen.has(k)) continue;
      seen.add(k);
      if (isConductive(cellAt(w, nx, ny)!)) queue.push([nx, ny]);
    }
  }
  return true;
}

function runEffect(w: World, x: number, y: number, c: Cell, eff: Effect, ctx: Ctx, element: string): boolean {
  const max = RULES.sim.water.maxLevel;
  if (eff.ignite !== undefined) return ignite(c, eff.ignite, dryness(w, c));
  if (eff.boil !== undefined) {
    if (c.water === 0 || c.frozen) return false;
    c.water--;
    addGas(c, "steam", eff.boil);
    w.marks.steam[idx(w, x, y)] = 1;
    return true;
  }
  if (eff.melt) {
    if (!c.frozen) return false;
    c.frozen = false;
    return true;
  }
  if (eff.freeze) {
    if (c.water === 0 || c.frozen) return false;
    c.frozen = true;
    return true;
  }
  if (eff.extinguish) {
    if (c.fire === 0) return false;
    c.fire = 0;
    c.fuel = 0;
    return true;
  }
  if (eff.condense) {
    if (c.gas !== "steam") return false;
    c.gasAmt -= 2;
    if (c.gasAmt <= 0) {
      c.gas = null;
      c.gasAmt = 0;
    }
    if (!isBlocking(c) || hasTag(c, "vessel")) c.water = Math.min(max, c.water + 2);
    return true;
  }
  if (eff.addWater !== undefined) {
    const n = eff.addWater === "power" ? ctx.power : eff.addWater;
    if (c.water >= max) return false;
    c.water = Math.min(max, c.water + n);
    return true;
  }
  if (eff.setCover !== undefined) {
    if (c.cover === eff.setCover) return false;
    putCover(c, eff.setCover);
    return true;
  }
  if (eff.spreadCover !== undefined) {
    if (!c.cover) return false;
    const options = [...neighbours(w, x, y)].filter(([nx, ny]) => !isBlocking(cellAt(w, nx, ny)!));
    for (let i = 0; i < eff.spreadCover && options.length; i++) {
      const [nx, ny] = options.splice(Math.floor(rand(w) * options.length), 1)[0]!;
      const n = cellAt(w, nx, ny)!;
      putCover(n, c.cover);
      if (c.fire > 0) ignite(n, "cover");
    }
    return true;
  }
  if (eff.dissolve !== undefined) return dissolve(c, eff.dissolve, element);
  if (eff.heat) {
    const hot = terrainDef(c).hotFor ?? 0;
    if (hot <= 0 || (c.heat ?? 0) >= hot) return false;
    c.heat = hot;
    return true;
  }
  if (eff.break) {
    const to = terrainDef(c).breaksTo;
    if (!to) return false;
    c.t = to;
    return true;
  }
  if (eff.setTerrain !== undefined) {
    if (c.t === eff.setTerrain) return false;
    displaceWater(w, x, y);
    c.t = eff.setTerrain;
    return true;
  }
  if (eff.raise !== undefined) {
    if (entityAt(w, x, y)) return false;
    displaceWater(w, x, y);
    c.t = eff.raise;
    c.cover = null;
    c.fire = 0;
    return true;
  }
  if (eff.conduct !== undefined) return conduct(w, x, y, eff.conduct);
  if (eff.fan) {
    if (ctx.dir === undefined) return false;
    c.fire = Math.min(3, c.fire + 1);
    const nx = x + DX[ctx.dir], ny = y + DY[ctx.dir];
    if (inBounds(w, nx, ny)) applyElement(w, nx, ny, "fire", { power: 2, direct: false });
    return true;
  }
  if (eff.pushGas) {
    if (!c.gas || ctx.dir === undefined) return false;
    const n = cellAt(w, x + DX[ctx.dir], y + DY[ctx.dir]);
    if (n && !isBlocking(n)) addGas(n, c.gas, c.gasAmt);
    c.gas = null;
    c.gasAmt = 0;
    return true;
  }
  if (eff.clearGas) {
    if (!c.gas) return false;
    c.gas = null;
    c.gasAmt = 0;
    return true;
  }
  if (eff.mark === "wind") {
    w.marks.wind[idx(w, x, y)] = 1;
    return false;
  }
  if (eff.gas !== undefined) {
    addGas(c, eff.gas, eff.n ?? 1);
    return true;
  }
  if (eff.grow !== undefined) {
    // Nothing takes root on a wall of raised earth.
    if (isBlocking(c)) return false;
    c.t = eff.grow;
    c.life = growTicks(w, eff, x, y, ctx);
    return true;
  }
  return false;
}
