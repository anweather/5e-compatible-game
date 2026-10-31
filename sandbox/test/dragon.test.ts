// The Dragon's rules (docs/BACKLOG.md 26). Mirrors the C# port's DragonTests
// (unity/Assets/Prototype/Chemistry/Tests/DragonTests.cs), goldens included.
import { test } from "node:test";
import assert from "node:assert/strict";
import { RULES, validateRules } from "../src/rules.ts";
import { step } from "../src/sim.ts";
import { applyIntent, sanitizeIntent } from "../src/intents.ts";
import { defenseText, startDefense } from "../src/defense.ts";
import { spawn } from "../src/entities.ts";
import { startGolf } from "../src/golf.ts";
import { SCENARIOS, loadScenario } from "../src/scenarios.ts";
import { breathBox, caughtByBreath, keepStart, shelteredFromBreath, summonDragon } from "../src/dragon.ts";
import { type Entity, type World, cellAt, createWorld, deserializeWorld, dragonState, fnv1a, hashWorld, serializeWorld } from "../src/world.ts";

/** World.CellHash in the C# port. */
function cellHash(w: World): string {
  return fnv1a(w.cells.map((c) =>
    `${c.t},${c.water},${c.frozen ? 1 : 0},${c.cover ?? ""},${c.fire},${c.fuel},${c.gas ?? ""},${c.gasAmt},${c.charge}${c.h ? `,h${c.h}` : ""}${c.life ? `,l${c.life}` : ""}${c.rust ? `,r${c.rust}` : ""}${c.heat ? `,t${c.heat}` : ""}${c.lit ? `,b${c.lit}` : ""}${c.slick ? `,k${c.slick}` : ""}${c.star ? `,s${c.star}` : ""}${c.cracked ? `,c${c.cracked}` : ""};`).join(""));
}

const run = (w: World, ticks: number) => { for (let i = 0; i < ticks; i++) step(w); };

/** Runs to the end of the flight. */
function flyOut(w: World, onTick: () => void = () => {}) {
  for (let i = 0; w.dragon && i < 20000; i++) {
    step(w);
    onTick();
  }
  assert.equal(w.dragon, null, "the dragon should have left");
}

/** A bare world with its starting state kept, then changes everywhere (a height on every cell of the changed list). */
function changedWorld(w: number, h: number, seed: number): World {
  const world = createWorld(w, h, seed);
  keepStart(world);
  for (let y = 0; y < h; y += 3) for (let x = 0; x < w; x += 3) cellAt(world, x, y)!.h = 1;
  return world;
}

/** The golden's map: a grass patch, a stone wall with a gap, a walled pond and four raiders: in the open, under water, between two stones, far off. */
function goldenWorld(): { w: World; raiders: Entity[]; start: string } {
  const w = createWorld(256, 128, 7);
  for (let y = 10; y <= 20; y++) for (let x = 20; x <= 30; x++) cellAt(w, x, y)!.t = "grass";
  for (let y = 0; y < 128; y++) if (y < 40 || y > 50) cellAt(w, 60, y)!.t = "stone";
  for (let y = 69; y <= 80; y++) for (let x = 89; x <= 100; x++) {
    if (y === 69 || y === 80 || x === 89 || x === 100) cellAt(w, x, y)!.t = "stone";
    else cellAt(w, x, y)!.water = 12;
  }
  cellAt(w, 70, 44)!.t = "stone";
  cellAt(w, 70, 46)!.t = "stone";
  const raiders = [spawn(w, "bone-hound", 10, 10)!, spawn(w, "bone-hound", 95, 75)!, spawn(w, "bone-hound", 70, 45)!, spawn(w, "bone-hound", 200, 60)!];
  keepStart(w);
  const start = cellHash(w);
  const lit = cellAt(w, 22, 12)!;
  lit.fire = 2;
  lit.fuel = 6;
  cellAt(w, 80, 30)!.h = 2;
  cellAt(w, 81, 30)!.t = "mud";
  cellAt(w, 5, 90)!.cover = "ash";
  return { w, raiders, start };
}

test("golden: a full event on a small map, the same cells, rng and flight as the C# core", () => {
  const { w, raiders, start } = goldenWorld();
  run(w, 10);
  summonDragon(w);
  const seen: string[] = [], fell = raiders.map(() => 0);
  flyOut(w, () => {
    if (w.tick % 60 === 0) seen.push(`${w.tick} ${cellHash(w)} ${w.rng} ${dragonState(w)}`);
    raiders.forEach((r, i) => {
      if ((r.status.downed ?? 0) > 0 && fell[i] === 0) fell[i] = w.tick;
    });
  });
  seen.push(`${w.tick} ${cellHash(w)} ${w.rng} ${dragonState(w)}`);
  assert.deepEqual(seen, GOLDEN);
  assert.deepEqual(fell, GOLDEN_FELL);
  assert.equal(cellHash(w), start, "the valley ends as it started");
  assert.equal(w.start!.total, 0);
});

const GOLDEN = [
  "60 7da842d 2298224275 1:4:75|1:190:0:0:0:0:2:0:0",
  "120 ac7e1b26 3150359324 1:0:120|1:190:0:0:0:0:2:0:0",
  "180 acbbcba 3654312600 1:4:120|1:190:0:0:0:0:2:0:0",
  "240 a30c8004 3654312600 1:0:0|2:0:0:0:0:1:2:12:2",
  "300 a30c8004 3654312600 1:4:0|3:382:0:0:0:1:2:0:6",
  "360 a30c8004 3654312600 1:0:0|3:382:0:0:0:1:2:0:6",
  "382 a30c8004 3654312600 1:6:0",
];
const GOLDEN_FELL = [192, 0, 0, 250];

test("a world the dragon never comes to keeps its cells and random numbers, starting state or not", () => {
  const build = () => {
    const w = createWorld(70, 70, 3);
    for (let y = 0; y < 70; y++) for (let x = 0; x < 70; x++) if ((x + y) % 3 === 0) cellAt(w, x, y)!.t = "grass";
    cellAt(w, 30, 30)!.fire = 2;
    cellAt(w, 30, 30)!.fuel = 8;
    return w;
  };
  const plain = build(), kept = build();
  keepStart(kept);
  run(plain, 200);
  run(kept, 200);
  assert.equal(cellHash(kept), cellHash(plain));
  assert.equal(kept.rng, plain.rng);
  assert.equal(kept.dragon, null);
  assert.equal(kept.start!.flights, 0);
});

test("every chunk is reset by the last pass, whatever the path and the size", () => {
  for (const [ww, hh] of [[64, 64], [128, 96], [300, 140], [140, 300], [65, 1], [200, 200]] as const) {
    for (let seed = 1; seed <= 12; seed++) {
      const w = changedWorld(ww, hh, seed);
      summonDragon(w);
      flyOut(w);
      assert.ok(w.cells.every((c) => c.h === 0), `${ww}x${hh} seed ${seed}`);
      assert.equal(w.start!.total, 0);
    }
  }
});

test("the seeds give different flights, and each flight of a world its own", () => {
  const paths = new Set<string>();
  for (let seed = 1; seed <= 32; seed++) {
    const w = createWorld(128, 128, seed);
    keepStart(w);
    summonDragon(w);
    paths.add(`${w.dragon!.alongX}${w.dragon!.fromFar}${w.dragon!.back}`);
  }
  assert.equal(paths.size, 8);
});

test("a wildfire doesn't outlive the flight: the dive snuffs it, so the valley ends as it started", () => {
  const w = createWorld(192, 192, 4);
  for (const c of w.cells) c.t = "grass";
  keepStart(w);
  for (let k = 0; k < 6; k++) {
    const c = cellAt(w, (k * 97 + 13) % 192, (k * 61 + 29) % 192)!;
    c.fire = 2;
    c.fuel = 8;
  }
  run(w, 30);
  summonDragon(w);
  run(w, RULES.sim.dragon!.warningTicks! + 1);
  assert.ok(w.cells.every((c) => c.fire === 0));
  flyOut(w);
  assert.ok(w.cells.every((c) => c.fire === 0 && c.t === "grass" && c.cover === null));
  assert.equal(w.start!.total, 0);
});

test("the warning lasts warningTicks, and at most chunksPerTick chunks reset a tick", () => {
  const w = changedWorld(200, 200, 2);
  summonDragon(w);
  const at = w.tick;
  while (w.dragon!.phase === 1) step(w);
  assert.equal(w.tick - at, RULES.sim.dragon!.warningTicks);
  let last = w.dragon!.done, lastPass = w.dragon!.pass;
  for (let i = 0; w.dragon && i < 5000; i++) {
    step(w);
    const d = w.dragon;
    if (!d) break;
    if (d.pass === lastPass) assert.ok(d.done - last <= RULES.sim.dragon!.chunksPerTick!);
    last = d.done;
    lastPass = d.pass;
  }
});

test("a run with the dragon comes out the same twice, and through a snapshot mid-flight", () => {
  const a = goldenWorld().w, b = goldenWorld().w;
  run(a, 10);
  run(b, 10);
  summonDragon(a);
  summonDragon(b);
  run(a, 260);
  run(b, 260);
  const c = deserializeWorld(serializeWorld(b));
  run(a, 200);
  run(c, 200);
  assert.equal(hashWorld(c), hashWorld(a));
  assert.equal(dragonState(c), dragonState(a));
});

test("the shelter rule: deep water, or a wall on the dragon's side, and nothing else", () => {
  const w = createWorld(128, 64, 2);
  keepStart(w);
  summonDragon(w);
  while (!breathBox(w)) step(w);
  const d = w.dragon!;
  const [x0, y0, x1, y1] = breathBox(w)!;
  // In the middle of the band, a wall placed toward where it flies from.
  const x = Math.floor((x0 + x1) / 2), y = Math.floor((y0 + y1) / 2);
  const dirBack = (d.back !== (d.pass % 2 === 1)) ? 1 : -1;
  const near = d.alongX ? ([x + dirBack * 2, y] as const) : ([x, y + dirBack * 2] as const);
  const far = d.alongX ? ([x - dirBack, y] as const) : ([x, y - dirBack] as const);
  assert.ok(caughtByBreath(w, x, y), "in the open");
  cellAt(w, ...far)!.t = "stone";
  assert.ok(caughtByBreath(w, x, y), "a wall on the far side is no use");
  cellAt(w, ...far)!.t = "dirt";
  cellAt(w, ...near)!.t = "stone";
  assert.ok(shelteredFromBreath(w, x, y) && !caughtByBreath(w, x, y), "behind stone");
  cellAt(w, ...near)!.t = "dirt";
  cellAt(w, ...near)!.t = "rock";
  assert.ok(caughtByBreath(w, x, y), "rock that doesn't block is no wall");
  cellAt(w, ...near)!.h = RULES.sim.height.blockAt;
  assert.ok(!caughtByBreath(w, x, y), "Wall of Stone's raised rock shelters");
  cellAt(w, ...near)!.t = "dirt";
  assert.ok(caughtByBreath(w, x, y), "a raised earth wall doesn't");
  cellAt(w, ...near)!.h = 0;
  cellAt(w, x, y)!.water = RULES.sim.dragon!.shelterWater! - 1;
  assert.ok(caughtByBreath(w, x, y), "shallow water");
  cellAt(w, x, y)!.water = RULES.sim.dragon!.shelterWater!;
  assert.ok(!caughtByBreath(w, x, y), "under water");
  cellAt(w, x, y)!.frozen = true;
  assert.ok(caughtByBreath(w, x, y), "ice is no shelter");
  assert.ok(!caughtByBreath(w, x1, y), "outside the breath");
});

test("the breath knocks down creatures and wizards, for sim.json's downed time, once a fall", () => {
  const w = createWorld(64, 64, 2);
  const wizard = spawn(w, "wizard", 30, 30)!;
  keepStart(w);
  summonDragon(w);
  while (!(wizard.status.downed ?? 0)) step(w);
  const ticks = Math.floor((RULES.sim.downed?.seconds ?? 3) * RULES.sim.ticksPerSecond);
  assert.equal(wizard.status.downed, ticks);
  assert.ok(w.entities.includes(wizard));
  const { notice } = applyIntent(w, "", { k: "walk", d: 1 });
  assert.equal(wizard.x, 30);
  assert.ok(notice);
});

test("a downed raider doesn't march, and the checksum sees it", () => {
  const w = createWorld(64, 64, 2);
  startDefense(w, [5, 5], [50, 50]);
  keepStart(w);
  summonDragon(w);
  while (w.dragon!.phase === 1) step(w);
  const raider = spawn(w, "bone-hound", 30, 30)!; // it would be at the flag by now
  for (let i = 0; i < 40 && !(raider.status.downed ?? 0); i++) step(w);
  const ticks = Math.floor((RULES.sim.downed?.seconds ?? 3) * RULES.sim.ticksPerSecond);
  assert.equal(raider.status.downed, ticks);
  assert.ok(defenseText(w).includes(`,d${ticks};`));
  const { x, y } = raider;
  run(w, ticks - 1);
  assert.deepEqual([raider.x, raider.y], [x, y]);
});

test("budget: the warning starts by itself once too many cells differ, counted by the audit alone", () => {
  const dragon = RULES.sim.dragon!;
  const budget = dragon.budget;
  dragon.budget = 40;
  try {
    const w = createWorld(128, 64, 2);
    keepStart(w);
    for (let x = 0; x < 41; x++) cellAt(w, x, 3)!.h = 1;
    assert.equal(w.start!.total, 0, "nothing is counted until the audit comes round");
    run(w, 1);
    assert.equal(w.start!.total, 41, "the audit reached the first chunk");
    assert.equal(w.dragon?.phase, 1, "the warning has started");
    assert.equal(w.start!.flights, 1);
  } finally {
    dragon.budget = budget;
  }
});

test("the count stays cheap: a tick audits one chunk, so a change in a far chunk shows when the audit reaches it", () => {
  const w = createWorld(128, 64, 2);
  keepStart(w);
  cellAt(w, 100, 3)!.h = 1;
  run(w, 1);
  assert.equal(w.start!.total, 0);
  run(w, 1);
  assert.equal(w.start!.total, 1);
});

test("budget 0: never by itself", () => {
  const dragon = RULES.sim.dragon!;
  const budget = dragon.budget;
  dragon.budget = 0;
  try {
    const w = changedWorld(64, 64, 2);
    run(w, 3);
    assert.equal(w.dragon, null);
  } finally {
    dragon.budget = budget;
  }
});

test("the changed count takes what lasts, not what fades", () => {
  const w = createWorld(64, 64, 2);
  keepStart(w);
  cellAt(w, 1, 1)!.gas = "steam";
  cellAt(w, 1, 1)!.gasAmt = 1;
  cellAt(w, 2, 1)!.charge = 2;
  cellAt(w, 3, 1)!.cover = "ash";
  cellAt(w, 4, 1)!.t = "rubble";
  cellAt(w, 5, 1)!.h = 1;
  cellAt(w, 6, 1)!.water = 4;
  cellAt(w, 7, 1)!.cover = "snow";
  cellAt(w, 8, 1)!.t = "mud";
  run(w, 1);
  assert.equal(w.start!.total, 3);
});

test("play areas come back as built: the ball, the machines, the gate links, the props", () => {
  const w = createWorld(64, 64, 2);
  startGolf(w, 5, 5, 20, 5);
  const barrel = spawn(w, "barrel", 40, 40);
  assert.ok(barrel, "a prop to bring back");
  cellAt(w, 30, 30)!.t = "gate";
  cellAt(w, 32, 30)!.t = "wheel";
  w.gateLinks.push({ gate: 30 * 64 + 30, machine: 30 * 64 + 32, open: false });
  keepStart(w);
  w.golf!.x = 9;
  w.golf!.strokes = 3;
  w.machines[30 * 64 + 32] = { level: 3, hold: 2, powered: true };
  w.entities.splice(w.entities.indexOf(barrel!), 1);
  cellAt(w, 30, 30)!.t = "gate_open";
  w.gateLinks[0]!.open = true;
  summonDragon(w);
  flyOut(w);
  assert.equal(w.golf!.x, 5);
  assert.equal(w.golf!.strokes, 0);
  assert.equal(w.machines[30 * 64 + 32]?.powered ?? false, false);
  assert.equal(cellAt(w, 30, 30)!.t, "gate");
  assert.equal(w.gateLinks[0]!.open, false);
  assert.ok(w.entities.some((e) => e.id === barrel!.id && e.x === 40));
});

test("the grimoires stay: a player's cast state isn't the world's", () => {
  const w = createWorld(64, 64, 2);
  keepStart(w);
  w.casts.p1 = { player: "p1", first: null, second: null, meter: 0, total: 0, window: 0, slot: 0, dpadCasting: -1, holding: false, extra: 0, charge: 1, known: ["fire+water"] };
  summonDragon(w);
  flyOut(w);
  assert.deepEqual(w.casts.p1!.known, ["fire+water"]);
});

test("a terrain the rules no longer know comes back as dirt", () => {
  const w = createWorld(64, 64, 2);
  cellAt(w, 5, 5)!.t = "gone-by-mod";
  RULES.terrain["gone-by-mod"] = RULES.terrain.dirt!;
  keepStart(w);
  delete RULES.terrain["gone-by-mod"];
  cellAt(w, 5, 5)!.t = "dirt";
  summonDragon(w);
  flyOut(w);
  assert.equal(cellAt(w, 5, 5)!.t, "dirt");
});

test("with no starting state nothing comes; while one flies a second summons changes nothing", () => {
  const w = createWorld(64, 64, 2);
  summonDragon(w);
  assert.equal(w.dragon, null);
  keepStart(w);
  summonDragon(w);
  const until = w.dragon!.until;
  run(w, 3);
  summonDragon(w);
  assert.equal(w.dragon!.until, until);
  assert.equal(w.start!.flights, 1);
});

test("only the host summons: the intent isn't taken from another page", () => {
  assert.equal(sanitizeIntent({ k: "dragon" }), null);
  const s = SCENARIOS[0]!;
  const w = loadScenario(s, 1);
  applyIntent(w, "host", { k: "dragon" });
  assert.equal(w.dragon?.phase, 1);
});

test("validateRules names a shelter terrain the rules lack", () => {
  const dragon = RULES.sim.dragon!;
  const list = dragon.wallTerrains;
  dragon.wallTerrains = ["stone", "cheese"];
  try {
    assert.ok(validateRules(RULES).includes("sim dragon: unknown terrain cheese"));
  } finally {
    dragon.wallTerrains = list;
  }
});
