// Earth shaping (terraform spells). The golden hash is shared with the C# port's
// GoldenEarthShapingMatchesTs (unity/Assets/Prototype/Chemistry/Tests/TerraformTests.cs).
import { test } from "node:test";
import assert from "node:assert/strict";
import { withoutSoaking } from "./soak.ts";
import { RULES, composeRules, installRules, validateRules, type ModPatch } from "../src/rules.ts";
import { step } from "../src/sim.ts";
import { castSpell, spellById, spellCells } from "../src/actions.ts";
import { applyElement, ignite, shatter } from "../src/react.ts";
import { spawn, walk } from "../src/entities.ts";
import { dig, reshape } from "../src/terraform.ts";
import { whirlState } from "../src/combos.ts";
import { golfState, startGolf } from "../src/golf.ts";
import { type Cell, type World, addGas, cellAt, createWorld, deserializeWorld, fnv1a, hashWorld, isBlocking, isClimbable, isRaisedWall, isSlippery, sanitizeWorld, serializeWorld } from "../src/world.ts";
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

/** World.CellHash in the C# port (life, rust and heat written only when not 0): the golden tests compare this string's FNV-1a. */
function cellHash(w: World): string {
  const s = w.cells.map((c) =>
    `${c.t},${c.water},${c.frozen ? 1 : 0},${c.cover ?? ""},${c.fire},${c.fuel},${c.gas ?? ""},${c.gasAmt},${c.charge}${c.h ? `,h${c.h}` : ""}${c.life ? `,l${c.life}` : ""}${c.rust ? `,r${c.rust}` : ""}${c.heat ? `,t${c.heat}` : ""}${c.lit ? `,b${c.lit}` : ""}${c.slick ? `,k${c.slick}` : ""}${c.cracked ? `,c${c.cracked}` : ""};`).join("");
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
  assert.ok(w.log.some((l) => l.text === "The raised rock is too hard for Lower Earth"), "the refusal names the spell");
  castSpell(w, "earthen-trench", 3, 9, 1);
  assert.equal(cellAt(w, 5, 9)!.h, -2, "Earthen Trench digs dirt");
  castSpell(w, "fireball", 10, 3, 0);
  assert.equal(cellAt(w, 10, 3)!.h, 0, "Fireball's crater (strength 2) can't dig rock");
  castSpell(w, "meteor", 14, 4, 0);
  assert.ok(cellAt(w, 14, 3)!.h < 0, "Meteor's crater (strength 3) digs rock");
  assert.equal(cellAt(w, 14, 3)!.t, "rubble");
  assert.equal(cellAt(w, 14, 6)!.h, 0, "metal never digs");
  run(w, 30);
  assert.equal(`${cellHash(w)} ${w.rng}`, "48b09445 2346426195");
});

test("hardness and strength are checked", () => {
  const r = composeRules([]);
  const errors = validateRules({
    ...r,
    terrain: { ...r.terrain, granite: { name: "Granite", tags: ["ground"], hardness: -1 } },
    spells: [...r.spells, { ...r.spells.find((s) => s.id === "mold-earth")!, id: "chisel", strength: 1.5 }],
  });
  assert.ok(errors.includes("terrain granite: hardness must be a whole number from 0 (0 never digs)"));
  assert.ok(errors.includes("spell chisel: strength must be a whole number from 1"));
});

// Mirrors TerraformTests.StoneShapeDigsRockButNothingDigsMetalOrBedrock (BACKLOG 4).
test("Stone Shape digs rock, but nothing digs metal or bedrock", () => {
  const w = createWorld(12, 5, 1);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 2, 2)!.t = "sand";
  cellAt(w, 4, 2)!.t = "mud";
  cellAt(w, 6, 2)!.t = "rock";
  cellAt(w, 8, 2)!.t = "metal";
  cellAt(w, 10, 2)!.t = "bedrock";
  for (const x of [2, 4, 6, 8]) castSpell(w, "mold-earth", x, 2, 1);
  assert.deepEqual([2, 4, 6, 8].map((x) => cellAt(w, x, 2)!.h), [-1, -1, 0, 0], "sand and mud dig; rock and metal don't");
  assert.ok(w.log.some((l) => l.text === "The raised rock is too hard for Lower Earth"));
  for (const x of [6, 8, 10]) castSpell(w, "stone-shape", x, 2, 1);
  assert.equal(cellAt(w, 6, 2)!.h, -1, "Stone Shape digs rock");
  assert.equal(cellAt(w, 6, 2)!.t, "rubble");
  assert.equal(cellAt(w, 8, 2)!.h, 0, "but not metal");
  assert.ok(w.log.some((l) => l.text === "The metal plate can't be dug"));
  assert.equal(cellAt(w, 10, 2)!.t, "bedrock", "nor bedrock");
  assert.ok(w.log.some((l) => l.text === "The bedrock can't be dug"));
  run(w, 20);
  assert.equal(`${cellHash(w)} ${w.rng}`, "f57b067a 1"); // the same in the C# core
});

// Mirrors TerraformTests.APickDigsRockThroughAnEffect.
test("a pick digs rock, but not metal or bedrock", () => {
  const w = createWorld(10, 3, 1);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 2, 1)!.t = "rock";
  cellAt(w, 4, 1)!.t = "metal";
  cellAt(w, 6, 1)!.t = "bedrock";
  for (const x of [2, 4, 6, 8]) dig(w, x, 1, RULES.items.pick);
  assert.equal(cellAt(w, 2, 1)!.h, -1);
  assert.equal(cellAt(w, 2, 1)!.t, "rubble");
  assert.equal(cellAt(w, 4, 1)!.h, 0);
  assert.equal(cellAt(w, 8, 1)!.h, -1, "dirt digs too");
  assert.ok(w.log.some((l) => l.text === "The metal plate can't be dug"));
  assert.ok(w.log.some((l) => l.text === "The bedrock can't be dug"));
  dig(w, 3, 1, RULES.items.ball);
  assert.equal(cellAt(w, 3, 1)!.h, 0, "an item that doesn't dig does nothing");
  run(w, 20);
  assert.equal(`${cellHash(w)} ${w.rng}`, "1c4e549b 1"); // the same in the C# core
});

test("dig tools need a strength", () => {
  const r = composeRules([]);
  const errors = validateRules({ ...r, items: { ...r.items, spade: { name: "Spade", use: "dig", strength: 0 } } });
  assert.ok(errors.includes("item spade: use dig needs a whole strength from 1"));
});

test("every ground says how hard it is", () => {
  for (const [id, t] of Object.entries(RULES.terrain)) {
    if ((t.tags.includes("ground") || t.tags.includes("low")) && !t.tags.includes("machine")) assert.notEqual(t.hardness, undefined, id);
  }
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
  assert.equal(`${cellHash(w)} ${w.rng}`, "7b41057e 1796687091");
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
  assert.equal(`${cellHash(w)} ${w.rng}`, "aa411832 2208810063");
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
  assert.equal(`${wet} ${cellHash(w)} ${w.rng}`, "f44639dc 668f5350 1"); // the same in the C# core
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

// Mirrors ChemistryTests.TheWaterAndEarthCombosCastTheirOwnSpells (docs/SPELL_COMBOS.md): Flood fills two pits (and,
// here in the sandbox, shoves a crate along: the C# core leaves pushing things to the view); Mud churns a patch of grass to mud; Molten Boulder blasts a crater with lava in it; Dust Storm
// raises a cloud of dust that hides what's behind it.
test("Flood, Mud, Molten Boulder and Dust Storm, matching the C# golden", () => {
  const w = createWorld(20, 12, 9);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 5, 2)!.t = "pit";
  cellAt(w, 6, 2)!.t = "pit";
  for (let x = 3; x <= 5; x++) cellAt(w, x, 7)!.t = "grass";
  castSpell(w, "flood", 1, 2, 1);
  castSpell(w, "mud", 4, 7, 0);
  castSpell(w, "molten-boulder", 14, 3, 0);
  castSpell(w, "dust-storm", 14, 9, 0);
  assert.ok(cellAt(w, 5, 2)!.water > 0 && cellAt(w, 6, 2)!.water > 0, "and fills the pits");
  assert.deepEqual([3, 4, 5].map((x) => cellAt(w, x, 7)!.t), ["mud", "mud", "mud"]);
  assert.equal(cellAt(w, 14, 3)!.t, "lava");
  assert.equal(cellAt(w, 14, 9)!.gas, "dust");
  run(w, 12);
  assert.equal(`${cellHash(w)} ${w.rng}`, "7a38bb69 939719861"); // the same in the C# core
  const v = createWorld(10, 5, 1);
  const crate = spawn(v, "crate", 2, 2)!;
  castSpell(v, "flood", 1, 2, 1);
  assert.equal(crate.x, 4, "the wave shoves the crate along");
});

// Mirrors ChemistryTests.GlassAndIceGoIntactCrackedShattered (docs/MATERIALS.md shatter.cracks): glass ground, a
// window pane and ice take two hits. The first cracks them (a warning if a creature stands on the ice), a second in
// the same tick is the same hit, the next shatters them, and a crack goes with its ice.
test("glass and ice go intact, cracked, shattered, matching the C# golden", () => {
  const w = createWorld(14, 7, 3);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 2, 3)!.t = "glass";
  cellAt(w, 5, 3)!.t = "pane";
  cellAt(w, 5, 2)!.t = "logwall";
  for (const x of [9, 12]) {
    cellAt(w, x, 3)!.water = 2;
    cellAt(w, x, 3)!.frozen = true;
  }
  spawn(w, "goblin", 9, 3);
  for (const x of [2, 5, 12]) {
    assert.equal(shatter(w, x, 3), false, "the first hit cracks it");
    assert.equal(shatter(w, x, 3), false, "one hit a tick");
  }
  castSpell(w, "shocking-grasp", 9, 3, 0);
  assert.equal(cellAt(w, 2, 3)!.t, "glass");
  assert.equal(cellAt(w, 5, 3)!.t, "pane");
  assert.ok(cellAt(w, 9, 3)!.frozen && cellAt(w, 9, 3)!.cracked, "lightning on ice cracks it");
  assert.ok(w.log.some((l) => l.text === "The ice cracks under Goblin"));
  const cracked = cellHash(w);
  run(w, 1);
  for (const x of [2, 5, 9]) assert.ok(shatter(w, x, 3), "the next shatters it");
  assert.equal(cellAt(w, 2, 3)!.t, "sand");
  assert.equal(cellAt(w, 5, 3)!.t, "rubble");
  assert.equal(cellAt(w, 9, 3)!.frozen, false, "open water under the goblin");
  applyElement(w, 12, 3, "fire", { power: 1, direct: true });
  run(w, 1);
  assert.equal(cellAt(w, 12, 3)!.cracked, undefined, "a crack goes with its ice");
  run(w, 12);
  assert.equal(`${cracked} ${cellHash(w)} ${w.rng}`, "17d9616c 4fcd46bc 1831565816"); // the same in the C# core
});

// Mirrors ChemistryTests.ForgeHeatsAndTheMaterialDecides: sand to glass, mud to brick, metal hot, ice and snow melted,
// wet things dry, through the materials' bake and melt verbs. It lights nothing.
test("Forge heats and the material decides, matching the C# golden", () => {
  const w = createWorld(24, 5, 4);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 2, 2)!.t = "sand";
  cellAt(w, 6, 2)!.t = "mud";
  cellAt(w, 6, 2)!.water = 1;
  cellAt(w, 10, 2)!.t = "metal";
  cellAt(w, 14, 2)!.cover = "snow";
  cellAt(w, 18, 2)!.water = 2;
  cellAt(w, 18, 2)!.frozen = true;
  cellAt(w, 22, 2)!.t = "grass";
  const gob = spawn(w, "goblin", 3, 2)!;
  applyElement(w, 3, 2, "water", { power: 1, direct: true });
  assert.ok((gob.status.wet ?? 0) > 0);
  for (const x of [2, 6, 10, 14, 18, 22]) castSpell(w, "forge", x, 2, 0);
  assert.equal(cellAt(w, 2, 2)!.t, "glass");
  assert.equal(cellAt(w, 6, 2)!.t, "brick");
  assert.equal(cellAt(w, 6, 2)!.water, 0, "the mud dried");
  assert.equal(cellAt(w, 10, 2)!.heat, 120);
  assert.equal(cellAt(w, 14, 2)!.cover, null);
  assert.equal(cellAt(w, 18, 2)!.frozen, false);
  assert.equal(cellAt(w, 22, 2)!.fire, 0, "it lights nothing");
  assert.equal(gob.status.wet ?? 0, 0);
  assert.equal(RULES.terrain.brick!.hardness, 3);
  run(w, 12);
  assert.equal(`${cellHash(w)} ${w.rng}`, "1124f40e 3023719660"); // the same in the C# core
});

// Mirrors ChemistryTests.AGeyserBoilsThePondIntoAJetAndFizzlesOnDryGround: it acts on water already there; on dry
// ground it fizzles and says so.
test("a geyser boils the pond into a jet and fizzles on dry ground, matching the C# golden", () => {
  const w = createWorld(12, 7, 5);
  for (const c of w.cells) c.t = "dirt";
  for (let y = 2; y <= 4; y++) for (let x = 6; x <= 8; x++) cellAt(w, x, y)!.water = 3;
  const gob = spawn(w, "goblin", 7, 3)!;
  const dry = cellHash(w);
  castSpell(w, "geyser", 2, 3, 0);
  assert.equal(w.log[w.log.length - 1]!.text, "The geyser fizzles: there's no water there to boil");
  assert.equal(cellHash(w), dry, "nothing changes on dry ground");
  castSpell(w, "geyser", 6, 3, 0);
  assert.equal(cellAt(w, 7, 3)!.gas, "steam");
  assert.equal(cellAt(w, 7, 3)!.water, 2);
  assert.equal(cellAt(w, 5, 3)!.gas, null, "only the water jets");
  assert.ok((gob.status.thrown ?? 0) > 0);
  run(w, 12);
  assert.ok(gob.hp < 10, "the steam scalds it");
  assert.equal(`${cellHash(w)} ${w.rng} ${gob.hp}`, "4209bc06 3136449455 5"); // the same in the C# core
});

// Mirrors ChemistryTests.AWaveRollsOutDousesKnocksBackAndLeavesMud.
test("a wave rolls out, douses, knocks back and leaves mud, matching the C# golden", () => {
  const w = createWorld(16, 7, 6);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 6, 3)!.t = "grass";
  ignite(cellAt(w, 6, 3)!, 2);
  const gob = spawn(w, "goblin", 4, 2)!;
  castSpell(w, "wave", 1, 3, 1);
  assert.equal(w.waves.length, 1);
  assert.equal(cellAt(w, 1, 3)!.water, 1, "it leaves shallow water as it rolls on");
  run(w, 6);
  assert.equal(w.waves[0]!.x, 4);
  assert.deepEqual([gob.x, gob.y], [5, 2], "knocked back ahead of the wave");
  assert.ok((gob.status.wet ?? 0) > 0);
  run(w, 10);
  assert.equal(w.waves.length, 0, "it breaks at its length's end");
  assert.equal(cellAt(w, 6, 3)!.fire, 0, "the fire is out");
  assert.deepEqual([gob.x, gob.y], [9, 2]);
  for (const y of [2, 3, 4]) assert.equal(cellAt(w, 8, y)!.t, "mud", "mud where it stopped");
  assert.equal(cellAt(w, 7, 3)!.t, "dirt");
  run(w, 4);
  assert.equal(`${cellHash(w)} ${w.rng}`, "33cc6d1f 752141770"); // the same in the C# core
});

// Mirrors ChemistryTests.AWaveRollsAlongASlantedAimAndLeavesWaterBehind (GitHub #11).
test("a wave rolls along a slanted aim and leaves water behind, matching the C# golden", () => {
  const w = createWorld(20, 16, 12);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 6, 5)!.t = "grass";
  ignite(cellAt(w, 6, 5)!, 2);
  castSpell(w, "wave", 2, 2, 1, undefined, 1, [3, 2]);
  assert.equal(w.waves[0]!.dir, 1, "it pushes along the nearest grid way");
  assert.equal(cellAt(w, 2, 2)!.water, 1, "shallow water where it passed");
  run(w, 6);
  assert.deepEqual([w.waves[0]!.x, w.waves[0]!.y], [5, 4]);
  run(w, 10);
  assert.equal(w.waves.length, 0);
  assert.equal(cellAt(w, 6, 5)!.fire, 0, "the fire on its slant is out");
  for (const y of [6, 7, 8]) assert.equal(cellAt(w, 9, y)!.t, "mud", "mud where it broke, off the straight line");
  assert.equal(cellAt(w, 9, 2)!.t, "dirt", "it didn't go straight east");
  run(w, 4);
  assert.equal(`${cellHash(w)} ${w.rng}`, "5331a509 2455825195"); // the same in the C# core
});

// Mirrors ChemistryTests.HeatOnStandingWaterBoilsItAndBakesNothingUnderIt (GitHub #15).
test("heat on standing water boils it and bakes nothing under it, matching the C# golden", () => {
  const w = createWorld(14, 5, 8);
  for (const c of w.cells) c.t = "dirt";
  for (let x = 1; x <= 5; x++)
    for (let y = 1; y <= 3; y++) {
      cellAt(w, x, y)!.t = "mud";
      cellAt(w, x, y)!.water = 3;
    }
  cellAt(w, 10, 2)!.t = "sand";
  cellAt(w, 10, 2)!.water = 2;
  castSpell(w, "geyser", 3, 2, 0);
  castSpell(w, "forge", 10, 2, 0);
  assert.equal(cellAt(w, 3, 2)!.t, "mud", "no brick under the pool");
  assert.equal(cellAt(w, 3, 2)!.water, 2, "the water boiled");
  assert.equal(cellAt(w, 3, 2)!.gas, "steam");
  assert.equal(cellAt(w, 10, 2)!.t, "sand", "no glass under the water");
  assert.equal(cellAt(w, 10, 2)!.heat ?? 0, 0, "the water took the heat");
  run(w, 12);
  assert.equal(`${cellHash(w)} ${w.rng}`, "cdece807 2305213897"); // the same in the C# core
});

// Mirrors ChemistryTests.FrostBridgesWaterFreezesTheWetAndGlazesTheGround.
test("frost bridges water, freezes the wet and glazes the ground, matching the C# golden", () => {
  const w = createWorld(14, 7, 7);
  for (const c of w.cells) c.t = "dirt";
  for (let y = 0; y <= 6; y++) for (let x = 6; x <= 8; x++) cellAt(w, x, y)!.water = 3;
  const gob = spawn(w, "goblin", 3, 3)!;
  applyElement(w, 3, 3, "water", { power: 1, direct: true });
  castSpell(w, "frost", 1, 3, 1);
  for (const x of [6, 7, 8]) assert.equal(cellAt(w, x, 3)!.frozen, true, "an ice bridge");
  assert.equal(cellAt(w, 7, 2)!.frozen, false);
  assert.equal(cellAt(w, 2, 3)!.cover, "frost");
  assert.ok(isSlippery(cellAt(w, 2, 3)!) && isSlippery(cellAt(w, 7, 3)!), "slippery underfoot");
  assert.ok((gob.status.frozen ?? 0) > 0);
  castSpell(w, "ray-of-frost", 1, 5, 1);
  assert.equal(cellAt(w, 2, 5)!.cover, null, "a cantrip doesn't frost the ground");
  castSpell(w, "acid-splash", 3, 3, 0);
  assert.ok(w.entities.includes(gob) && (gob.status.cracked ?? 0) > 0, "the first hit cracks it");
  castSpell(w, "acid-splash", 3, 3, 0);
  assert.ok(!w.entities.includes(gob), "the next shatters it");
  castSpell(w, "fire-bolt", 2, 3, 0);
  assert.equal(cellAt(w, 2, 3)!.cover, null, "fire melts the frost");
  run(w, 12);
  assert.equal(`${cellHash(w)} ${w.rng}`, "4f8ffa48 1199730150"); // the same in the C# core
});

// Mirrors ChemistryTests.ARollingBoulderRollsDownhillSmashesKnocksOverAndDamsTheStream.
test("a rolling boulder rolls downhill, smashes, knocks over and dams the stream, matching the C# golden", () => {
  const w = createWorld(16, 10, 8);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 5, 4)!.t = "glass";
  cellAt(w, 6, 4)!.water = 2;
  cellAt(w, 6, 4)!.frozen = true;
  cellAt(w, 8, 5)!.h = -1;
  Object.assign(cellAt(w, 8, 6)!, { t: "pit", h: -1, water: 3 });
  const gob = spawn(w, "goblin", 7, 4)!;
  castSpell(w, "rolling-boulder", 1, 4, 1, undefined, 2);
  assert.equal(w.boulders.length, 1);
  run(w, 4);
  assert.deepEqual([w.boulders[0]!.x, w.boulders[0]!.y], [3, 4]);
  assert.equal(cellAt(w, 2, 4)!.gas, "dust", "a trail of dust");
  castSpell(w, "fire-bolt", 2, 4, 0);
  assert.ok(w.log.some((l) => l.text === "The dust goes up in a flash"));
  run(w, 8);
  assert.ok((gob.status.thrown ?? 0) > 0, "knocked over");
  assert.ok(gob.hp < 10);
  run(w, 12);
  assert.equal(w.boulders.length, 0);
  assert.ok((cellAt(w, 5, 4)!.cracked ?? 0) > 0, "the glass cracks");
  assert.ok((cellAt(w, 6, 4)!.cracked ?? 0) > 0, "and the ice");
  assert.equal(cellAt(w, 8, 6)!.t, "dirt", "it rolled down the slope and dammed the stream");
  assert.equal(`${cellHash(w)} ${w.rng} ${gob.x},${gob.y} ${gob.hp}`, "9179f2b8 729412872 7,4 5"); // the same in the C# core
});

// Mirrors ChemistryTests.ARollingBoulderRollsAlongASlantedAimAndThenDownhill (GitHub #21).
test("a rolling boulder rolls along a slanted aim and then downhill, matching the C# golden", () => {
  const w = createWorld(18, 14, 8);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 5, 4)!.t = "glass";
  for (const y of [6, 7, 8, 9, 10]) cellAt(w, 6, y)!.h = -1;
  castSpell(w, "rolling-boulder", 2, 2, 1, undefined, 1, [3, 2]);
  assert.equal(w.boulders[0]!.dir, 1, "it pushes along the nearest grid way");
  run(w, 6);
  assert.deepEqual([w.boulders[0]!.x, w.boulders[0]!.y], [5, 4], "three cells along the aim, not along a grid way");
  assert.ok((cellAt(w, 5, 4)!.cracked ?? 0) > 0, "and it strikes what's on its slant");
  run(w, 4);
  assert.deepEqual([w.boulders[0]!.x, w.boulders[0]!.y], [6, 6], "the slope to its side turns it");
  assert.equal(w.boulders[0]!.dir, 2);
  run(w, 4);
  assert.deepEqual([w.boulders[0]!.x, w.boulders[0]!.y], [6, 8], "on down its new way");
  run(w, 20);
  assert.equal(w.boulders.length, 0);
  assert.notEqual(cellAt(w, 3, 2)!.gas, "dust", "no trail along the grid way it wasn't aimed");
  assert.equal(`${cellHash(w)} ${w.rng}`, "c5a8709f 3377061646"); // the same in the C# core
});

// Mirrors ChemistryTests.PlantGrowthVinesClimbWallsBridgeGapsBurnAndRootsCrackStone.
test("Plant Growth's vines climb walls, bridge gaps, burn, and their roots crack stone, matching the C# golden", () => {
  const w = createWorld(12, 7, 9);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 3, 3)!.t = "stone";
  cellAt(w, 5, 3)!.t = "pit";
  cellAt(w, 7, 3)!.t = "river";
  Object.assign(cellAt(w, 9, 3)!, { t: "rock", h: 3 });
  cellAt(w, 6, 5)!.t = "pit";
  castSpell(w, "plant-growth", 6, 3, 0);
  for (const x of [3, 5, 7, 9]) assert.ok(isClimbable(cellAt(w, x, 3)!), `vines at ${x}`);
  assert.equal(cellAt(w, 4, 3)!.cover, null, "nothing to climb on open ground");
  applyElement(w, 6, 5, "fire", { power: 1, direct: true });
  assert.ok(cellAt(w, 6, 5)!.fire > 0, "vines burn");
  run(w, 920);
  assert.equal(cellAt(w, 3, 3)!.t, "rubble", "roots crack the stone wall");
  assert.equal(cellAt(w, 9, 3)!.t, "rock");
  assert.equal(cellAt(w, 5, 3)!.cover, null, "the vines rot away");
  assert.equal(`${cellHash(w)} ${w.rng}`, "f70d280c 1696107128"); // the same in the C# core
});

// Mirrors ChemistryTests.WallOfStoneMakesStepsOverWaterRampsWhenHeldAndOnlyHardBlowsBreakIt.
test("Wall of Stone makes steps over water, a ramp when held, and only hard blows break it, matching the C# golden", () => {
  const w = createWorld(14, 8, 10);
  for (const c of w.cells) c.t = "dirt";
  for (let y = 0; y <= 7; y++) for (let x = 6; x <= 7; x++) cellAt(w, x, y)!.water = 3;
  castSpell(w, "wall-of-stone", 2, 3, 1);
  assert.equal(cellAt(w, 4, 3)!.h, 3);
  assert.ok(isBlocking(cellAt(w, 4, 3)!));
  assert.equal(cellAt(w, 6, 3)!.h, 1, "a stepping stone");
  assert.equal(cellAt(w, 6, 3)!.t, "rock");
  assert.ok(!isBlocking(cellAt(w, 6, 3)!));
  castSpell(w, "stone-ramp", 2, 6, 1);
  assert.deepEqual([2, 3, 4, 5].map((x) => cellAt(w, x, 6)!.h), [1, 2, 2, 2], "a ramp up");
  castSpell(w, "fireball", 3, 3, 0);
  assert.equal(cellAt(w, 3, 3)!.h, 3, "too hard for a fireball");
  castSpell(w, "falling-star", 3, 3, 0);
  run(w, 6);
  assert.ok(cellAt(w, 3, 3)!.h < 3, "a falling star breaks it");
  run(w, 6);
  assert.equal(`${cellHash(w)} ${w.rng}`, "4432219c 1218213550"); // the same in the C# core
});

// Mirrors ChemistryTests.ADustFlashShovesARaiderTheSameAsTheSandbox.
test("a dust flash shoves a raider the same in both engines, matching the C# golden", () => {
  const w = createWorld(10, 5, 11);
  for (const c of w.cells) c.t = "dirt";
  for (let x = 3; x <= 5; x++) addGas(cellAt(w, x, 2)!, "dust", 3);
  const hound = spawn(w, "bone-hound", 5, 2)!;
  applyElement(w, 3, 2, "fire", { power: 1, direct: true });
  assert.deepEqual([hound.x, hound.y], [7, 2], "shoved two cells away from the spark");
  run(w, 6);
  assert.equal(`${cellHash(w)} ${w.rng} ${hound.x},${hound.y} ${hound.hp}`, "31a36a85 3279484455 7,2 6"); // the same in the C# core
});

// Mirrors ChemistryTests.LightningJumpsToAPuddleAndItsChargeOpensTheGate.
test("lightning jumps to a puddle and its charge opens the gate, matching the C# golden", () => {
  const w = createWorld(16, 7, 12);
  for (const c of w.cells) c.t = "dirt";
  for (let x = 4; x <= 6; x++) cellAt(w, x, 5)!.water = 2;
  cellAt(w, 7, 5)!.t = "coil";
  cellAt(w, 12, 3)!.t = "gate";
  castSpell(w, "lightning-bolt", 2, 3, 1);
  assert.ok(cellAt(w, 4, 5)!.charge > 0, "it jumped to the puddle");
  assert.ok(cellAt(w, 7, 5)!.charge > 0, "and ran through it to the coil");
  assert.equal(w.gatesOpen, false);
  step(w);
  assert.equal(w.gatesOpen, true);
  assert.equal(cellAt(w, 12, 3)!.t, "gate_open");
  assert.ok(w.log.some((l) => l.text === "The gate grinds open, powered by the lightning coil"));
  run(w, 30);
  assert.equal(cellAt(w, 12, 3)!.t, "gate", "it shuts once the coil's hold runs out");
  const dry = createWorld(16, 7, 12);
  for (const c of dry.cells) c.t = "dirt";
  cellAt(dry, 10, 3)!.t = "sand";
  castSpell(dry, "lightning-bolt", 2, 3, 1);
  assert.equal(cellAt(dry, 10, 3)!.t, "glass", "with nothing to jump to it flies straight");
  assert.equal(`${cellHash(w)} ${w.rng}`, "c4741e7f 12"); // the same in the C# core
});

// Mirrors ChemistryTests.RainCloudsGreenAshLightningMakesAStormAndColdMakesSnow.
test("rain clouds green ash, lightning makes a storm and cold makes snow, matching the C# golden", () => {
  const w = createWorld(16, 7, 13);
  for (const c of w.cells) c.t = "dirt";
  for (let y = 1; y <= 5; y++) for (let x = 1; x <= 5; x++) cellAt(w, x, y)!.cover = "ash";
  castSpell(w, "rain-cloud", 3, 3, 0);
  castSpell(w, "rain-cloud", 11, 3, 0);
  castSpell(w, "shocking-grasp", 3, 3, 0);
  assert.equal(cellAt(w, 3, 3)!.gas, "storm");
  applyElement(w, 11, 3, "cold", { power: 1, direct: true });
  assert.equal(cellAt(w, 11, 3)!.gas, "snowcloud");
  run(w, 120);
  assert.ok(w.cells.some((c) => c.t === "sprouts" || c.t === "grass"), "the rain greens the ash");
  assert.ok(w.cells.some((c) => c.cover === "snow"), "the snow cloud snows");
  assert.equal(`${cellHash(w)} ${w.rng}`, "788f5f20 1615770602"); // the same in the C# core
});

// Mirrors ChemistryTests.SandBlastScoursWearsAndLeavesSandAndGustDrivesOilAcrossAPond.
test("Sand Blast scours, wears and leaves sand, and Gust drives oil across a pond, matching the C# golden", () => {
  const w = createWorld(14, 7, 14);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 2, 2)!.cover = "snow";
  cellAt(w, 3, 2)!.t = "rusted-metal";
  cellAt(w, 4, 2)!.h = 2;
  cellAt(w, 5, 2)!.t = "logwall";
  castSpell(w, "sand-blast", 1, 2, 1);
  assert.equal(cellAt(w, 2, 2)!.cover, null);
  assert.equal(cellAt(w, 2, 2)!.t, "sand");
  assert.equal(cellAt(w, 3, 2)!.t, "metal", "the rust scoured off");
  assert.equal(cellAt(w, 4, 2)!.h, 1, "raised earth worn down a step");
  assert.equal(cellAt(w, 5, 2)!.t, "worn-logwall");
  castSpell(w, "sand-blast", 1, 2, 1);
  assert.equal(cellAt(w, 5, 2)!.t, "rubble", "and then through");
  castSpell(w, "forge", 2, 2, 0);
  assert.equal(cellAt(w, 2, 2)!.t, "glass");
  for (let x = 3; x <= 9; x++) cellAt(w, x, 5)!.water = 3;
  cellAt(w, 3, 5)!.cover = "oil";
  castSpell(w, "gust-of-wind", 1, 5, 1);
  assert.equal(cellAt(w, 3, 5)!.cover, null);
  assert.equal(cellAt(w, 9, 5)!.cover, "oil", "driven across the pond");
  run(w, 6);
  assert.equal(`${cellHash(w)} ${w.rng}`, "7716f25c 14"); // the same in the C# core
});

// Mirrors ChemistryTests.LavaRunsDownhillLeavingACrustAndSetsAtOnceInWater.
test("lava runs downhill leaving a crust and sets at once in water, matching the C# golden", () => {
  const w = createWorld(12, 7, 15);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 5, 3)!.h = -1;
  cellAt(w, 6, 3)!.h = -2;
  Object.assign(cellAt(w, 7, 3)!, { t: "pit", h: -2, water: 3 });
  castSpell(w, "lava-pool", 3, 3, 0);
  assert.equal(cellAt(w, 4, 3)!.t, "lava-pool");
  run(w, 6);
  assert.equal(cellAt(w, 5, 3)!.t, "lava-pool", "it runs downhill");
  assert.equal(cellAt(w, 4, 3)!.t, "rock", "leaving a crust to walk on");
  run(w, 13);
  assert.equal(cellAt(w, 7, 3)!.t, "rock", "and sets at once in the water");
  assert.ok(w.log.some((l) => l.text === "The lava hisses and sets into rock"));
  run(w, 6);
  assert.equal(`${cellHash(w)} ${w.rng}`, "811d9fa2 2967354774"); // the same in the C# core
});

// Mirrors ChemistryTests.ScaldingSteamRustsMetalFaster.
test("Scalding Steam rusts metal faster, matching the C# golden", () => {
  const w = createWorld(8, 5, 16);
  for (const c of w.cells) c.t = "dirt";
  Object.assign(cellAt(w, 2, 2)!, { t: "metal", water: 1 });
  Object.assign(cellAt(w, 5, 2)!, { t: "metal", water: 1 });
  addGas(cellAt(w, 5, 2)!, "steam", 30);
  run(w, 8);
  assert.ok(cellAt(w, 5, 2)!.rust! > 2 * cellAt(w, 2, 2)!.rust!, "faster under steam");
  assert.ok((cellAt(w, 2, 2)!.rust ?? 0) > 0);
  assert.equal(`${cellHash(w)} ${w.rng}`, "3bf05b30 2775531184"); // the same in the C# core
});

// Mirrors ChemistryTests.CreateWaterWatersBurntGround.
test("Create Water waters burnt ground, matching the C# golden", () => {
  const w = createWorld(8, 6, 17);
  for (const c of w.cells) c.t = "dirt";
  for (let y = 1; y <= 4; y++) for (let x = 1; x <= 4; x++) cellAt(w, x, y)!.cover = "ash";
  castSpell(w, "create-water", 2, 2, 0);
  assert.equal(cellAt(w, 2, 2)!.cover, null);
  assert.ok(cellAt(w, 2, 2)!.water > 0);
  run(w, 80);
  assert.ok(["sprouts", "grass"].includes(cellAt(w, 2, 2)!.t), "green shoots in the ash");
  assert.equal(cellAt(w, 4, 4)!.cover, "ash", "only where the water landed");
  assert.equal(`${cellHash(w)} ${w.rng}`, "49ebceb 3599190446"); // the same in the C# core
});

// Mirrors ChemistryTests.AFallingStarMarksItsTargetThenStrikes: a mark for a second (6 ticks), then a crater with a
// little lava in it, burning rock round it, snow and ice melted and water flashed to steam.
test("a falling star marks its target, then strikes, matching the C# golden", () => {
  const w = createWorld(15, 9, 6);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 8, 4)!.cover = "snow";
  cellAt(w, 7, 3)!.water = 2;
  cellAt(w, 7, 3)!.frozen = true;
  cellAt(w, 6, 4)!.water = 2;
  castSpell(w, "falling-star", 7, 4, 0);
  assert.equal(cellAt(w, 7, 4)!.star, 1);
  assert.equal(cellAt(w, 6, 3)!.star, 1);
  assert.equal(cellAt(w, 5, 4)!.star, undefined);
  const back = deserializeWorld(serializeWorld(w));
  run(w, 5);
  assert.equal(cellAt(w, 7, 4)!.h, 0, "still falling");
  step(w);
  assert.equal(cellAt(w, 7, 4)!.star, undefined, "the mark goes when it lands");
  assert.equal(w.stars.length, 0);
  assert.ok(cellAt(w, 7, 4)!.h < 0);
  assert.equal(cellAt(w, 7, 4)!.t, "lava-pool");
  assert.ok(cellAt(w, 7, 4)!.heat! > 0);
  assert.notEqual(cellAt(w, 8, 4)!.cover, "snow");
  assert.equal(cellAt(w, 7, 3)!.frozen, false);
  assert.equal(cellAt(w, 6, 4)!.gas, "steam");
  assert.ok(w.cells.some((c) => c.cover === "embers" && c.fire > 0));
  run(w, 12);
  assert.equal(`${cellHash(w)} ${w.rng}`, "c91f14e2 657895373"); // the same in the C# core
  run(back, 18);
  assert.equal(hashWorld(back), hashWorld(w), "a star on its way down comes through a snapshot");
});

// Mirrors ChemistryTests.AWhirlwindDrawsGasAndCreaturesInAndRunsFireRoundItsRing: a whirlwind on a field with smoke off
// to one side, a goblin and a bone hound on its edge and grass along its top ring; fire lit on the ring runs round it,
// the smoke and the creatures come in toward the middle, and after its ten seconds it's gone.
test("a whirlwind draws gas and creatures in and runs fire round its ring, matching the C# golden", () => {
  const w = createWorld(15, 9, 4);
  for (const c of w.cells) c.t = "dirt";
  for (let x = 5; x <= 9; x++) cellAt(w, x, 2)!.t = "grass";
  const gob = spawn(w, "goblin", 5, 4)!, hound = spawn(w, "bone-hound", 9, 6)!;
  castSpell(w, "whirlwind", 7, 4, 0);
  assert.equal(w.whirls.length, 1);
  cellAt(w, 9, 4)!.gas = "smoke";
  cellAt(w, 9, 4)!.gasAmt = 3;
  applyElement(w, 5, 2, "fire", { power: 3, direct: true });
  run(w, 3);
  assert.ok(cellAt(w, 6, 2)!.fire > 0, "the fire runs east along the top ring");
  assert.deepEqual([gob.x, gob.y], [6, 4], "the goblin is drawn a cell in");
  assert.equal(cellAt(w, 8, 4)!.gas, "smoke", "and the smoke");
  run(w, 30);
  const mid = `${cellHash(w)} ${w.rng} ${gob.x},${gob.y} ${hound.x},${hound.y}`;
  run(w, 30);
  assert.equal(w.whirls.length, 0, "gone after its duration");
  assert.equal(`${mid} | ${cellHash(w)} ${w.rng}`, "62e2a56e 3068262754 7,0 7,1 | ab58829b 901838356"); // the same in the C# core
});

test("a whirlwind drags loose props in, leaves the heavy ones and the player, and comes through a snapshot", () => {
  const w = createWorld(15, 9, 4);
  for (const c of w.cells) c.t = "dirt";
  const crate = spawn(w, "crate", 5, 4)!, boulder = spawn(w, "boulder", 9, 4)!, wizard = spawn(w, "wizard", 7, 2)!;
  castSpell(w, "whirlwind", 7, 4, 0);
  run(w, 3);
  assert.deepEqual([crate.x, crate.y], [6, 4], "the crate comes a cell in");
  assert.deepEqual([boulder.x, boulder.y], [9, 4], "the boulder is too heavy");
  assert.deepEqual([wizard.x, wizard.y], [7, 2], "the player stays put");
  const back = deserializeWorld(serializeWorld(w));
  assert.equal(hashWorld(back), hashWorld(w));
  run(w, 3);
  run(back, 3);
  assert.equal(back.entities.find((e) => e.kind === "crate")!.x, 7, "and on into the middle");
  assert.equal(hashWorld(back), hashWorld(w));
});

const field = (width: number, height: number, seed: number, t: string): World => {
  const w = createWorld(width, height, seed);
  for (const c of w.cells) c.t = t;
  return w;
};

// A whirlwind cast south of a cell and heading north, 15 ticks on: its eye has crossed it.
function crossing(set: (c: Cell) => void): World {
  const w = field(9, 12, 1, "dirt");
  set(cellAt(w, 4, 5)!);
  castSpell(w, "whirlwind", 4, 8, 0);
  run(w, 15);
  return w;
}

// Mirrors TerraformTests.AWhirlwindTakesOnWhatItCrosses.
test("a whirlwind takes on what it crosses", () => {
  assert.equal(crossing((c) => { c.fire = 3; c.fuel = 40; }).whirls[0]!.carrying, "fire");
  assert.equal(crossing((c) => { c.t = "pit"; c.water = 4; }).whirls[0]!.carrying, "water");
  assert.equal(crossing((c) => { c.t = "sand"; }).whirls[0]!.carrying, "sand");
  assert.equal(crossing((c) => { c.gas = "smoke"; c.gasAmt = 6; }).whirls[0]!.carrying, "smoke");
  assert.equal(crossing((c) => { c.cover = "snow"; }).whirls[0]!.carrying, "snow");
  assert.equal(crossing(() => {}).whirls[0]!.carrying, null, "bare ground gives it nothing");
  assert.ok(crossing((c) => { c.t = "sand"; }).log.some((l) => l.text === "The whirlwind whips the sand into a sandstorm"));
});

test("a whirlwind goes with the wind or the way it was cast, and a wall stops it", () => {
  const cast = field(12, 9, 1, "dirt");
  castSpell(cast, "whirlwind", 4, 4, 1);
  run(cast, 12);
  assert.deepEqual([cast.whirls[0]!.x, cast.whirls[0]!.y], [6, 4], "east, as cast, a cell every 6 ticks");
  const windy = field(12, 9, 1, "dirt");
  windy.wind = { dir: 2, ticks: 100 };
  castSpell(windy, "whirlwind", 4, 2, 1);
  run(windy, 12);
  assert.deepEqual([windy.whirls[0]!.x, windy.whirls[0]!.y], [4, 4], "the wind wins: south");
  const walled = field(12, 9, 1, "dirt");
  cellAt(walled, 5, 4)!.t = "bedrock";
  castSpell(walled, "whirlwind", 4, 4, 1);
  run(walled, 12);
  assert.equal(walled.whirls[0]!.x, 4);
});

test("a waterspout drops its water where it dies out", () => {
  const w = field(20, 9, 1, "dirt");
  cellAt(w, 4, 4)!.t = "pit";
  cellAt(w, 4, 4)!.water = 4;
  castSpell(w, "whirlwind", 4, 4, 1);
  run(w, 60);
  assert.equal(w.whirls.length, 0);
  assert.ok(cellAt(w, 14, 4)!.water >= 2);
  assert.ok(w.log.some((l) => l.text === "The waterspout dies out and drops what it carried"));
});

// Mirrors TerraformTests.GoldenFireTornadoMatchesTs.
test("a fire tornado lights what it passes and drops fire where it ends, matching the C# golden", () => {
  const w = field(20, 9, 3, "grass");
  cellAt(w, 4, 4)!.fire = 3;
  cellAt(w, 4, 4)!.fuel = 8;
  castSpell(w, "whirlwind", 4, 4, 1);
  run(w, 30);
  const mid = `${whirlState(w)} ${cellHash(w)} ${w.rng}`;
  run(w, 30);
  assert.equal(w.whirls.length, 0);
  assert.equal(`${mid} | ${cellHash(w)} ${w.rng}`, "|9:4:30:fire e6fa9db8 2713091359 | 47709c01 321794052");
});

// Mirrors TerraformTests.GoldenDustFlashMatchesTs.
test("a dust cloud flashes all at once when fire meets it, matching the C# golden", () => {
  const w = field(16, 9, 1, "dirt");
  for (let y = 0; y < 9; y++) for (let x = 10; x < 16; x++) cellAt(w, x, y)!.t = "grass";
  castSpell(w, "dust-storm", 6, 4, 0);
  cellAt(w, 13, 4)!.gas = "dust";
  cellAt(w, 13, 4)!.gasAmt = 3;
  applyElement(w, 4, 4, "fire", { power: 1, direct: true });
  assert.equal(w.cells.filter((c) => c.gas === "dust").length, 1, "only the far cloud is left");
  assert.equal(cellAt(w, 6, 4)!.fire, 3);
  assert.ok(w.log.some((l) => l.text === "The dust goes up in a flash"));
  run(w, 20);
  assert.equal(`${cellHash(w)} ${w.rng}`, "28826ce6 2354002487");
});

test("dust drifting over a fire flashes", () => {
  const w = field(8, 8, 1, "dirt");
  Object.assign(cellAt(w, 3, 3)!, { fire: 2, fuel: 6, gas: "dust", gasAmt: 2 });
  Object.assign(cellAt(w, 4, 3)!, { gas: "dust", gasAmt: 2 });
  step(w);
  assert.equal(cellAt(w, 3, 3)!.gas, null);
  assert.equal(cellAt(w, 4, 3)!.gas, null);
  assert.ok(cellAt(w, 4, 3)!.fire > 0);
});

test("a sandstorm's dust goes up too", () => {
  const w = field(14, 9, 1, "sand");
  castSpell(w, "whirlwind", 4, 4, 1);
  run(w, 9);
  assert.equal(w.whirls[0]!.carrying, "sand");
  const x = w.whirls[0]!.x;
  assert.equal(cellAt(w, x, 4)!.gas, "dust");
  applyElement(w, x, 4, "fire", { power: 1, direct: true });
  assert.equal(cellAt(w, x, 4)!.gas, null);
  assert.ok(w.log.some((l) => l.text === "The dust goes up in a flash"));
});

test("a flash shoves what stands in the cloud away from where it caught", () => {
  const w = field(14, 9, 1, "dirt");
  const crate = spawn(w, "crate", 6, 4)!;
  castSpell(w, "dust-storm", 6, 4, 0);
  applyElement(w, 4, 4, "fire", { power: 1, direct: true });
  assert.ok(crate.x > 6, "shoved east");
});

test("carries and flashes are checked", () => {
  const r = composeRules([]);
  const errors = validateRules({
    ...r,
    gases: { ...r.gases, flour: { name: "Flour", burn: { flash: { fire: 0, push: -1 } } } },
    spells: [...r.spells, { ...r.spells.find((s) => s.id === "gust-of-wind")!, id: "gyre", travelEvery: 0, carries: [{ id: "x", name: "x", if: { hue: 1 } as never, sheds: "nothing" }] }],
  });
  for (const e of ["gas flour: burn.flash.fire must be a whole number from 1", "gas flour: burn.flash.push must be a whole number from 0",
    "spell gyre: travelEvery must be a whole number from 1", "spell gyre: carries x sheds unknown element nothing", "spell gyre: carries x has unknown condition \"hue\""]) assert.ok(errors.includes(e), e);
});

// Mirrors GolfTests.ATeeShotAPenaltyACarryOverThePondAndHoledInSix.
test("spell golf: a tee shot, a ball lost in the pond, a carry over it and holed in 6, matching the C# golden", () => {
  const w = createWorld(40, 12, 3);
  for (const c of w.cells) c.t = "dirt";
  for (let y = 3; y <= 7; y++) {
    cellAt(w, 11, y)!.h = 1; // a rise to climb
    cellAt(w, 12, y)!.h = 1;
    for (const x of [17, 18]) Object.assign(cellAt(w, x, y)!, { t: "pit", h: -1, water: 3 }); // a pond across the way
  }
  startGolf(w, 4, 5, 30, 5);
  const stroke = (id: string, x: number, y: number) => { castSpell(w, id, x, y, 1); run(w, 24); return `${w.golf!.x},${w.golf!.y}`; };
  assert.equal(stroke("gust-of-wind", 2, 5), "14,5", "the tee shot rolls over the rise");
  assert.equal(stroke("gust-of-wind", 12, 5), "4,5", "into the pond: back on the tee");
  assert.equal(w.golf!.strokes, 3, "a stroke's penalty");
  assert.equal(stroke("gust-of-wind", 2, 5), "14,5");
  assert.equal(stroke("mold-earth-heap", 14, 5), "22,5", "Raise Earth throws it over the pond");
  assert.equal(stroke("gust-of-wind", 20, 5), "30,5");
  assert.ok(w.golf!.holed && w.log.some((l) => l.text === "Holed in 6!"));
  assert.equal(`${golfState(w)} ${fnv1a(golfState(w))} ${cellHash(w)} ${w.rng}`, "|g30:5:6:1:1:0:0:8 afbde760 4fbe03ad 3"); // the same in the C# core
});

// Mirrors GolfTests.GustKnocksARaiderBackOnceAndRaiseEarthThrowsOneOffItsFeet.
test("Gust of Wind knocks a raider back once, Raise Earth throws one off its feet, matching the C# golden", () => {
  const w = createWorld(20, 8, 5);
  for (const c of w.cells) c.t = "dirt";
  const hound = spawn(w, "bone-hound", 5, 3)!, crab = spawn(w, "ash-crab", 10, 6)!;
  castSpell(w, "gust-of-wind", 2, 3, 1);
  assert.deepEqual([hound.x, hound.y], [7, 3], "force.push 2, once: not again on the cells it's knocked onto");
  castSpell(w, "mold-earth-heap", 10, 6, 0);
  assert.deepEqual([crab.x, crab.y, crab.status.thrown], [10, 6, 2], "force.throw 2");
  assert.equal(cellAt(w, 10, 6)!.h, 1);
  run(w, 3);
  assert.equal(`${cellHash(w)} ${w.rng} ${hound.x},${hound.y} ${crab.status.thrown ?? 0}`, "65a33bbd 5 7,3 0"); // the same in the C# core
});
