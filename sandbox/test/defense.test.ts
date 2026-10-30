// The defense field (defense.ts). The golden hashes are shared with the C# port's
// DefenseTests (unity/Assets/Prototype/Chemistry/Tests/DefenseTests.cs).
import { test } from "node:test";
import assert from "node:assert/strict";
import { RULES, composeRules, validateRules, type ModPatch } from "../src/rules.ts";
import { step } from "../src/sim.ts";
import { castSpell } from "../src/actions.ts";
import { reshape } from "../src/terraform.ts";
import { LOST, RUNNING, defenseText, startDefense } from "../src/defense.ts";
import { type Entity, type World, cellAt, createWorld, hashWorld } from "../src/world.ts";

const run = (w: World, ticks: number) => { for (let i = 0; i < ticks; i++) step(w); };
const fnv = (s: string) => {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 0x01000193);
  return (h >>> 0).toString(16);
};
const cellHash = (w: World) => fnv(w.cells.map((c) =>
  `${c.t},${c.water},${c.frozen ? 1 : 0},${c.cover ?? ""},${c.fire},${c.fuel},${c.gas ?? ""},${c.gasAmt},${c.charge}${c.h ? `,h${c.h}` : ""}${c.life ? `,l${c.life}` : ""};`).join(""));
const raiders = (w: World): Entity[] => w.entities.filter((e) => RULES.entities[e.kind]?.raider);

/** A field of dirt walled with bedrock (ChemistryTests.Room in C#). */
function room(w: number, h: number, seed = 7): World {
  const world = createWorld(w, h, seed);
  for (let x = 0; x < w; x++) for (let y = 0; y < h; y++) {
    if (x === 0 || y === 0 || x === w - 1 || y === h - 1) cellAt(world, x, y)!.t = "bedrock";
  }
  return world;
}

const GATE: [number, number] = [2, 7], FLAG: [number, number] = [36, 7];

/** The same script as the C# GoldenWaveMatchesTs: raiders walk, one is burnt, a wall reroutes them, a pool is shocked. */
function goldenWave(seed: number): World {
  const w = room(40, 15, seed);
  startDefense(w, GATE, FLAG);
  run(w, 40);
  const first = raiders(w)[0]!;
  castSpell(w, "fire-bolt", first.x, first.y, 1);
  run(w, 20);
  castSpell(w, "wall-of-stone", 20, 4, 2); // across the way, x 20, y 4..9
  run(w, 30);
  const lead = raiders(w)[0]!;
  castSpell(w, "create-water", lead.x, lead.y, 1);
  run(w, 2);
  castSpell(w, "shocking-grasp", lead.x, lead.y, 1); // where it has waded to
  run(w, 300);
  return w;
}

test("the defense rules check out", () => {
  assert.deepEqual(validateRules(RULES), []);
  const bad = composeRules([{ sim: { defense: { kinds: ["goblin"] } } } as unknown as ModPatch]);
  assert.deepEqual(validateRules(bad), ["sim defense: kind goblin is not a raider"]);
});

test("a wave starts after the warmup and its raiders come through the gate on schedule", () => {
  const w = room(40, 15);
  startDefense(w, GATE, FLAG);
  const p = RULES.sim.defense;
  run(w, p.warmup - 1);
  assert.equal(raiders(w).length, 0);
  run(w, 1);
  assert.deepEqual(raiders(w).map((r) => [r.kind, r.x, r.y]), [[p.kinds[0], 2, 7]]);
  run(w, p.spawnEvery);
  assert.equal(raiders(w).length, 2);
  assert.ok(raiders(w)[0]!.x > 2, "the first is on its way east");
});

test("raiders walk round a raised wall rather than through it", () => {
  const w = room(40, 15);
  for (let y = 4; y <= 9; y++) reshape(w, 20, y, 3);
  startDefense(w, GATE, FLAG);
  const seen = new Set<string>();
  for (let t = 0; t < 400; t++) {
    step(w);
    for (const r of raiders(w)) seen.add(`${r.x},${r.y}`);
  }
  for (let y = 4; y <= 9; y++) assert.ok(!seen.has(`20,${y}`), `nobody stood in the wall at (20, ${y})`);
  assert.ok(seen.has("20,3") || seen.has("20,10"), "they went round an end");
});

test("a trench dug ahead of them reroutes them", () => {
  const w = room(40, 15);
  startDefense(w, GATE, FLAG);
  run(w, 40);
  for (let y = 6; y <= 8; y++) reshape(w, 20, y, -2);
  const seen = new Set<string>();
  for (let t = 0; t < 300; t++) {
    step(w);
    for (const r of raiders(w)) seen.add(`${r.x},${r.y}`);
  }
  for (let y = 6; y <= 8; y++) assert.ok(!seen.has(`20,${y}`), `nobody went down into the trench at (20, ${y})`);
  assert.ok(seen.has("20,5") || seen.has("20,9"));
});

test("fire hurts an ash crab half as much as a giant spider; a soaked crab is shocked to death", () => {
  const loss = (kind: string) => {
    const w = room(20, 9, 3);
    w.entities.push({ id: w.nextId++, kind, x: 8, y: 4, hp: 40, status: {}, maxHp: 40, wait: 0 });
    castSpell(w, "fire-bolt", 8, 4, 1);
    return 40 - w.entities[0]!.hp;
  };
  const spider = loss("giant-spider"), crab = loss("ash-crab");
  assert.ok(spider > 0);
  assert.equal(crab, spider - Math.floor(spider / 2));

  const w = room(20, 9, 3);
  w.entities.push({ id: w.nextId++, kind: "ash-crab", x: 8, y: 4, hp: 9, status: {}, maxHp: 9, wait: 0 });
  castSpell(w, "create-water", 8, 4, 1);
  step(w);
  castSpell(w, "shocking-grasp", 9, 4, 1);
  step(w);
  assert.equal(raiders(w).length, 0, "wet doubles the shock and the crab is weak to lightning");
});

test("a raider reaching the flag costs a life; out of lives the defense is lost", () => {
  const w = room(12, 9);
  startDefense(w, [3, 4], [6, 4]);
  for (let t = 0; t < 60 && w.defense!.lives === RULES.sim.defense.lives; t++) step(w);
  assert.equal(w.defense!.lives, RULES.sim.defense.lives - 1);
  for (let t = 0; t < 2000 && w.defense!.phase === RUNNING; t++) step(w);
  assert.equal(w.defense!.phase, LOST);
  assert.equal(raiders(w).length, 0);
});

test("the defense is deterministic and matches the C# golden", () => {
  const a = goldenWave(11), b = goldenWave(11);
  assert.equal(hashWorld(a), hashWorld(b));
  assert.equal(`${fnv(defenseText(a))} ${cellHash(a)} ${a.rng}`, "19e78df2 e6f169fe 3663131637");
  // Wave 2 is under way: one raider is round the wall's south end, the rest are held in the mud the pool left.
  assert.equal(defenseText(a).split("|")[0], "1/2/9/0/346/8");
  assert.ok(raiders(a).some((r) => r.x === 21 && r.y === 10));
  assert.ok(raiders(a).some((r) => cellAt(a, r.x, r.y)!.t === "mud" && (r.status.snagged ?? 0) > 0));
});
