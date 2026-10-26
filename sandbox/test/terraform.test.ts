// Earth shaping (terraform spells). The golden hash is shared with the C# port's
// GoldenEarthShapingMatchesTs (unity/Assets/Prototype/Chemistry/Tests/TerraformTests.cs).
import { test } from "node:test";
import assert from "node:assert/strict";
import { RULES, composeRules, installRules, validateRules, type ModPatch } from "../src/rules.ts";
import { step } from "../src/sim.ts";
import { castSpell, spellById, spellCells } from "../src/actions.ts";
import { spawn } from "../src/entities.ts";
import { reshape } from "../src/terraform.ts";
import { type World, cellAt, createWorld, hashWorld, isBlocking, isRaisedWall, sanitizeWorld } from "../src/world.ts";
import highWalls from "../../rules/mods/high-walls.json" with { type: "json" };

const run = (w: World, ticks: number) => { for (let i = 0; i < ticks; i++) step(w); };
const totalWater = (w: World) => w.cells.reduce((sum, c) => sum + c.water, 0);

function room(w: number, h: number, seed = 7): World {
  const world = createWorld(w, h, seed);
  for (let x = 0; x < w; x++) for (let y = 0; y < h; y++) {
    if (x === 0 || y === 0 || x === w - 1 || y === h - 1) cellAt(world, x, y)!.t = "bedrock";
  }
  return world;
}

/** A pond of pits holding a full step of water. */
function pond(w: World, x0: number, x1: number, y0: number, y1: number) {
  for (let x = x0; x <= x1; x++) for (let y = y0; y <= y1; y++) Object.assign(cellAt(w, x, y)!, { t: "pit", water: 4 });
}

/** World.CellHash in the C# port: the golden tests compare this string's FNV-1a. */
function cellHash(w: World): string {
  const s = w.cells.map((c) =>
    `${c.t},${c.water},${c.frozen ? 1 : 0},${c.cover ?? ""},${c.fire},${c.fuel},${c.gas ?? ""},${c.gasAmt},${c.charge}${c.h ? `,h${c.h}` : ""};`).join("");
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 0x01000193);
  return (h >>> 0).toString(16);
}

/** Swaps in modded rules for one test. */
function withRules(patch: ModPatch, body: () => void) {
  const before = composeRules([]);
  installRules(composeRules([patch]));
  try { body(); } finally { installRules(before); }
}

test("the terraform spells validate", () => {
  assert.deepEqual(validateRules(RULES), []);
  assert.deepEqual(validateRules(composeRules([highWalls.patch as ModPatch])), []);
});

test("an Earthen Trench cut from a pond's bank digs from the caster's feet and the water follows", () => {
  const w = room(20, 9);
  pond(w, 2, 5, 2, 6);
  // The wizard stands on the dry bank at (6, 4); aimSpell puts a self line one square out.
  castSpell(w, "earthen-trench", 7, 4, 1);
  assert.deepEqual([5, 6, 7, 13, 14].map((x) => cellAt(w, x, 4)!.h), [0, -2, -2, -2, 0], "8 squares from the caster's own");
  run(w, 20);
  // Settled: on the flat water needs a 2-unit difference to move, so the channel slopes.
  assert.deepEqual([6, 7, 8, 9, 10, 11, 12, 13].map((x) => cellAt(w, x, 4)!.water), [5, 4, 3, 2, 1, 1, 0, 0]);
  assert.equal(cellAt(w, 8, 3)!.water, 0, "the banks stay dry");
  assert.equal(totalWater(w), 80);
});

test("other line spells still start one square out from the caster", () => {
  const w = room(20, 9);
  assert.deepEqual(spellCells(w, spellById("ray-of-frost")!, 7, 4, 1)[0], [7, 4]);
  assert.deepEqual(spellCells(w, spellById("gust-of-wind")!, 7, 4, 1)[0], [7, 4]);
  assert.deepEqual(spellCells(w, spellById("earthen-trench")!, 7, 4, 1).slice(0, 2), [[6, 4], [7, 4]]);
});

test("lightning runs along a flooded trench", () => {
  const w = room(20, 9);
  pond(w, 2, 5, 2, 6);
  castSpell(w, "earthen-trench", 7, 4, 1);
  run(w, 20);
  castSpell(w, "shocking-grasp", 2, 2, 1);
  assert.ok(cellAt(w, 11, 4)!.charge > 0, "the far wet end of the channel is charged");
  assert.equal(cellAt(w, 12, 4)!.charge, 0, "the dry end isn't");
});

test("a Wall of Stone dams the flow: the far side stays dry", () => {
  for (const wall of [false, true]) {
    const w = room(16, 8);
    if (wall) castSpell(w, "wall-of-stone", 6, 1, 2);
    for (let y = 1; y <= 6; y++) for (let x = 1; x <= 3; x++) cellAt(w, x, y)!.water = 16;
    run(w, 300);
    let behind = 0;
    for (let y = 1; y <= 6; y++) for (let x = 7; x <= 14; x++) behind += cellAt(w, x, y)!.water;
    assert.equal(totalWater(w), 288);
    if (!wall) assert.equal(behind, 54, "without the wall the water spreads past x=6");
    else {
      assert.equal(behind, 0);
      assert.equal(cellAt(w, 6, 3)!.t, "rock");
      assert.ok(isBlocking(cellAt(w, 6, 3)!), "three steps up is a wall");
    }
  }
});

test("a Wall of Stone across a dug trench walls every square, trench included", () => {
  const w = room(12, 12);
  castSpell(w, "earthen-trench", 2, 5, 1);
  assert.equal(cellAt(w, 5, 5)!.h, -2);
  castSpell(w, "wall-of-stone", 5, 2, 2);
  for (let y = 2; y <= 7; y++) {
    assert.equal(cellAt(w, 5, y)!.h, RULES.sim.height.blockAt, `(5, ${y}) is raised to wall height`);
    assert.ok(isRaisedWall(cellAt(w, 5, y)!), `(5, ${y}) is a wall`);
  }
  cellAt(w, 4, 4)!.h = 4;
  castSpell(w, "wall-of-stone", 4, 2, 2);
  assert.equal(cellAt(w, 4, 4)!.h, 4, "height never lowers a square that is already higher");
});

test("Move Earth heaves up a hill, highest in the middle", () => {
  const w = createWorld(17, 17, 3);
  castSpell(w, "move-earth", 8, 8, 1);
  assert.deepEqual(Array.from({ length: 17 }, (_, x) => cellAt(w, x, 8)!.h), [0, 0, 0, 0, 0, 1, 1, 2, 2, 2, 1, 1, 0, 0, 0, 0, 0]);
  assert.equal(cellAt(w, 5, 5)!.h, 1, "the corners of the burst are edge too");
  assert.ok(!isBlocking(cellAt(w, 8, 8)!), "a hill you can walk up");
});

test("raising and digging conserve water, and so does the flow after", () => {
  const w = room(16, 16);
  pond(w, 4, 10, 4, 10);
  cellAt(w, 6, 6)!.frozen = true;
  castSpell(w, "move-earth", 7, 7, 1);
  castSpell(w, "wall-of-stone", 3, 7, 1);
  castSpell(w, "mold-earth", 12, 12, 1);
  castSpell(w, "mold-earth-heap", 5, 5, 1);
  assert.equal(totalWater(w), 196);
  run(w, 150);
  assert.equal(totalWater(w), 196);
});

test("digging strips grass and covers; raising stays within the height range", () => {
  const w = room(8, 8);
  Object.assign(cellAt(w, 3, 3)!, { t: "grass", cover: "oil" });
  castSpell(w, "mold-earth", 3, 3, 1);
  assert.deepEqual([cellAt(w, 3, 3)!.t, cellAt(w, 3, 3)!.cover, cellAt(w, 3, 3)!.h], ["dirt", null, -1]);
  for (let i = 0; i < 6; i++) reshape(w, 5, 5, -1);
  assert.equal(cellAt(w, 5, 5)!.h, RULES.sim.height.min);
  for (let i = 0; i < 9; i++) reshape(w, 5, 5, 2);
  assert.equal(cellAt(w, 5, 5)!.h, RULES.sim.height.max);
  assert.equal(reshape(w, 0, 0, 1), false, "bedrock can't be shaped");
  cellAt(w, 2, 2)!.water = 6;
  reshape(w, 2, 2, 1);
  assert.deepEqual([[2, 2], [2, 1], [3, 2], [2, 3], [1, 2]].map(([x, y]) => cellAt(w, x!, y!)!.water), [0, 2, 2, 1, 1], "spilled round its neighbours, N E S W");
  const gob = spawn(w, "goblin", 2, 5)!;
  assert.equal(reshape(w, gob.x, gob.y, 3), false, "no wall rises under a creature");
});

test("a mod changes the height range and the spells' numbers", () => {
  const w = room(12, 12);
  withRules(highWalls.patch as ModPatch, () => {
    castSpell(w, "wall-of-stone", 2, 1, 2);
    assert.equal(cellAt(w, 2, 3)!.h, 5);
    assert.ok(isBlocking(cellAt(w, 2, 3)!));
    castSpell(w, "move-earth", 7, 6, 1);
    assert.deepEqual([4, 5, 6, 7].map((x) => cellAt(w, x, 6)!.h), [1, 2, 3, 4]);
    assert.ok(!isBlocking(cellAt(w, 7, 6)!), "four steps is no wall when walls start at five");
  });
  sanitizeWorld(w);
  assert.equal(cellAt(w, 2, 3)!.h, RULES.sim.height.max, "switching the mod off clamps heights back into range");
});

test("bad terraform spells are refused with clear messages", () => {
  const errors = validateRules(composeRules([{
    terrain: { mud: { name: "Mud", tags: ["ground"], dugTo: "clay" } },
    spells: [
      { id: "a", name: "A", level: 1, shape: "point", effect: "terraform", blurb: "" },
      { id: "b", name: "B", level: 1, shape: "line", effect: "terraform", raise: 2, dig: 1, blurb: "" },
      { id: "c", name: "C", level: 1, shape: "square", effect: "terraform", dig: 1.5, terrain: "lava", taper: "yes", blurb: "" },
      { id: "d", name: "D", level: 1, shape: "burst", effect: "terraform", raise: 0, terrain: "bedrock", blurb: "" },
      { id: "f", name: "F", level: 1, shape: "line", effect: "terraform", height: 5, blurb: "" },
      { id: "g", name: "G", level: 1, shape: "line", effect: "terraform", height: -1.5, dig: 1, blurb: "" },
      { id: "e", name: "E", level: 1, shape: "point", effect: "quake", blurb: "" },
    ] as unknown as ModPatch["spells"],
  }]));
  assert.deepEqual(errors, [
    "terrain mud: dugTo clay unknown",
    "spell a: terraform needs exactly one of raise, dig or height",
    "spell b: terraform needs exactly one of raise, dig or height",
    "spell c: dig must be a whole number of steps from 1",
    "spell c: terrain lava unknown",
    "spell c: taper must be true or false",
    "spell d: raise must be a whole number of steps from 1",
    "spell d: terrain bedrock is blocking (walls come from raise)",
    "spell f: height must be a whole number from -3 to 4 (sim.height)",
    "spell g: terraform needs exactly one of raise, dig or height",
    "spell g: height must be a whole number from -3 to 4 (sim.height)",
    "spell e: unknown effect quake",
  ]);
});

/** The same casts as the C# GoldenEarthShapingMatchesTs. */
function earthShaping(seed: number): World {
  const w = room(18, 10, seed);
  pond(w, 2, 4, 2, 6);
  for (let x = 8; x <= 15; x++) for (let y = 6; y <= 8; y++) cellAt(w, x, y)!.t = "grass";
  cellAt(w, 9, 7)!.cover = "oil";
  cellAt(w, 10, 7)!.cover = "oil";
  castSpell(w, "earthen-trench", 6, 4, 1); // from the dry bank at (5, 4)
  run(w, 20);
  castSpell(w, "wall-of-stone", 12, 1, 2); // across the trench's end
  castSpell(w, "move-earth", 14, 7, 1);
  castSpell(w, "create-water", 14, 7, 1);
  castSpell(w, "mold-earth", 9, 7, 1);
  castSpell(w, "fire-bolt", 11, 8, 1);
  castSpell(w, "shocking-grasp", 6, 4, 1);
  castSpell(w, "mold-earth-heap", 3, 3, 1);
  run(w, 120);
  return w;
}

test("earth shaping is deterministic and matches the C# golden", () => {
  const a = earthShaping(11), b = earthShaping(11);
  assert.equal(hashWorld(a), hashWorld(b));
  assert.equal(cellHash(a), "93d9117b");
  assert.equal(a.rng, 3719496534);
  assert.equal(cellAt(a, 5, 4)!.h, -2, "the trench starts at the caster's feet");
  assert.ok(isRaisedWall(cellAt(a, 12, 4)!), "the wall closes the trench");
  assert.deepEqual(a.log.map((l) => `${l.tick}|${l.kind}|${l.count}|${l.text}`), [
    "0|info|1|You cast Earthen Trench",
    "20|info|1|You cast Wall of Stone",
    "20|info|1|You cast Move Earth",
    "20|info|1|You cast Create Water",
    "20|info|1|You cast Shape Earth",
    "20|info|1|You cast Fire Bolt",
    "20|info|1|Fire takes hold of the grass",
    "20|info|1|You cast Shocking Grasp",
    "20|notable|1|Lightning arcs through everything conductive that touches",
    "20|info|1|You cast Shape Earth (heap)",
  ]);
});
