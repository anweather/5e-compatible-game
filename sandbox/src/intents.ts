// Everything a player can do, as a small JSON message. Solo or online, every
// change to the world goes through applyIntent, so all pages stay identical.
import { RULES, composeRules, installRules, validateRules, type ModPatch } from "./rules.ts";
import { carry, castAt, paint, placeCritter } from "./actions.ts";
import { spawn, walk } from "./entities.ts";
import { SCENARIOS, loadScenario } from "./scenarios.ts";
import {
  type Dir, type World,
  DX, DY, cellAt, dirFrom, entityAt, hasTag, isBlocking, log, playerOf, sanitizeWorld,
} from "./world.ts";

export type Intent =
  | { k: "walk"; d: Dir }
  | { k: "cast"; id: string; x: number; y: number }
  | { k: "carry"; fx: number; fy: number; tx: number; ty: number }
  | { k: "critter"; id: string; x: number; y: number }
  | { k: "paint"; b: string; cells: [number, number][] }
  | { k: "load"; id: string; seed: number }
  | { k: "play"; on: boolean }
  | { k: "speed"; v: number }
  | { k: "step" }
  | { k: "mods"; ids: string[] }
  | { k: "join"; peer: string }
  | { k: "leave"; peer: string };

const isInt = (v: unknown, lo = -1, hi = 512): v is number => Number.isInteger(v) && (v as number) >= lo && (v as number) <= hi;
const isId = (v: unknown): v is string => typeof v === "string" && /^[A-Za-z0-9_.:-]{1,64}$/.test(v);

/** Checks a message from another page before it goes anywhere. Null = drop it. */
export function sanitizeIntent(raw: unknown): Intent | null {
  if (!raw || typeof raw !== "object") return null;
  const m = raw as Record<string, unknown>;
  switch (m.k) {
    case "walk": return isInt(m.d, 0, 3) ? { k: "walk", d: m.d as Dir } : null;
    case "cast": return isId(m.id) && isInt(m.x) && isInt(m.y) ? { k: "cast", id: m.id, x: m.x, y: m.y } : null;
    case "carry": return isInt(m.fx) && isInt(m.fy) && isInt(m.tx) && isInt(m.ty) ? { k: "carry", fx: m.fx, fy: m.fy, tx: m.tx, ty: m.ty } : null;
    case "critter": return isId(m.id) && isInt(m.x) && isInt(m.y) ? { k: "critter", id: m.id, x: m.x, y: m.y } : null;
    case "paint": {
      if (!isId(m.b) || !Array.isArray(m.cells) || m.cells.length > 300) return null;
      const cells = m.cells.filter((c): c is [number, number] => Array.isArray(c) && isInt(c[0]) && isInt(c[1]));
      return { k: "paint", b: m.b, cells: cells.map(([x, y]) => [x, y]) };
    }
    case "load": return isId(m.id) && isInt(m.seed, 0, 2 ** 32) ? { k: "load", id: m.id, seed: m.seed } : null;
    case "play": return typeof m.on === "boolean" ? { k: "play", on: m.on } : null;
    case "speed": return m.v === 0.5 || m.v === 1 || m.v === 3 ? { k: "speed", v: m.v } : null;
    case "step": return { k: "step" };
    case "mods": return Array.isArray(m.ids) && m.ids.length <= 12 && m.ids.every(isId) ? { k: "mods", ids: [...m.ids] } : null;
    default: return null; // join/leave come only from the host itself
  }
}

/** Where a scenario's '@' is, or the middle of the map. */
function spawnPoint(w: World): [number, number] {
  const s = SCENARIOS.find((sc) => sc.id === w.scenarioId);
  if (s) for (let y = 0; y < s.rows.length; y++) {
    const x = s.rows[y]!.indexOf("@");
    if (x >= 0) return [x, y];
  }
  return [Math.floor(w.w / 2), Math.floor(w.h / 2)];
}

/** Puts a wizard on the board for `peer`, claiming an unowned one first. */
export function joinPlayer(w: World, peer: string): void {
  if (playerOf(w, peer)) return;
  const free = w.entities.find((e) => RULES.entities[e.kind]?.player && !e.owner);
  if (free) {
    free.owner = peer;
    return;
  }
  // Breadth-first from the spawn point to the nearest open, dry square.
  const [sx, sy] = spawnPoint(w);
  const seen = new Set([`${sx},${sy}`]);
  const queue: [number, number][] = [[sx, sy]];
  while (queue.length) {
    const [x, y] = queue.shift()!;
    const c = cellAt(w, x, y)!;
    if (!isBlocking(c) && !hasTag(c, "low") && !entityAt(w, x, y)) {
      const e = spawn(w, "wizard", x, y);
      if (e) e.owner = peer;
      log(w, "A new wizard arrives", "info");
      return;
    }
    for (let d = 0; d < 4; d++) {
      const nx = x + DX[d]!, ny = y + DY[d]!, key = `${nx},${ny}`;
      if (!seen.has(key) && cellAt(w, nx, ny)) {
        seen.add(key);
        queue.push([nx, ny]);
      }
    }
  }
}

/** Applies a set of mods to the live rules. Returns an error, or null if they're in. */
export function applyMods(w: World, ids: string[], patches: ModPatch[]): string | null {
  const rules = composeRules(patches);
  const errors = validateRules(rules);
  if (errors.length) return `Those mods don't fit together: ${errors.slice(0, 3).join("; ")}`;
  installRules(rules);
  w.mods = [...ids];
  sanitizeWorld(w);
  return null;
}

export interface ApplyEnv {
  /** Patches for the ids in a "mods" intent, already fetched, in the same order. */
  patches?: ModPatch[];
}

/**
 * Applies one intent from `from`. Returns the world to continue with (a
 * different object after "load") and a message for that player, if any.
 */
export function applyIntent(w: World, from: string, it: Intent, env: ApplyEnv = {}): { world: World; notice: string | null } {
  const me = playerOf(w, from);
  const down = "You're down. Press Reset to try again.";
  switch (it.k) {
    case "walk":
      return { world: w, notice: me ? walk(w, me, it.d) : down };
    case "cast":
      return { world: w, notice: castAt(w, it.id, it.x, it.y, from) };
    case "carry":
      return { world: w, notice: carry(w, it.fx, it.fy, it.tx, it.ty) };
    case "critter": {
      const facing: Dir = (me && dirFrom(me.x, me.y, it.x, it.y)) ?? 1;
      placeCritter(w, it.id, it.x, it.y, facing);
      return { world: w, notice: null };
    }
    case "paint":
      for (const [x, y] of it.cells) paint(w, it.b, x, y);
      return { world: w, notice: null };
    case "load": {
      const s = SCENARIOS.find((sc) => sc.id === it.id);
      if (!s) return { world: w, notice: "That scenario doesn't exist." };
      const owners = w.entities.filter((e) => e.owner).map((e) => e.owner!).sort();
      const next = loadScenario(s, it.seed);
      next.mods = w.mods;
      next.ctl = { ...w.ctl };
      sanitizeWorld(next);
      for (const o of owners) joinPlayer(next, o);
      return { world: next, notice: null };
    }
    case "play":
      w.ctl.playing = it.on;
      return { world: w, notice: null };
    case "speed":
      w.ctl.speed = it.v;
      return { world: w, notice: null };
    case "step":
      return { world: w, notice: null };
    case "mods": {
      const err = applyMods(w, it.ids, env.patches ?? []);
      if (!err) log(w, it.ids.length ? `Rules changed: ${it.ids.length} mod${it.ids.length > 1 ? "s" : ""} on` : "Rules back to the base tables", "power");
      return { world: w, notice: err };
    }
    case "join":
      joinPlayer(w, it.peer);
      return { world: w, notice: null };
    case "leave": {
      const p = playerOf(w, it.peer);
      if (p) w.entities.splice(w.entities.indexOf(p), 1);
      return { world: w, notice: null };
    }
  }
}
