// Things on the board: damage, death, explosions, and being shoved around.
import { RULES, hasComponent } from "./rules.ts";
import { applyElement, applyEntityElement, ignite } from "./react.ts";
import { crater } from "./terraform.ts";
import {
  type Cell, type Dir, type Entity, type World,
  DX, DY, cellAt, entityAt, hasTag, isBlocking, isSlippery, log, logOnce, neighbours, roll, terrainDef,
} from "./world.ts";

export const defOf = (e: Entity) => RULES.entities[e.kind]!;
export const isLevitating = (e: Entity) => (e.status.levitate ?? 0) > 0;
export const massOf = (e: Entity) => (isLevitating(e) ? 0 : defOf(e).mass);

export function spawn(w: World, kind: string, x: number, y: number): Entity | undefined {
  const c = cellAt(w, x, y);
  if (!c || isBlocking(c) || entityAt(w, x, y) || !(kind in RULES.entities)) return undefined;
  const e: Entity = { id: w.nextId++, kind, x, y, hp: RULES.entities[kind]!.hp, status: {} };
  w.entities.push(e);
  return e;
}

/**
 * Hurts an entity. `element` (if the harm has one) doubles it for a raider weak to
 * it and halves it, rounded up, for one that resists it (C#: World.DamageRaider). With the sim's
 * status.shatter status (frozen), a hurt of any element but its notBy (cold) shatters it.
 */
export function damage(w: World, e: Entity, amount: number, cause: string, element?: string) {
  if (amount <= 0 || !w.entities.includes(e)) return;
  const def = defOf(e);
  if (def.hp >= 999) return; // boulders shrug
  amount = scaleDamage(def, element, amount);
  const shatter = RULES.sim.status.shatter;
  if (shatter?.when && (e.status[shatter.when] ?? 0) > 0 && (shatter.notBy === undefined || element !== shatter.notBy)) return destroy(w, e, "shatters");
  e.hp -= amount;
  if (hasComponent(def, "creature")) logOnce(w, `hurt#${e.id}#${cause}`, `${def.name} takes ${amount} ${cause} damage (${Math.max(0, e.hp)}/${def.hp} HP)`, "hurt");
  if (e.hp <= 0) destroy(w, e);
}

/** Damage after a raider's weaknesses and resistances (C#: World.Scaled). */
export function scaleDamage(def: ReturnType<typeof defOf>, element: string | undefined, amount: number): number {
  const r = def.raider;
  if (!r || element === undefined) return amount;
  if (r.weak?.includes(element)) return amount * 2;
  if (r.resist?.includes(element)) return amount - Math.floor(amount / 2);
  return amount;
}

export function destroy(w: World, e: Entity, how = "is defeated") {
  const i = w.entities.indexOf(e);
  if (i < 0) return;
  w.entities.splice(i, 1);
  const def = defOf(e);
  if (def.explodes) return explode(w, e.x, e.y, def.explodes);
  if (def.player) {
    log(w, "You're down! Press Reset to try again.", "hurt");
  } else if (hasComponent(def, "creature")) {
    log(w, `${def.name} ${how}`, "hurt");
  } else if (e.status.burning) {
    const c = cellAt(w, e.x, e.y)!;
    c.fire = Math.max(c.fire, 2);
    c.fuel = Math.max(c.fuel, 6);
    log(w, `The ${def.name.toLowerCase()} burns to cinders`, "info");
  } else {
    log(w, `The ${def.name.toLowerCase()} breaks apart`, "info");
  }
}

function explode(w: World, x: number, y: number, ex: NonNullable<ReturnType<typeof defOf>["explodes"]>) {
  log(w, "KABOOM! The oil barrel explodes", "notable");
  crater(w, x, y, ex.crater);
  for (let dy = -ex.radius; dy <= ex.radius; dy++) {
    for (let dx = -ex.radius; dx <= ex.radius; dx++) {
      const c = cellAt(w, x + dx, y + dy);
      if (!c || isBlocking(c)) continue;
      c.cover = ex.cover;
      ignite(c, "cover");
      c.fire = Math.max(c.fire, ex.fire);
    }
  }
  const hit = w.entities.filter((e) => Math.max(Math.abs(e.x - x), Math.abs(e.y - y)) <= ex.radius + 1);
  for (const e of hit) {
    damage(w, e, roll(w, ex.damage), "blast");
    if (!w.entities.includes(e)) continue;
    applyEntityElement(w, e, "fire", { power: 3, direct: false });
    if (e.x !== x || e.y !== y) push(w, e, awayDir(e.x - x, e.y - y), ex.push, false);
  }
}

export function awayDir(dx: number, dy: number, fallback: Dir = 0): Dir {
  if (dx === 0 && dy === 0) return fallback;
  if (Math.abs(dx) >= Math.abs(dy)) return dx > 0 ? 1 : 3;
  return dy > 0 ? 2 : 0;
}

/** Shoves an entity. Distance = force - mass + 1; slick ground adds a slide. */
export function push(w: World, e: Entity, dir: Dir, force: number, direct: boolean): void {
  const def = defOf(e);
  if (def.raider) return; // raiders stand their ground (the C# core has no shoves)
  const dist = force - massOf(e) + 1;
  if (dist <= 0) {
    if (direct) logOnce(w, `budge#${e.id}`, `The ${def.name.toLowerCase()} doesn't budge`, "info");
    return;
  }
  travel(w, e, dir, dist, force > 1);
}

/** Moves up to `dist` squares, then keeps sliding while the ground is slick. */
function travel(w: World, e: Entity, dir: Dir, dist: number, hard: boolean): void {
  const def = defOf(e);
  let moved = 0;
  const step = (): boolean => {
    const nx = e.x + DX[dir], ny = e.y + DY[dir];
    const n = cellAt(w, nx, ny);
    const other = entityAt(w, nx, ny);
    if (!n || isBlocking(n) || other) {
      const into = other ? defOf(other).name.toLowerCase() : n ? terrainDef(n).name.toLowerCase() : "edge";
      if (moved > 0 || hard) {
        logOnce(w, `slam#${e.id}`, `${def.name} slams into the ${into}`, "notable");
        damage(w, e, RULES.sim.push.collisionDamage, "impact");
        if (other) damage(w, other, RULES.sim.push.collisionDamage, "impact");
      }
      return false;
    }
    e.x = nx;
    e.y = ny;
    moved++;
    if (!isLevitating(e)) thorns(w, e, n);
    if (!w.entities.includes(e)) return false;
    // Falling into a pit ends the shove (unless floating).
    if (hasTag(n, "low") && !(n.frozen && n.water > 0) && !isLevitating(e)) return false;
    return true;
  };
  for (let i = 0; i < dist; i++) if (!step()) return;
  const slick = () => {
    const c = cellAt(w, e.x, e.y)!;
    return !isLevitating(e) && (isSlippery(c) || (e.status.frozen ?? 0) > 0);
  };
  if (moved > 0 && slick()) {
    const c = cellAt(w, e.x, e.y)!;
    const what = (e.status.frozen ?? 0) > 0 ? "frozen solid" : c.frozen ? "across the ice" : `across the ${RULES.covers[c.cover!]?.name.toLowerCase() ?? "ground"}`;
    logOnce(w, `slide#${e.id}`, `${def.name} slides ${what}`, "notable");
    for (let i = 0; i < RULES.sim.push.slideCells && slick(); i++) if (!step()) return;
  }
}

/**
 * A creature stepping onto a cover that hurts (thorns: Spike Growth) takes its
 * damage, and one that slows (speed below 1), or ground that does (mud), is snagged:
 * a step takes 1/speed as long, so it can't take the next one for the rest of that
 * time. Objects shrug.
 */
export function thorns(w: World, e: Entity, c: Cell) {
  if (!hasComponent(defOf(e), "creature")) return;
  const cv = c.cover !== null ? RULES.covers[c.cover] : undefined;
  if (cv?.damage) damage(w, e, roll(w, cv.damage), cv.name.toLowerCase());
  const speed = Math.min(cv?.speed ?? 1, terrainDef(c).speed ?? 1);
  if (speed > 0 && speed < 1 && w.entities.includes(e)) {
    e.status.snagged = Math.max(e.status.snagged ?? 0, Math.round(RULES.sim.ticksPerSecond * (1 / speed - 1)));
  }
}

/** One step of walking. Returns why not, or null if the step happened. */
export function walk(w: World, e: Entity, dir: Dir): string | null {
  if ((e.status.snagged ?? 0) > 0) return (terrainDef(cellAt(w, e.x, e.y)!).speed ?? 1) < 1 ? "The mud holds your feet. Pull free in a moment." : "Thorns snag you. Pull free in a moment.";
  const nx = e.x + DX[dir], ny = e.y + DY[dir];
  const n = cellAt(w, nx, ny);
  if (!n || isBlocking(n)) return n ? `The ${terrainDef(n).name.toLowerCase()} is in the way.` : "That's the edge of the map.";
  const other = entityAt(w, nx, ny);
  if (other) return `The ${defOf(other).name.toLowerCase()} is in the way.`;
  if (hasTag(n, "low") && n.water === 0 && !isLevitating(e)) return "That's a drop into a pit. Levitate yourself, fill it, or bridge it.";
  travel(w, e, dir, 1, false);
  return null;
}

/**
 * Shock-water (conduct.chainsWet): a wet creature of a material that chains wet is shocked by freshly charged
 * water beside it too, not only under it (World.ShockedFromBeside).
 */
function shockedFromBeside(w: World, e: Entity, def: (typeof RULES.entities)[string]): boolean {
  if (!((e.status.wet ?? 0) > 0) || !def.material || !RULES.terrain[def.material]?.chainsWet) return false;
  for (const [nx, ny] of neighbours(w, e.x, e.y)) {
    const n = cellAt(w, nx, ny)!;
    if (n.charge === 3 && n.water > 0 && !n.frozen) return true;
  }
  return false;
}

/** Per-tick entity upkeep: statuses, burning, wading, zaps, falls. */
export function tickEntities(w: World) {
  const s = RULES.sim;
  for (const e of [...w.entities]) {
    if (!w.entities.includes(e)) continue;
    const def = defOf(e);
    const c = cellAt(w, e.x, e.y)!;
    const lev = isLevitating(e);

    if (c.fire > 0 && !lev) applyEntityElement(w, e, "fire", { power: c.fire, direct: false });
    if (c.water > 0 && !c.frozen && !lev) applyEntityElement(w, e, "water", { power: 1, direct: false });

    if ((e.status.burning ?? 0) > 0 && w.tick % s.status.burnDamageEvery === 0) {
      damage(w, e, 1, "fire", "fire");
      if (hasComponent(def, "flammable") && w.entities.includes(e)) applyElement(w, e.x, e.y, "fire", { power: 1, direct: false });
    }
    if ((c.heat ?? 0) > 0 && hasComponent(def, "creature") && w.tick % s.status.burnDamageEvery === 0) damage(w, e, 1, "hot metal", "fire");
    if (c.gas === "steam" && c.gasAmt >= 2 && hasComponent(def, "creature") && w.tick % s.status.scaldEvery === 0) {
      damage(w, e, RULES.gases.steam?.scald ?? 1, "scalding");
    }
    if ((c.charge === 3 || shockedFromBeside(w, e, def)) && w.entities.includes(e)) {
      const mul = applyEntityElement(w, e, "lightning", { power: 1, direct: false });
      damage(w, e, s.zapDamage * mul, "lightning", "lightning");
    }
    if (!w.entities.includes(e)) continue;

    const inPit = hasTag(c, "low") && !(c.frozen && c.water > 0) && !lev;
    if (inPit && c.water === 0 && !e.status.fallen) {
      e.status.fallen = 1e9;
      log(w, `${def.name} falls into the pit`, "notable");
      damage(w, e, s.fallDamage, "falling");
    } else if (!inPit && e.status.fallen) {
      delete e.status.fallen;
    }

    for (const k of Object.keys(e.status)) {
      if (k === "fallen") continue;
      e.status[k]!--;
      if (e.status[k]! <= 0) {
        delete e.status[k];
        if (k === "levitate") log(w, `The ${def.name.toLowerCase()} drifts back down`, "info");
      }
    }
  }
}
