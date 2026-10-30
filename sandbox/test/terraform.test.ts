// Earth shaping (terraform spells). The golden hash is shared with the C# port's
// GoldenEarthShapingMatchesTs (unity/Assets/Prototype/Chemistry/Tests/TerraformTests.cs).
import { test } from "node:test";
import assert from "node:assert/strict";
import { withoutSoaking } from "./soak.ts";
import { RULES, composeRules, installRules, validateRules, type ModPatch } from "../src/rules.ts";
import { step } from "../src/sim.ts";
import { castSpell, spellById, spellCells } from "../src/actions.ts";
import { applyElement } from "../src/react.ts";
import { spawn, walk } from "../src/entities.ts";
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

test("a Wall of Stone dams the flow: the far side stays dry", () => withoutSoaking(() => {
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
}));

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

test("raising and digging conserve water, and so does the flow after", () => withoutSoaking(() => {
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
}));

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
    assert.equal(cellAt(w, 2, 3)!.h, 10);
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
      { id: "c", name: "C", level: 1, shape: "square", effect: "terraform", dig: 1.5, terrain: "magma", taper: "yes", blurb: "" },
      { id: "d", name: "D", level: 1, shape: "burst", effect: "terraform", raise: 0, terrain: "bedrock", blurb: "" },
      { id: "f", name: "F", level: 1, shape: "line", effect: "terraform", height: 9, blurb: "" },
      { id: "g", name: "G", level: 1, shape: "line", effect: "terraform", height: -1.5, dig: 1, blurb: "" },
      { id: "e", name: "E", level: 1, shape: "point", effect: "quake", blurb: "" },
    ] as unknown as ModPatch["spells"],
  }]));
  assert.deepEqual(errors, [
    "terrain mud: dugTo clay unknown",
    "spell a: terraform needs exactly one of raise, dig or height",
    "spell b: terraform needs exactly one of raise, dig or height",
    "spell c: dig must be a whole number of steps from 1",
    "spell c: terrain magma unknown",
    "spell c: taper must be true or false",
    "spell d: raise must be a whole number of steps from 1",
    "spell d: terrain bedrock is blocking (walls come from raise)",
    "spell f: height must be a whole number from -6 to 8 (sim.height)",
    "spell g: terraform needs exactly one of raise, dig or height",
    "spell g: height must be a whole number from -6 to 8 (sim.height)",
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
  assert.equal(cellHash(a), "7175c6c6");
  assert.equal(a.rng, 3719496534);
  assert.equal(cellAt(a, 5, 4)!.h, -2, "the trench starts at the caster's feet");
  assert.ok(isRaisedWall(cellAt(a, 12, 4)!), "the wall closes the trench");
  assert.deepEqual(a.log.map((l) => `${l.tick}|${l.kind}|${l.count}|${l.text}`), [
    "0|info|1|You cast Earthen Trench",
    "20|info|1|You cast Wall of Stone",
    "20|info|1|You cast Move Earth",
    "20|info|1|You cast Create Water",
    "20|info|1|You cast Lower Earth",
    "20|info|1|You cast Fire Bolt",
    "20|info|1|Fire takes hold of the grass",
    "20|info|1|You cast Shocking Grasp",
    "20|notable|1|Lightning arcs through everything conductive that touches",
    "20|info|1|You cast Raise Earth",
  ]);
});

test("charged casts dig deeper, build higher and reach further, and match the C# golden", () => {
  const w = room(24, 12, 3);
  pond(w, 2, 4, 2, 9);
  castSpell(w, "mold-earth-heap", 8, 3, 1, undefined, 3);
  assert.equal(cellAt(w, 8, 3)!.h, 3, "Raise Earth held to the full: three steps, a wall");
  castSpell(w, "mold-earth", 12, 3, 1, undefined, 2);
  assert.equal(cellAt(w, 12, 3)!.h, -2, "Lower Earth held halfway: two steps down");
  castSpell(w, "wall-of-stone", 16, 2, 2, undefined, 2);
  assert.equal(cellAt(w, 16, 3)!.h, 5, "Wall of Stone held: two steps taller");
  castSpell(w, "ray-of-frost", 5, 6, 3, undefined, 3);
  assert.ok(cellAt(w, 2, 6)!.frozen, "a held Ray of Frost reaches 10 squares, across the pond");
  for (let x = 16; x <= 22; x++) for (let y = 6; y <= 10; y++) cellAt(w, x, y)!.t = "grass";
  assert.equal(spellCells(w, spellById("fire-bolt")!, 18, 8, 1, 3).length, 13, "a held Fire Bolt spreads two squares round its spot");
  castSpell(w, "fire-bolt", 18, 8, 1, undefined, 3);
  for (let i = 0; i < 60; i++) step(w);
  // The hash changed when a charged spot, square or burst began to grow (chargedGrowth): the bolt lights 13 squares, not 1.
  assert.equal(`${cellHash(w)} ${w.rng}`, "1e7f4b73 3022804953");
});

test("charged spots, squares and bursts grow; earth shaping keeps its patch (as in C#)", () => {
  const w = room(24, 24, 3);
  const cells = (id: string, charge: number) => spellCells(w, spellById(id)!, 12, 12, 1, charge).length;
  assert.equal(cells("fireball", 3), 9 * 9, "radius 2 + 2");
  assert.equal(cells("grease", 2), 3 * 3, "side 2 + 1");
  assert.equal(cells("shocking-grasp", 2), 5, "a spot and its four sides");
  assert.equal(cells("mold-earth", 3), 2 * 2, "Lower Earth digs deeper instead");
  assert.equal(cells("move-earth", 3), 7 * 7, "Move Earth heaves higher instead");
  assert.equal(cells("misty-step", 3), 1, "a target stays one square");
});

/** A row of raised rock (hardness 3) and a row of metal plate (hardness 4) across a room of dirt. */
function hardRoom(): World {
  const w = room(20, 12, 9);
  for (let x = 2; x <= 17; x++) {
    cellAt(w, x, 3)!.t = "rock";
    cellAt(w, x, 6)!.t = "metal";
  }
  return w;
}

test("a dig weaker than the ground leaves it: Shape Earth digs dirt, a Meteor digs rock, nothing digs metal; matches the C# golden", () => {
  const w = hardRoom();
  castSpell(w, "mold-earth", 4, 3, 1);
  assert.equal(cellAt(w, 4, 3)!.h, 0, "Lower Earth can't dig rock");
  assert.ok(w.log.some((l) => l.text === "The raised rock is too hard to dig"));
  castSpell(w, "earthen-trench", 3, 9, 1);
  assert.equal(cellAt(w, 5, 9)!.h, -2, "Earthen Trench digs dirt");
  castSpell(w, "fireball", 10, 3, 0);
  assert.equal(cellAt(w, 10, 3)!.h, 0, "Fireball's crater (strength 2) can't dig rock");
  castSpell(w, "meteor", 14, 4, 0);
  assert.ok(cellAt(w, 14, 3)!.h < 0, "Meteor's crater (strength 3) digs rock");
  assert.equal(cellAt(w, 14, 3)!.t, "rubble");
  assert.equal(cellAt(w, 14, 6)!.h, 0, "metal never digs");
  run(w, 30);
  assert.equal(`${cellHash(w)} ${w.rng}`, "58631e96 9");
});

test("hardness and strength are checked", () => {
  const r = composeRules([]);
  const errors = validateRules({
    ...r,
    terrain: { ...r.terrain, granite: { name: "Granite", tags: ["ground"], hardness: 0 } },
    spells: [...r.spells, { ...r.spells.find((s) => s.id === "mold-earth")!, id: "chisel", strength: 1.5 }],
  });
  assert.ok(errors.includes("terrain granite: hardness must be a whole number from 1"));
  assert.ok(errors.includes("spell chisel: strength must be a whole number from 1"));
});

test("Burning Hands is a cone from the caster, longer when charged, and matches the C# golden", () => {
  const w = room(20, 14, 4);
  for (let x = 1; x < 19; x++) for (let y = 1; y < 13; y++) cellAt(w, x, y)!.t = "grass";
  const s = spellById("burning-hands")!;
  // Aimed east from the square in front of the caster at (3, 6): rows of 1, 3 and 5 squares.
  assert.deepEqual(spellCells(w, s, 4, 6, 1), [[4, 6], [5, 5], [5, 6], [5, 7], [6, 4], [6, 5], [6, 6], [6, 7], [6, 8]]);
  assert.equal(spellCells(w, s, 4, 6, 1, 3).length, 1 + 3 + 5 + 7 + 9 + 11, "charged to 3: six rows");
  castSpell(w, "burning-hands", 4, 6, 1, undefined, 2);
  for (let i = 0; i < 40; i++) step(w);
  assert.equal(`${cellHash(w)} ${w.rng}`, "d1f5f85a 1912747678");
});

test("a river refills, so a trench cut from it fills and the river stays full; matches the C# golden", () => {
  const w = room(24, 12, 6);
  for (let x = 1; x <= 10; x++) Object.assign(cellAt(w, x, 6)!, { t: "river", water: 4 });
  castSpell(w, "earthen-trench", 12, 6, 1);
  run(w, 60);
  assert.equal(cellAt(w, 3, 6)!.water, 4, "the river is full again");
  assert.ok([12, 14, 16].every((x) => cellAt(w, x, 6)!.water > 0), "water ran down the trench");
  assert.ok(totalWater(w) > 40, "more water than the river held: the spring keeps giving");
  assert.equal(`${cellHash(w)} ${w.rng}`, "64047114 6");
});

test("Fireball blows a small crater before it burns, and matches the C# golden", () => {
  const w = room(20, 12, 8);
  for (let x = 1; x < 19; x++) for (let y = 1; y < 11; y++) cellAt(w, x, y)!.t = "grass";
  castSpell(w, "fireball", 10, 6, 1);
  assert.equal(cellAt(w, 10, 6)!.h, -2, "two steps down in the middle");
  assert.equal(cellAt(w, 11, 7)!.h, -1, "one step round it");
  assert.equal(cellAt(w, 12, 6)!.h, 0, "the blast's edge isn't dug");
  assert.ok(cellAt(w, 12, 6)!.fire > 0, "but it burns");
  run(w, 40);
  assert.equal(`${cellHash(w)} ${w.rng}`, "b1b430e0 3300970619");
});

test("a slanted trench runs off the grid, side to side, and the river's water follows it; matches the C# golden", () => {
  const w = room(24, 14, 9);
  for (let x = 2; x <= 4; x++) for (let y = 3; y <= 10; y++) Object.assign(cellAt(w, x, y)!, { t: "river", water: 4 });
  const s = spellById("earthen-trench")!;
  const cells = spellCells(w, s, 6, 6, 1, 1, [6, 2]);
  assert.deepEqual(cells[0], [5, 6], "it starts under the caster on the river's bank");
  assert.ok(cells.every(([x, y], i) => i === 0 || Math.abs(x - cells[i - 1]![0]) + Math.abs(y - cells[i - 1]![1]) === 1), "each square shares a side with the last");
  assert.ok(cells.at(-1)![1] > 6, "it slants south as it goes east");
  assert.deepEqual(spellCells(w, s, 6, 6, 1, 1, [12, 1]), spellCells(w, s, 6, 6, 1), "within 5 degrees of east it's the straight trench");
  castSpell(w, "earthen-trench", 6, 6, 1, undefined, 1, [6, 2]);
  run(w, 60);
  assert.ok(cells.slice(0, 8).every(([x, y]) => cellAt(w, x, y)!.water > 0), "water ran along it, round its corners, as far as along a straight one");
  assert.equal(`${cellHash(w)} ${w.rng}`, "99221231 9");
});

test("a slanted cone fans along the aim", () => {
  const w = room(20, 14, 4);
  const s = spellById("burning-hands")!;
  assert.deepEqual(spellCells(w, s, 4, 6, 1, 1, [3, 2]), [[4, 6], [5, 6], [6, 6], [7, 6], [5, 7], [6, 7], [5, 8], [6, 8], [5, 9]]);
});

test("earth shaping topples trees into deadfall that burns, and matches the C# golden", () => {
  const w = room(20, 12, 10);
  for (let x = 1; x < 19; x++) for (let y = 1; y < 11; y++) cellAt(w, x, y)!.t = "grass";
  for (const [x, y] of [[5, 5], [6, 5], [7, 6], [13, 3], [15, 8]] as const) cellAt(w, x, y)!.t = "tree";
  castSpell(w, "move-earth", 6, 5, 0);
  assert.equal(cellAt(w, 6, 5)!.t, "deadfall", "the tree in the heave toppled");
  assert.ok(cellAt(w, 6, 5)!.h > 0, "and the ground rose under it");
  assert.equal(w.log.filter((e) => e.text.includes("tree topples")).length, 1, "one line for the cast, however many trees");
  castSpell(w, "wall-of-stone", 13, 2, 2);
  assert.equal(cellAt(w, 13, 3)!.t, "rock", "a wall raised through a tree goes up as rock");
  castSpell(w, "fireball", 15, 8, 0);
  assert.equal(cellAt(w, 15, 8)!.t, "deadfall", "a crater fells the tree it lands on");
  castSpell(w, "fire-bolt", 5, 5, 0);
  run(w, 40);
  assert.equal(`${cellHash(w)} ${w.rng}`, "1213368f 1054537720");
});

// Mirrors ChemistryTests.AcidSplashEatsBramblesThornsAndOilAndBurnsAGoblin (docs/MATERIALS.md dissolve): a row of
// brambles, thorns, grease and oil round a goblin; Acid Splash on the goblin eats them all and hurts it.
test("Acid Splash eats brambles, thorns and oil and burns a goblin, matching the C# golden", () => {
  const w = createWorld(12, 9, 3);
  for (const c of w.cells) c.t = "dirt";
  for (let x = 5; x <= 7; x++) cellAt(w, x, 3)!.t = "brush";
  cellAt(w, 5, 4)!.cover = "grease";
  cellAt(w, 7, 4)!.cover = "thorns";
  cellAt(w, 6, 5)!.cover = "oil";
  cellAt(w, 4, 4)!.cover = "oil"; // outside the splash
  const gob = spawn(w, "goblin", 6, 4)!;
  castSpell(w, "acid-splash", 6, 4, 0);
  for (let x = 5; x <= 7; x++) assert.equal(cellAt(w, x, 3)!.t, "dirt", `brambles at ${x} eaten`);
  assert.deepEqual([cellAt(w, 5, 4)!.cover, cellAt(w, 7, 4)!.cover, cellAt(w, 6, 5)!.cover, cellAt(w, 4, 4)!.cover], [null, null, null, "oil"]);
  assert.ok(w.log.some((l) => l.text === "The acid eats the brambles away"));
  assert.equal(`${cellHash(w)} ${w.rng} ${gob.hp}`, "d9c38f7d 1831565816 5"); // the same in the C# core
  const slime = { covers: { slime: { name: "Slime", tags: [], dissolve: { by: "vinegar", leaves: "goo" } } } } as unknown as ModPatch;
  assert.deepEqual(validateRules(composeRules([slime])), ["cover slime: dissolves by unknown element vinegar", "cover slime: dissolvesTo goo unknown"]);
});

// Mirrors ChemistryTests.WaterSoaksDirtToMudWhichDriesBack (docs/MATERIALS.md soak.leaves, soak.dries): shallow water
// over dirt, grass and rock; the dirt turns to mud as it soaks in (grass and rock don't), fire on the mud is
// smothered (fire-mud) even where oil lies, a goblin on it is held, and once it's dry the mud is dirt again.
test("water soaks dirt to mud, which slows, smothers fire and dries back to dirt; matches the C# golden", () => {
  const w = createWorld(10, 5, 1);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 6, 2)!.t = "grass";
  cellAt(w, 7, 2)!.t = "rock";
  for (let x = 3; x <= 7; x++) cellAt(w, x, 2)!.water = 2;
  cellAt(w, 4, 2)!.cover = "oil";
  run(w, 200);
  assert.deepEqual([3, 4, 5, 6, 7].map((x) => cellAt(w, x, 2)!.t), ["mud", "mud", "mud", "grass", "rock"]);
  applyElement(w, 4, 2, "fire", { power: 3, direct: true });
  assert.equal(cellAt(w, 4, 2)!.fire, 0, "the mud smothers it, oil or no");
  const gob = spawn(w, "goblin", 2, 2)!;
  walk(w, gob, 1);
  assert.equal(gob.status.snagged, RULES.sim.ticksPerSecond, "a step in mud (speed 0.5) takes twice as long");
  w.entities.length = 0;
  const wet = cellHash(w);
  run(w, 800);
  assert.deepEqual([3, 4, 5].map((x) => cellAt(w, x, 2)!.t), ["dirt", "dirt", "dirt"], "dried back to dirt");
  assert.equal(`${wet} ${cellHash(w)} ${w.rng}`, "efb1f804 2143a548 1"); // the same in the C# core
});

// Mirrors ChemistryTests.CreaturesBurnFreezeAndSoakByTheirMaterial (docs/MATERIALS.md "Creatures as flesh"): a goblin
// (flesh), a bone hound (bone) and an ash crab (chitin) in fire, then water, then cold. Flesh burns, soaks and
// freezes; bone doesn't burn; chitin doesn't freeze (it stays wet).
test("creatures burn, freeze and soak by their material, matching the C# golden", () => {
  const w = createWorld(16, 7, 5);
  for (const c of w.cells) c.t = "dirt";
  const them = [spawn(w, "goblin", 3, 3)!, spawn(w, "bone-hound", 7, 3)!, spawn(w, "ash-crab", 11, 3)!];
  const states: string[] = [];
  for (const element of ["fire", "water", "cold"]) {
    for (const e of them) applyElement(w, e.x, e.y, element, { power: 1, direct: false });
    states.push(them.map((e) => `${e.status.burning ?? 0},${e.status.wet ?? 0},${e.status.frozen ?? 0}`).join(" "));
  }
  assert.deepEqual(states, ["9,0,0 0,0,0 9,0,0", "0,60,0 0,60,0 0,60,0", "0,0,30 0,0,30 0,60,0"]);
  assert.equal(`${cellHash(w)} ${w.rng}`, "7cbe626d 5"); // the same in the C# core
  const odd = { reactions: { addEntity: [{ id: "e-odd", element: "fire", if: { verb: "melt" }, do: [{ statusOf: "glow" }] }] } } as unknown as ModPatch;
  assert.deepEqual(validateRules(composeRules([odd])).sort(), ["rule e-odd: statusOf must be burn, freeze or soak", "rule e-odd: verb must be burn, freeze or soak"]);
});
