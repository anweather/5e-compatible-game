// One tick of the world. Order matters and is fixed:
// critters -> fire -> water -> gas -> weather -> entities -> machines -> decay
// -> the day clock (Hasten Time).
import { RULES } from "./rules.ts";
import { applyElement, displaceWater, ignite } from "./react.ts";
import { massOf, push, tickEntities } from "./entities.ts";
import { tickDefense } from "./defense.ts";
import {
  type Cell, type World,
  DX, DY, addGas, cellAt, coverHasTag, elevation, entityAt, flammability, hasTag, idx, isBlocking, isRaisedWall,
  log, logOnce, neighbours, rand, terrainDef, windDir,
} from "./world.ts";
import { type WeatherNow, heldWeather, rainOf, snowOf, weatherAt, wetOf } from "./sky.ts";
import { METEOR_SPELL, landSpell, spellById } from "./actions.ts";

export function step(w: World): void {
  w.tick++;
  w.logged.clear();
  w.strikes = [];
  endHeldWeather(w);
  w.weather = skyNow(w);
  tickCritters(w);
  tickFire(w);
  tickWater(w);
  tickGas(w);
  tickWeather(w);
  tickEntities(w);
  tickDefense(w);
  tickMachines(w);
  for (let i = 0; i < w.cells.length; i++) {
    const c = w.cells[i]!;
    if (c.charge > 0) c.charge--;
    if (c.life > 0 && --c.life === 0) ripen(w, i, c);
  }
  if (w.wind && --w.wind.ticks <= 0) w.wind = null;
  tickClock(w);
  w.marks.flow.fill(0);
  w.marks.steam.fill(0);
  w.marks.wind.fill(0);
}

/** The weather this tick: the held weather while Control Weather lasts, else the seed's (C#: World.SkyNow). */
export function skyNow(w: World): WeatherNow {
  const natural = weatherAt(RULES.sim, w.seed, w.tick);
  if (!w.weatherSet) return natural;
  const { kind, from, until } = w.weatherSet;
  return heldWeather(RULES.sim, w.seed, kind, from, until, w.tick, natural.period);
}

/** At the start of a tick: held weather whose time is up gives way to the natural weather. */
function endHeldWeather(w: World) {
  if (!w.weatherSet || w.tick < w.weatherSet.until) return;
  w.weatherSet = null;
  log(w, "The weather goes its own way again", "info");
}

/** At the end of a tick: a hastened clock runs `rate` ticks of day for this one. */
function tickClock(w: World) {
  if (!w.haste) return;
  w.clockAhead += w.haste.rate - 1;
  if (w.haste.ticks > 1) {
    w.haste.ticks--;
    return;
  }
  w.haste = null;
  log(w, "Time slows back to its own pace", "info");
}

/**
 * A cell's life ran out (World.Ripen): a cover that lasts (thorns) withers away,
 * and a terrain that grows (green shoots, a sapling) becomes its growsTo. While it
 * burns it waits a tick at a time; a tree waits while something stands there, and
 * pushes the water out of wet ground as it grows.
 */
function ripen(w: World, i: number, c: Cell) {
  if (c.fire > 0) {
    c.life = 1;
    return;
  }
  const id = c.cover, cv = id !== null ? RULES.covers[id] : undefined;
  if (cv && (cv.lasts ?? 0) > 0) {
    c.cover = null;
    if (cv.witherLog) logOnce(w, `wither-${id}`, cv.witherLog);
  }
  const to = terrainDef(c).growsTo;
  const grown = to !== undefined ? RULES.terrain[to] : undefined;
  if (!to || !grown) return;
  const x = i % w.w, y = Math.floor(i / w.w);
  if (grown.tags.includes("blocking")) {
    if (entityAt(w, x, y)) {
      c.life = 1;
      return;
    }
    displaceWater(w, x, y);
    c.cover = null;
  }
  const line = terrainDef(c).growLog;
  c.t = to;
  if (line) logOnce(w, `grow-${to}`, line);
}

export function emitCritter(w: World, kind: string, x: number, y: number, dir: 0 | 1 | 2 | 3) {
  const def = RULES.critters.find((c) => c.id === kind)!;
  const ctx = { power: def.power ?? 1, dir, direct: false };
  if (def.directional) {
    for (let k = 1; k <= (def.length ?? 3); k++) {
      const cx = x + DX[dir] * k, cy = y + DY[dir] * k;
      const c = cellAt(w, cx, cy);
      if (!c) break;
      applyElement(w, cx, cy, def.element, ctx);
      const e = entityAt(w, cx, cy);
      if (e && def.element === "air" && massOf(e) === 0) push(w, e, dir, 1, false);
      if (isBlocking(c)) break;
    }
    return;
  }
  const r = def.radius ?? 0;
  for (let dy = -r; dy <= r; dy++) for (let dx = -r; dx <= r; dx++) applyElement(w, x + dx, y + dy, def.element, ctx);
}

function tickCritters(w: World) {
  for (const cr of w.critters) {
    const def = RULES.critters.find((c) => c.id === cr.kind);
    if (!def || def.mode !== "emit") continue;
    if (--cr.timer > 0) continue;
    cr.timer = def.every ?? 6;
    emitCritter(w, cr.kind, cr.x, cr.y, cr.dir);
  }
}

function tickFire(w: World) {
  const p = RULES.sim.fire;
  const burning: number[] = [];
  w.cells.forEach((c, i) => c.fire > 0 && burning.push(i));
  for (const i of burning) {
    const c = w.cells[i]!;
    if (c.fire === 0) continue;
    const x = i % w.w, y = Math.floor(i / w.w);
    // Water puts out anything but a floating oil or grease fire.
    if (c.water > 0 && !c.frozen && !coverHasTag(c, "flammable")) {
      c.fire = 0;
      c.fuel = 0;
      addGas(c, "steam", 1);
      continue;
    }
    if (doused(w, c)) continue;
    if (c.gas !== "steam" && rand(w) < p.smokeChance) addGas(c, "smoke", 1);
    for (const [nx, ny, d] of neighbours(w, x, y)) {
      const n = cellAt(w, nx, ny)!;
      if (n.fire > 0) continue;
      const flam = flammability(n);
      const downwind = windDir(w) === d ? p.downwindBonus : 0;
      const wet = n.water > 0 && !n.frozen;
      const chance = flam > 0 ? (p.spreadChance * flam * (c.fire / 3) + downwind * flam) * rainDamp(w) : wet ? p.boilChance : p.heatChance;
      if (rand(w) < chance) applyElement(w, nx, ny, "fire", { power: 1, direct: false });
    }
    if (--c.fuel > 0) continue;
    if (coverHasTag(c, "flammable")) {
      c.cover = null;
      const t = terrainDef(c);
      if (t.tags.includes("flammable")) {
        c.fuel = t.fuel ?? 5;
        c.fire = 2;
      } else {
        c.fire = 0;
        if (!isBlocking(c)) c.cover = "ash";
      }
      continue;
    }
    const t = terrainDef(c);
    c.fire = 0;
    if (t.burnsTo) {
      c.t = t.burnsTo;
      c.cover = isBlocking(c) || hasTag(c, "low") ? null : "ash";
      if (t.burnLog) log(w, t.burnLog, "notable");
    }
  }
}

/** A spring's square (a river) refills to its level each tick, unless it is ice (World.Springs). */
function springs(w: World) {
  const max = RULES.sim.water.maxLevel;
  for (const c of w.cells) {
    const level = terrainDef(c).spring ?? 0;
    if (level > 0 && !c.frozen && c.water < level) c.water = Math.min(level, max);
  }
}

function tickWater(w: World) {
  springs(w);
  const n = w.cells.length;
  const forward = w.tick % 2 === 0;
  for (let k = 0; k < n; k++) {
    const i = forward ? k : n - 1 - k;
    const c = w.cells[i]!;
    if (c.water === 0 || c.frozen || isBlocking(c)) continue;
    const x = i % w.w, y = Math.floor(i / w.w);
    const level = elevation(c) + c.water;
    // Water always runs onto lower ground; on the flat it needs a 2-step
    // difference, so a still puddle doesn't slosh back and forth forever.
    let best = -1, bestScore = 0;
    for (const [nx, ny] of neighbours(w, x, y)) {
      const nc = cellAt(w, nx, ny)!;
      if (nc.frozen && nc.water > 0) continue;
      if ((hasTag(nc, "blocking") && !hasTag(nc, "vessel")) || isRaisedWall(nc)) continue;
      if (nc.water >= RULES.sim.water.maxLevel) continue;
      const drop = level - (elevation(nc) + nc.water);
      const downhill = elevation(nc) < elevation(c);
      if (!(drop >= 2 || (downhill && drop >= 1))) continue;
      const score = drop + (downhill ? 0.5 : 0);
      if (score > bestScore) {
        bestScore = score;
        best = idx(w, nx, ny);
      }
    }
    if (best >= 0) {
      c.water--;
      w.cells[best]!.water++;
      w.marks.flow[i] = 1;
      w.marks.flow[best] = 1;
    }
  }
}

function tickGas(w: World) {
  const p = RULES.sim.gas;
  for (let i = 0; i < w.cells.length; i++) {
    const c = w.cells[i]!;
    if (!c.gas) continue;
    if (rand(w) < (RULES.gases[c.gas]?.decay ?? p.decayChance)) c.gasAmt--;
    if (c.gasAmt <= 0) {
      c.gas = null;
      c.gasAmt = 0;
      continue;
    }
    const x = i % w.w, y = Math.floor(i / w.w);
    const wd = windDir(w);
    if (wd >= 0 && rand(w) < 0.5) {
      const t = cellAt(w, x + DX[wd]!, y + DY[wd]!);
      if (t && !isBlocking(t)) {
        addGas(t, c.gas, 1);
        c.gasAmt--;
        if (c.gasAmt <= 0) c.gas = null;
        continue;
      }
    }
    if (c.gasAmt >= 2) {
      const opts = [...neighbours(w, x, y)].map(([nx, ny]) => cellAt(w, nx, ny)!).filter(
        (nc) => !isBlocking(nc) && (nc.gas === null || nc.gas === c.gas) && nc.gasAmt < c.gasAmt - 1,
      );
      if (opts.length) {
        addGas(opts[Math.floor(rand(w) * opts.length)]!, c.gas, 1);
        c.gasAmt--;
      }
    }
  }
}

function tickMachines(w: World) {
  let poweredBy: string | null = null;
  for (let i = 0; i < w.cells.length; i++) {
    const c = w.cells[i]!;
    const def = RULES.machines[c.t];
    if (!def) continue;
    const m = (w.machines[i] ??= { level: 0, hold: 0, powered: false });
    const x = i % w.w, y = Math.floor(i / w.w);
    switch (def.needs) {
      case "steam": {
        if (w.marks.steam[i]) m.level = Math.min(def.max ?? 10, m.level + 1);
        else if (w.tick % (def.decayEvery ?? 8) === 0) m.level = Math.max(0, m.level - 1);
        m.powered = m.powered ? m.level > 0 : m.level >= (def.build ?? 4);
        break;
      }
      case "mass": {
        const e = entityAt(w, x, y);
        m.level = e ? massOf(e) : 0;
        m.powered = m.level >= (def.mass ?? 3);
        break;
      }
      default: {
        const hit =
          def.needs === "flow" ? w.marks.flow[i] : def.needs === "wind" ? w.marks.wind[i] : c.charge > 0 ? 1 : 0;
        m.hold = hit ? (def.hold ?? 10) : Math.max(0, m.hold - 1);
        m.level = m.hold;
        m.powered = m.hold > 0;
      }
    }
    if (m.powered && !poweredBy) poweredBy = def.name;
  }
  setGates(w, poweredBy);
}

function setGates(w: World, poweredBy: string | null) {
  const open = poweredBy !== null;
  if (open === w.gatesOpen) return;
  let changed = false;
  w.cells.forEach((c, i) => {
    if (open && c.t === "gate") {
      c.t = "gate_open";
      changed = true;
    } else if (!open && c.t === "gate_open") {
      const x = i % w.w, y = Math.floor(i / w.w);
      if (entityAt(w, x, y)) return; // something is wedged in it
      c.t = "gate";
      changed = true;
    }
  });
  w.gatesOpen = open;
  if (!changed) return;
  if (open) {
    w.openedBy = poweredBy;
    log(w, `The gate grinds open, powered by the ${poweredBy!.toLowerCase()}`, "power");
  } else {
    log(w, "The machines fall quiet and the gate slams shut", "power");
  }
}

// Re-exported so callers can light things without importing react.ts.
export { ignite };

// ---------- weather ----------

// What the weather does to cells, after gas: rain fills squares and dry spells
// dry shallow ones, snow freezes shallow water, covers open ground and drifts
// into banks against walls and trees (and dry spells thaw the ice and melt the
// snow), storms strike lightning (World.Weather.cs ports this).
// Nothing happens during the calm stretches the world starts with.
function tickWeather(w: World) {
  const now = w.weather, p = RULES.sim.weather;
  w.newMeteors = [];
  landMeteors(w);
  if (now.period < p.calmPeriods && !w.weatherSet) return;
  const rain = rainOf(now), snow = snowOf(now);
  if (rain > 0) {
    if (w.tick % p.rain.fillEvery === 0) fill(w, (p.rain.fillChance * rain) / 100);
  } else if (snow > 0) {
    if (w.tick % p.snow.freezeEvery === 0) {
      freeze(w, (p.snow.freezeChance * snow) / 100, true);
      settle(w, (p.snow.coverChance * snow) / 100, (p.snow.bankChance * snow) / 100);
    }
  } else if (w.tick % p.rain.dryEvery === 0) {
    dry(w, p.rain.dryChance);
    freeze(w, p.snow.thawChance, false);
    melt(w, p.snow.thawChance);
  }
  if (now.kind === "storm" && w.tick % p.storm.strikeEvery === 0 && rand(w) < (p.storm.strikeChance * now.strength) / 100) strike(w);
  if (now.meteors > 0 && w.tick % p.meteors.every === 0 && rand(w) < (p.meteors.chance * now.meteors) / 100) meteor(w);
}

function meteor(w: World) {
  const x = Math.floor(rand(w) * w.w), y = Math.floor(rand(w) * w.h);
  const m: [number, number, number] = [x, y, w.tick + RULES.sim.weather.meteors.fallTicks];
  w.falling.push(m);
  w.newMeteors.push(m);
}

/** Meteors whose tick has come land as the Meteor spell, in the order they appeared (World.LandMeteors). */
function landMeteors(w: World) {
  if (!w.falling.length) return;
  const spell = spellById(METEOR_SPELL);
  for (const [x, y, lands] of w.falling) {
    if (lands > w.tick || !spell) continue;
    log(w, "A meteor strikes", "notable");
    landSpell(w, spell, x, y, 0, undefined, 1);
  }
  w.falling = w.falling.filter(([, , lands]) => lands > w.tick);
}

function fill(w: World, chance: number) {
  const max = RULES.sim.water.maxLevel;
  for (const c of w.cells) {
    if (isBlocking(c) || c.water >= max) continue;
    if (rand(w) < chance) c.water++;
  }
}

/** A unit of water off squares two units deep or less (not ice): puddles dry, lakes stay. */
function dry(w: World, chance: number) {
  for (const c of w.cells) {
    if (c.water === 0 || c.water > 2 || c.frozen) continue;
    if (rand(w) < chance) c.water--;
  }
}

/** Snow freezes the water on squares two units deep or less; without it, that ice thaws. Lakes are left alone. */
function freeze(w: World, chance: number, frozen: boolean) {
  for (const c of w.cells) {
    if (c.water === 0 || c.water > 2 || !!c.frozen === frozen) continue;
    if (rand(w) < chance) c.frozen = frozen;
  }
}

/** The covers snow lays and drifts into (materials.json; C#: World.SnowCover, BankCover). */
const SNOW = "snow", BANK = "snowbank";

/** Snow settles on open, dry, unburnt ground, and drifts into banks where it's sheltered (World.Settle). */
function settle(w: World, cover: number, bank: number) {
  const wind = windDir(w), upwind = wind >= 0 ? (wind + 2) % 4 : -1;
  for (let i = 0; i < w.cells.length; i++) settleOn(w, i, upwind, cover, bank);
}

/** Snow on one square: a cover on open ground, a bank where it's sheltered (World.SettleOn). */
function settleOn(w: World, i: number, upwind: number, cover: number, bank: number) {
  const c = w.cells[i];
  if (c.water > 0 || c.fire > 0 || isBlocking(c)) return;
  if (c.cover === null) {
    if (rand(w) < cover) c.cover = SNOW;
  } else if (c.cover === SNOW && sheltered(w, i % w.w, Math.floor(i / w.w), upwind) && rand(w) < bank) c.cover = BANK;
}

/** Whether something blocking stands next to a square: on its upwind side in a wind, any side without. */
function sheltered(w: World, x: number, y: number, upwind: number): boolean {
  for (let d = 0; d < 4; d++) {
    if (upwind >= 0 && d !== upwind) continue;
    const n = cellAt(w, x + DX[d], y + DY[d]);
    if (n && isBlocking(n)) return true;
  }
  return false;
}

/** Banks sink back to snow, and snow melts into a puddle (World.Melt). */
function melt(w: World, chance: number) {
  for (const c of w.cells) meltOn(w, c, chance);
}

/** One square's snow: a bank sinks to snow, snow melts into a puddle (World.MeltOn). */
function meltOn(w: World, c: Cell, chance: number) {
  if (c.cover !== SNOW && c.cover !== BANK) return;
  if (rand(w) >= chance) return;
  if (c.cover === BANK) {
    c.cover = SNOW;
    return;
  }
  c.cover = null;
  if (c.water === 0) c.water = 1;
}

function strike(w: World) {
  const x = Math.floor(rand(w) * w.w), y = Math.floor(rand(w) * w.h);
  w.strikes.push([x, y]);
  log(w, "Lightning strikes", "notable");
  applyElement(w, x, y, "lightning", { power: 2, direct: false });
}

/** Rain (or snow, by half) may burn a fire down a step; true if it went out. */
function doused(w: World, c: Cell): boolean {
  const rain = wetOf(w.weather);
  if (rain === 0) return false;
  if (rand(w) >= (RULES.sim.weather.rain.douseChance * rain) / 100) return false;
  if (--c.fire > 0) return false;
  addGas(c, "steam", 1);
  logOnce(w, "rain-douse", "The rain puts out a fire");
  return true;
}

/** How much of fire's spread the rain (or snow, by half) leaves, 1 when dry. */
const rainDamp = (w: World) => 1 - (RULES.sim.weather.rain.dampen * wetOf(w.weather)) / 100;
