// The rule interpreter: an element arrives at a cell (from a spell, a
// critter, or spreading fire) and reactions.json decides what happens.
import { RULES, VERB_STATUS, entityCondHolds, hasComponent, statusTicks, type CellCond, type CellRule, type Effect, type EntityDef, type EntityRule } from "./rules.ts";
import {
  type Cell, type Dir, type Entity, type World,
  DX, DY, addGas, burns, cellAt, coverBurns, entityAt, hasTag, idx, inBounds, isBlocking, isConductive,
  logOnce, neighbours, rand, terrainDef,
} from "./world.ts";
import { awayDir, push } from "./entities.ts";
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

export function matches(c: Cell, cond: CellCond | undefined): boolean {
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
  if (cond.hot !== undefined && ((c.heat ?? 0) > 0) !== cond.hot) return false;
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

/** A bake effect (World.Bake): heat turns the terrain into its bakesTo. */
function bake(c: Cell): boolean {
  const to = terrainDef(c).bakesTo;
  if (!to) return false;
  c.t = to;
  return true;
}

/** A meltCover effect (World.MeltCover): heat turns the cover into its meltsTo, and its meltWater. */
function meltCover(c: Cell): boolean {
  const cv = c.cover !== null ? RULES.covers[c.cover] : undefined;
  if (!cv?.meltWater) return false;
  putCover(c, cv.meltsTo ?? null);
  c.water = Math.min(RULES.sim.water.maxLevel, c.water + cv.meltWater);
  return true;
}

const flashOf = (c: Cell) => (c.gas ? RULES.gases[c.gas]?.burn?.flash : undefined);

/**
 * Fire has met a gas that flashes (burn.flash: dust): the whole cloud joined to (x, y) goes up at once and is used up.
 * Each of its cells burns for a moment at the flash's fire, the fire reaches what's there through the rules like any
 * other, and it shoves things away from where it caught. World.FlashGas; the C# core's shove is the view's.
 */
export function flashGas(w: World, x: number, y: number): void {
  const first = flashOf(cellAt(w, x, y)!);
  if (!first) return;
  const seen = new Set([idx(w, x, y)]);
  const cloud: [number, number][] = [[x, y]];
  const flashes = cloud.map(([cx, cy]) => flashOf(cellAt(w, cx, cy)!)!);
  for (let k = 0; k < cloud.length; k++) {
    for (const [nx, ny] of neighbours(w, cloud[k]![0], cloud[k]![1])) {
      const next = flashOf(cellAt(w, nx, ny)!);
      if (!next || seen.has(idx(w, nx, ny))) continue;
      seen.add(idx(w, nx, ny));
      cloud.push([nx, ny]);
      flashes.push(next);
    }
  }
  for (const [cx, cy] of cloud) {
    const c = cellAt(w, cx, cy)!;
    c.gas = null;
    c.gasAmt = 0;
  }
  if (first.log) logOnce(w, `flash#${w.tick}`, first.log, "notable");
  cloud.forEach(([cx, cy], k) => {
    const c = cellAt(w, cx, cy)!, f = flashes[k]!;
    c.fire = Math.max(c.fire, f.fire);
    c.fuel = Math.max(c.fuel, f.fire);
    applyElement(w, cx, cy, "fire", { power: f.fire, direct: false });
    const e = entityAt(w, cx, cy);
    if (e && f.push) push(w, e, awayDir(cx - x, cy - y), f.push, false);
  });
}

/** Applies one element to one cell, then to whatever stands there. */
export function applyElement(w: World, x: number, y: number, element: string, ctx: Ctx): void {
  const c = cellAt(w, x, y);
  if (!c) return;
  index();
  if (element === "fire") flashGas(w, x, y);
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

/**
 * A statusOf effect (World.MaterialStatus): the status the verb gives (burn burning, freeze frozen, soak wet), for
 * as many ticks as the thing's material says (flesh burns 9, bone not at all).
 */
function materialStatus(e: Entity, def: EntityDef, verb: string) {
  const ticks = statusTicks(def.material ? RULES.terrain[def.material] : undefined, verb);
  const status = VERB_STATUS[verb];
  if (ticks > 0 && status) e.status[status] = Math.max(e.status[status] ?? 0, ticks);
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
      if (eff.statusOf) materialStatus(e, def, eff.statusOf);
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

/** Ice that cracks, on this cell (World.IceOver); a mod's ice that doesn't crack doesn't shatter either. */
function iceOver(c: Cell) {
  const ice = c.frozen ? RULES.terrain.ice : undefined;
  return ice?.cracks ? ice : undefined;
}

const logShattered = (w: World, stage: { name: string }) => logOnce(w, `shatter#${stage.name}#${w.tick}`, `The ${stage.name.toLowerCase()} shatters`, "notable");

/**
 * A hit on the cell: a blast, ground shaken under it, a heavy thing or lightning on ice. Its ice first, else its
 * terrain if that has a shatter.leaves (a window pane, glass ground); fire does nothing to it. With shatter.cracks
 * the first hit cracks it (once a tick, so one blast is one hit) and the next shatters it (World.Shatter). Returns
 * whether it shattered.
 */
export function shatter(w: World, x: number, y: number): boolean {
  const c = cellAt(w, x, y);
  if (!c) return false;
  const ice = iceOver(c), t = terrainDef(c);
  if (!ice && !t.shattersTo) return false;
  const stage = ice ?? t;
  if (stage.cracks) {
    if (c.cracked === w.tick + 1) return false;
    if (!c.cracked) {
      crack(w, x, y, c, stage.name);
      return false;
    }
  }
  if (ice) c.frozen = false;
  else c.t = t.shattersTo!;
  delete c.cracked;
  logShattered(w, stage);
  return true;
}

/** Cracked, with a warning if a creature stands on it (World.Crack). */
function crack(w: World, x: number, y: number, c: Cell, stageName: string) {
  c.cracked = w.tick + 1;
  const name = stageName.toLowerCase();
  const on = entityAt(w, x, y), def = on && RULES.entities[on.kind];
  const line = def && hasComponent(def, "creature") ? `The ${name} cracks under ${def.name}` : `The ${name} cracks`;
  logOnce(w, `crack#${line}#${w.tick}`, line, "notable");
}

/** A shatter effect (World.Struck): true if the hit cracked or shattered something. */
function struck(w: World, x: number, y: number, c: Cell): boolean {
  const was = c.cracked;
  return shatter(w, x, y) || c.cracked !== was;
}

/** A blast's shock round (x, y) (World.BlastAround). */
export function blastAround(w: World, x: number, y: number, radius: number) {
  for (let dy = -radius; dy <= radius; dy++) for (let dx = -radius; dx <= radius; dx++) shatter(w, x + dx, y + dy);
}

/** A wall at (x, y) is gone: a pane beside it with no wall left to stand in falls (World.WallLost). */
export function wallLost(w: World, x: number, y: number) {
  for (const [nx, ny] of neighbours(w, x, y)) {
    const n = cellAt(w, nx, ny)!;
    const t = terrainDef(n);
    // A pane with nothing to stand in goes at once, cracked or not.
    if (isBlocking(n) && t.shattersTo && !held(w, nx, ny)) {
      n.t = t.shattersTo;
      delete n.cracked;
      logShattered(w, t);
    }
  }
}

/** Whether a wall stands beside a pane (panes don't hold each other up). */
function held(w: World, x: number, y: number): boolean {
  for (const [nx, ny] of neighbours(w, x, y)) {
    const n = cellAt(w, nx, ny)!;
    if (isBlocking(n) && !terrainDef(n).shattersTo) return true;
  }
  return false;
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

/**
 * Moves a cell's gas one cell along dir (pushGas: a gust carries smoke, fog and steam). A different gas there is
 * shoved on ahead first, so gases neither merge nor vanish; against a wall it stays. True if it moved (World.ShoveGas).
 */
export function shoveGas(w: World, x: number, y: number, dir: Dir): boolean {
  const c = cellAt(w, x, y)!;
  const nx = x + DX[dir], ny = y + DY[dir];
  const n = cellAt(w, nx, ny);
  if (!n || isBlocking(n)) return false;
  if (n.gas !== null && n.gas !== c.gas && !shoveGas(w, nx, ny, dir)) return false;
  addGas(n, c.gas!, c.gasAmt);
  c.gas = null;
  c.gasAmt = 0;
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
  if (eff.shatter) return struck(w, x, y, c);
  if (eff.bake) return bake(c);
  if (eff.meltCover) return meltCover(c);
  if (eff.break) {
    const to = terrainDef(c).breaksTo;
    if (!to) return false;
    c.t = to;
    wallLost(w, x, y);
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
  if (eff.pushGas) return !!c.gas && ctx.dir !== undefined && shoveGas(w, x, y, ctx.dir);
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
