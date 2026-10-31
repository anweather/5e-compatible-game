// The Gauntlet's rules (docs/BACKLOG.md 25, docs/handoffs/obstacle-course-brief.md): spell zones, and an obstacle
// course's gate, checkpoints and bell, with a clock and a best time per player. The C# core's World.Course.cs ports
// this line for line.
//
// A spell zone is a block of cells that allows only some elements (every element of the combo that casts the spell)
// or some spells by id: a cast landing in it with anything else fizzles. Zones are world data, the same on every
// peer, and in the hash. The course (world/gauntlet.json) is a gate, booths in a row (each a zone with a checkpoint
// cell) and a bell. courseStep says a player stands on a cell: the gate starts the player's clock, each booth further
// on is the checkpoint reached, and the bell stops the clock and keeps the best time. A wizard downed inside the
// course gets up at its last checkpoint (courseRespawn), not the yard. All whole numbers, in ticks.
import { RULES, type SpellDef } from "./rules.ts";
import { type World, log } from "./world.ts";

/** A block of cells, its corners inclusive (C#: CellRect, written [x0, y0, x1, y1]). */
export interface CellRect {
  x0: number;
  y0: number;
  x1: number;
  y1: number;
}

/** An area that allows only some elements or spells (C#: SpellZone): anything else cast into it fizzles. */
export interface SpellZone {
  id: string;
  name: string;
  area: CellRect;
  /** Elements allowed: a spell is, if a combo of only these elements casts it. */
  elements: string[];
  /** Spells allowed by id, whatever their elements (Misty Step has none). */
  spells: string[];
}

/** world/gauntlet.json (C#: CourseLayout): the course's area, its gate, its booths in order and its bell. */
export interface CourseLayout {
  name: string;
  area: CellRect;
  gate: CellRect;
  bell: CellRect;
  /** Where a wizard downed before the first booth gets up: the gate's middle. */
  startX: number;
  startY: number;
  booths: { zone: SpellZone; checkX: number; checkY: number }[];
}

/** One player's run (C#: CourseRun): the last checkpoint reached (-1 the gate), the clock's start (-1 not running), the best and last times in ticks (0 none). */
export interface CourseRun {
  player: string;
  checkpoint: number;
  start: number;
  best: number;
  last: number;
}

const contains = (r: CellRect, x: number, y: number) => x >= r.x0 && x <= r.x1 && y >= r.y0 && y <= r.y1;
const rectText = (r: CellRect) => `${r.x0}:${r.y0}:${r.x1}:${r.y1}`;

function rect(v: unknown, what: string): CellRect {
  if (!Array.isArray(v) || v.length !== 4) throw new Error(`gauntlet.json: ${what} needs [x0, y0, x1, y1]`);
  const [x0, y0, x1, y1] = v as number[];
  return { x0: Math.min(x0!, x1!), y0: Math.min(y0!, y1!), x1: Math.max(x0!, x1!), y1: Math.max(y0!, y1!) };
}

/** The course in world/gauntlet.json's shape (C#: CourseLayout.FromJson). */
export function courseFromJson(doc: Record<string, unknown>): CourseLayout {
  const gate = rect(doc.gate, "the gate");
  const booths = ((doc.booths ?? []) as Record<string, unknown>[]).map((b, i) => {
    const id = (b.id as string | undefined) ?? `booth ${i + 1}`;
    const cp = b.checkpoint as number[] | undefined;
    if (!Array.isArray(cp) || cp.length !== 2) throw new Error(`gauntlet.json: ${id} needs "checkpoint": [x, y]`);
    const allow = (b.allow ?? {}) as { elements?: string[]; spells?: string[] };
    const zone: SpellZone = { id, name: (b.name as string | undefined) ?? id, area: rect(b.area, id), elements: [...(allow.elements ?? [])], spells: [...(allow.spells ?? [])] };
    return { zone, checkX: cp[0]!, checkY: cp[1]! };
  });
  return {
    name: (doc.name as string | undefined) ?? "the course",
    area: rect(doc.area, "the area"),
    gate,
    bell: rect(doc.bell, "the bell"),
    startX: gate.x0 + Math.trunc((gate.x1 - gate.x0) / 2),
    startY: gate.y0 + Math.trunc((gate.y1 - gate.y0) / 2),
    booths,
  };
}

/** Adds a spell zone anywhere in the valley (World.AddZone). World data: every peer adds the same, before the first tick. */
export function addZone(w: World, zone: SpellZone): void {
  w.zones.push(zone);
}

/** Lays the course out (World.SetCourse): its booths become spell zones. World data, like addZone. */
export function setCourse(w: World, course: CourseLayout): void {
  if (w.course) return;
  w.course = course;
  for (const b of course.booths) w.zones.push(b.zone);
}

/** The zone that smothers this spell cast at (x, y) (World.ZoneAgainst), or undefined when it may go off there. */
export function zoneAgainst(w: World, s: SpellDef, x: number, y: number): SpellZone | undefined {
  return w.zones.find((z) => contains(z.area, x, y) && !allows(z, s.id));
}

// A spell is allowed by id, or when a combo of only the zone's elements casts it (or holds into it).
function allows(z: SpellZone, spell: string): boolean {
  if (z.spells.includes(spell)) return true;
  const c = RULES.combos;
  const combos = [
    ...Object.entries(c.singles).map(([first, def]) => ({ els: [first], def })),
    ...Object.entries(c.pairs).flatMap(([first, row]) => Object.entries(row).map(([second, def]) => ({ els: [first, second], def }))),
  ];
  return combos.some(({ els, def }) => (def.spell === spell || def.held === spell) && els.every((e) => z.elements.includes(e)));
}

/**
 * A player stands on (x, y) (World.ApplyCourse): the gate (re)starts their clock, a booth further on than their last is
 * the checkpoint reached, and the bell stops the clock.
 */
export function courseStep(w: World, player: string, x: number, y: number): void {
  const course = w.course;
  if (!course || !player || !contains(course.area, x, y)) return;
  const run = (w.courseRuns[player] ??= { player, checkpoint: -1, start: -1, best: 0, last: 0 });
  if (contains(course.gate, x, y)) {
    if (run.start < 0) log(w, `${player} is through the gate: the clock is running`, "notable");
    run.start = w.tick;
    run.checkpoint = -1;
    return;
  }
  if (run.start < 0) return;
  if (contains(course.bell, x, y)) return ringBell(w, run);
  for (let i = run.checkpoint + 1; i < course.booths.length; i++) {
    if (!contains(course.booths[i]!.zone.area, x, y)) continue;
    run.checkpoint = i;
    log(w, `${player} reaches the checkpoint at the ${course.booths[i]!.zone.name}`);
    return;
  }
}

function ringBell(w: World, run: CourseRun): void {
  const time = Math.max(1, w.tick - run.start);
  const best = run.best === 0 || time < run.best;
  run.start = -1;
  run.last = time;
  if (best) run.best = time;
  log(w, best ? `${run.player} rings the bell in ${courseSeconds(time)}: a new best!` : `${run.player} rings the bell in ${courseSeconds(time)} (best ${courseSeconds(run.best)})`, "notable");
}

/** Ticks as seconds to a tenth, "42.5 s" (World.Seconds). */
export function courseSeconds(ticks: number): string {
  const tps = Math.max(1, RULES.sim.ticksPerSecond), tenths = Math.trunc((ticks * 10 + Math.trunc(tps / 2)) / tps);
  return `${Math.trunc(tenths / 10)}.${tenths % 10} s`;
}

/**
 * Where a wizard downed at (x, y) gets up (World.CourseRespawn): inside the course on a run, the last checkpoint
 * reached (the gate before the first); otherwise undefined, the yard as ever.
 */
export function courseRespawn(w: World, player: string, x: number, y: number): [number, number] | undefined {
  const course = w.course;
  if (!course || !contains(course.area, x, y)) return undefined;
  const run = w.courseRuns[player];
  if (!run || run.start < 0) return undefined;
  if (run.checkpoint < 0) return [course.startX, course.startY];
  const b = course.booths[run.checkpoint]!;
  return [b.checkX, b.checkY];
}

/** The zones as text, "" with none (World.ZoneText). */
export const zoneState = (w: World): string => w.zones.map((z) => `|z${z.id}:${rectText(z.area)}:${z.elements.join(",")}:${z.spells.join(",")}`).join("");

/** The players' runs as text, by player in ordinal order, "" before anyone ran (World.CourseText). */
export const courseState = (w: World): string =>
  Object.keys(w.courseRuns).sort((a, b) => (a < b ? -1 : a > b ? 1 : 0)).map((p) => {
    const r = w.courseRuns[p]!;
    return `|r${r.player}:${r.checkpoint}:${r.start}:${r.best}:${r.last}`;
  }).join("");
