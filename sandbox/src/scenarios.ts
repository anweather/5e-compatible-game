// Hand-built rooms. One character per cell; see LEGEND.
import { RULES } from "./rules.ts";
import { spawn } from "./entities.ts";
import { type World, createWorld, idx } from "./world.ts";

type CellSpec = { t: string; water?: number; frozen?: boolean; cover?: string; entity?: string };

export const LEGEND: Record<string, CellSpec> = {
  "%": { t: "bedrock" },
  "#": { t: "stone" },
  ".": { t: "dirt" },
  ",": { t: "grass" },
  '"': { t: "brush" },
  T: { t: "tree" },
  "=": { t: "wood" },
  _: { t: "pit" },
  w: { t: "pit", water: 4 },
  "*": { t: "pit", water: 4, frozen: true },
  "~": { t: "dirt", water: 2 },
  m: { t: "metal" },
  o: { t: "dirt", cover: "oil" },
  G: { t: "gate" },
  B: { t: "boiler" },
  P: { t: "plate" },
  W: { t: "wheel" },
  C: { t: "coil" },
  M: { t: "windmill" },
  g: { t: "dirt", entity: "goblin" },
  k: { t: "grass", entity: "goblin" },
  h: { t: "wood", entity: "goblin" },
  c: { t: "grass", entity: "crate" },
  x: { t: "dirt", entity: "barrel" },
  b: { t: "dirt", entity: "boulder" },
  "@": { t: "grass", entity: "wizard" },
};

export interface Goal {
  kind: "breach" | "goblins";
  /** For "breach": the wall column that must get a hole in it. */
  column?: number;
}

export interface Scenario {
  id: string;
  name: string;
  intro: string;
  goal?: Goal;
  rows: string[];
}

export const SCENARIOS: Scenario[] = [
  {
    id: "steam-gate",
    name: "The Steam Gate",
    intro:
      "Get through the east wall. Five machines can open the gate: a boiler, a pressure plate, a water wheel, a lightning coil and a windmill. One stretch of wall isn't bedrock. How many ways can you find?",
    goal: { kind: "breach", column: 26 },
    rows: [
      "%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%",
      "%,,,,,,,,,,,,,............%....%",
      '%,,,,,,,,,T,,,......."""..%....%',
      '%,,,www,,,,T,,.......""B..%....%',
      "%,,wwwww,,,,,,............%....%",
      "%,,wwwww,,,,,,............%....%",
      "%,,,wwwmmmmmmmmmmmmmmmmmC.%....%",
      "%,,,,,,,,,,,,,............%....%",
      '%,,,,,,,,"",,,.......x..g.%....%',
      '%,,,,,,,,"",,@......x.....G....%',
      '%,T,,,,,,"",,,.........g..%....%',
      "%,,T,,,,~,,,,,............%....%",
      "%,,,,,,,_____W___________.%....%",
      "%,,,,,,,,,,,,,............%....%",
      "%,T,,,,,,,,c,,..........M.%....%",
      "%,,,,,,,,,,,,,............%....%",
      "%,,,,T,,,,,,,,.bwwwP......#....%",
      "%,,,,,,,,,,,,,............%....%",
      "%,,,,,,,,,,,,,............%....%",
      "%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%",
    ],
  },
  {
    id: "goblin-bridge",
    name: "Goblin Bridge",
    intro:
      "Goblins hold the bridge and the far bank. Clear them out without walking over: fire, falls, ice, oil, water and lightning all work. The chasm is dry to the north and flooded to the south.",
    goal: { kind: "goblins" },
    rows: [
      "%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%",
      "%,,,,,,,,,,,________...........%",
      "%,,,,,,,,,,,________...........%",
      "%,,,,,,,T,,,________...........%",
      "%,,T,,,,,,,,________.,,,,,,,...%",
      "%,,,,,,,,,,,________.,,,,,k,...%",
      "%,,,,,,,,,,,________.,,k,,,,...%",
      "%,,www,,,,,,________.,,,x,,,...%",
      "%,,www,,,,,,==h=====.,,,,,,,...%",
      "%,,www,,,,,,=====h==...........%",
      "%,,,,,,,,,,,________...........%",
      "%,,,,,@,,,,,________..x..g~....%",
      "%,T,,,,,,c,,________....~~~....%",
      '%,,,,,,,,,x,wwwwwwww........"".%',
      '%,,,,,,,,,,,wwwwwwww..g....."".%',
      '%,,,,T,,,,,,wwwwwwww........"".%',
      "%,,,,,,,,,,,wwwwwwww...........%",
      "%,,,,,,,,,,,wwwwwwww...........%",
      "%,,,,,,,,,,,wwwwwwww...........%",
      "%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%",
    ],
  },
  {
    id: "meadow",
    name: "Open Meadow",
    intro:
      "No goal. A pond, a metal fence, some brambles, a stone wall and a few things to knock about. Paint your own terrain and machines with the Build tools.",
    rows: [
      "%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%",
      "%,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,%",
      "%,,,,,,,,,,,,,,,,,,,,,#######,,%",
      "%,,TT,,,,,,,,,,,,,,,,,#,,,,,,,,%",
      "%,,T,,,,,,,,,,,b,,,,,,#,,,T,,,,%",
      "%,,,,,,,,g,,,,,,,,,,,,#,,,,T,,,%",
      "%,,,,,,,,,,,,,,,,,,,,,#,,,,,,,,%",
      "%,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,%",
      "%,,,,,,,,,,,,wwwww,,,,,,,,,,,,,%",
      "%,,,mmmm,,,,wwwwwww,,,,,,,,,,,,%",
      "%,,,,,,,,,,,wwwwwww,,,,,,,,,T,,%",
      "%,,,,,,,,,,,wwwwwww,,,,,,,,,,,,%",
      "%,,,,,,,,,,,,wwwww,,,,,,,,,,,,,%",
      "%,,,,,,,,,,,,,,@,,,,,,,,,,,,,,,%",
      '%,,,,,,,""",,,,,,,,,,,,,,,,,,,,%',
      '%,,,,T,,""",,,,,,,,,cx,,T,,,,,,%',
      '%,,,,,,,""",,,,,,,,,,,,,,T,,,,,%',
      "%,........,,,,,,,,,,,,,,,,,,,,,%",
      "%,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,%",
      "%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%",
    ],
  },
];

export function loadScenario(s: Scenario, seed = 1): World {
  const h = s.rows.length, w = s.rows[0]!.length;
  const world = createWorld(w, h, seed);
  s.rows.forEach((row, y) => {
    if (row.length !== w) throw new Error(`${s.id}: row ${y} is ${row.length} wide, expected ${w}`);
    [...row].forEach((ch, x) => {
      const spec = LEGEND[ch];
      if (!spec) throw new Error(`${s.id}: unknown map character '${ch}' at ${x},${y}`);
      if (!(spec.t in RULES.terrain)) throw new Error(`${s.id}: legend '${ch}' names unknown terrain ${spec.t}`);
      const c = world.cells[idx(world, x, y)]!;
      c.t = spec.t;
      c.water = spec.water ?? 0;
      c.frozen = spec.frozen ?? false;
      c.cover = spec.cover ?? null;
      if (spec.entity) spawn(world, spec.entity, x, y);
    });
  });
  return world;
}

/** `via` names how a breach happened: a machine name, "tunnel" or "blink". */
export type GoalState = { done: boolean; text: string; via?: string };

export function goalState(w: World, s: Scenario): GoalState | null {
  if (!s.goal) return null;
  if (s.goal.kind === "goblins") {
    const total = s.rows.join("").split("").filter((ch) => LEGEND[ch]?.entity === "goblin").length;
    const left = w.entities.filter((e) => e.kind === "goblin").length;
    return left === 0
      ? { done: true, text: `All ${total} goblins are down.` }
      : { done: false, text: `${total - left} of ${total} goblins down` };
  }
  const col = s.goal.column!;
  for (let y = 0; y < w.h; y++) {
    const t = w.cells[idx(w, col, y)]!.t;
    if (t === "gate_open") return { done: true, via: w.openedBy ?? "machine", text: `Through! The ${(w.openedBy ?? "machine").toLowerCase()} opened the gate.` };
    if (!RULES.terrain[t]!.tags.includes("blocking")) return { done: true, via: "tunnel", text: "Through! You dug a way past the gate." };
  }
  const p = w.entities.find((e) => RULES.entities[e.kind]?.player);
  if (p && p.x > col) return { done: true, via: "blink", text: "Through! You blinked between the gate bars." };
  return { done: false, text: "The east wall still stands" };
}
