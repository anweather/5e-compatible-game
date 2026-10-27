// Nature: Plant Growth regrows burnt ground (green shoots, then grass, out from
// the middle; saplings from old stumps that grow into trees) and Spike Growth
// lays thorns that hurt and slow, burn and wither. The golden hash is shared
// with the C# port's NatureTests (unity/Assets/Prototype/Chemistry/Tests/NatureTests.cs).
import { test } from "node:test";
import assert from "node:assert/strict";
import { RULES, composeRules, validateRules } from "../src/rules.ts";
import { step } from "../src/sim.ts";
import { castSpell, spellById, spellCells } from "../src/actions.ts";
import { spawn, walk } from "../src/entities.ts";
import { type World, cellAt, createWorld } from "../src/world.ts";

const run = (w: World, ticks: number) => { for (let i = 0; i < ticks; i++) step(w); };

function room(w: number, h: number, seed = 7): World {
  const world = createWorld(w, h, seed);
  for (let x = 0; x < w; x++) for (let y = 0; y < h; y++) {
    if (x === 0 || y === 0 || x === w - 1 || y === h - 1) cellAt(world, x, y)!.t = "bedrock";
  }
  return world;
}

/** World.CellHash in the C# port, life included (written only when it isn't 0). */
function cellHash(w: World): string {
  const s = w.cells.map((c) =>
    `${c.t},${c.water},${c.frozen ? 1 : 0},${c.cover ?? ""},${c.fire},${c.fuel},${c.gas ?? ""},${c.gasAmt},${c.charge}${c.h ? `,h${c.h}` : ""}${c.life ? `,l${c.life}` : ""};`).join("");
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 0x01000193);
  return (h >>> 0).toString(16);
}

/** Ground fire has been over: dirt under ash inside the room, with old stumps where trees stood. */
function burnt(w: World, stumps: [number, number][] = []) {
  for (let x = 1; x < w.w - 1; x++) for (let y = 1; y < w.h - 1; y++) Object.assign(cellAt(w, x, y)!, { t: "dirt", cover: "ash" });
  for (const [x, y] of stumps) cellAt(w, x, y)!.t = "stump";
}

const count = (w: World, t: string) => w.cells.filter((c) => c.t === t).length;

test("the nature spells validate", () => {
  assert.deepEqual(validateRules(RULES), []);
  assert.equal(spellById("plant-growth")!.group, "nature");
  assert.equal(spellById("spike-growth")!.group, "nature");
});

test("Plant Growth reaches further with each level of charge", () => {
  const w = room(21, 21);
  const s = spellById("plant-growth")!;
  assert.deepEqual([1, 2, 3].map((charge) => spellCells(w, s, 10, 10, 1, charge).length), [81, 121, 169]);
});

test("Plant Growth turns burnt ground back to grass, from the middle out", () => {
  const w = room(15, 15);
  burnt(w);
  castSpell(w, "plant-growth", 7, 7, 1);
  assert.equal(cellAt(w, 7, 7)!.t, "sprouts");
  assert.equal(cellAt(w, 7, 7)!.cover, null, "the ash is cleared");
  assert.equal(cellAt(w, 11, 7)!.t, "sprouts", "4 squares out");
  assert.equal(cellAt(w, 12, 7)!.cover, "ash", "5 squares out is left");
  assert.ok(cellAt(w, 7, 7)!.life < cellAt(w, 11, 11)!.life, "the middle ripens first");
  run(w, 10);
  assert.equal(cellAt(w, 7, 7)!.t, "grass");
  assert.equal(cellAt(w, 11, 11)!.t, "sprouts");
  run(w, 20);
  assert.equal(count(w, "grass"), 81);
  assert.equal(cellAt(w, 12, 7)!.t, "dirt");
});

test("a sapling springs from an old stump and grows into a tree", () => {
  const w = room(15, 15);
  burnt(w, [[7, 7]]);
  castSpell(w, "plant-growth", 7, 7, 1);
  const c = cellAt(w, 7, 7)!;
  assert.equal(c.t, "sapling");
  assert.ok(c.life >= 90 && c.life <= 150, `life ${c.life}`);
  run(w, 89);
  assert.equal(c.t, "sapling");
  run(w, 61);
  assert.equal(c.t, "tree");
  assert.equal(c.life, 0);
  assert.ok(w.log.some((l) => l.text === "A sapling grows into a young tree"));
});

test("nothing grows on stone, water or a wall of raised earth, nor while it burns", () => {
  const w = room(15, 15);
  burnt(w);
  Object.assign(cellAt(w, 6, 7)!, { t: "stone", cover: null });
  Object.assign(cellAt(w, 8, 7)!, { t: "pit", cover: null, water: 4 });
  Object.assign(cellAt(w, 7, 6)!, { h: 3 });
  Object.assign(cellAt(w, 7, 8)!, { fire: 3, fuel: 9 });
  castSpell(w, "plant-growth", 7, 7, 1);
  assert.equal(cellAt(w, 6, 7)!.t, "stone");
  assert.equal(cellAt(w, 8, 7)!.t, "pit");
  assert.equal(cellAt(w, 7, 6)!.t, "dirt", "the wall stays bare earth");
  assert.equal(cellAt(w, 7, 8)!.t, "dirt", "a blaze isn't smothered");
});

test("charged, Plant Growth seeds saplings in the ash; the same cast always grows the same", () => {
  const grow = () => {
    const w = room(17, 17);
    burnt(w, [[5, 5], [11, 10]]);
    castSpell(w, "plant-growth", 8, 8, 1, undefined, 2);
    const saplings = count(w, "sapling");
    run(w, 40);
    const early = `${cellHash(w)} ${w.rng}`;
    run(w, 260);
    return { w, saplings, early };
  };
  const { w, saplings, early } = grow();
  assert.ok(saplings > 2, `${saplings} saplings`);
  assert.equal(count(w, "tree"), saplings, "every sapling is a tree by now");
  assert.equal(count(w, "sapling"), 0);
  assert.equal(`${cellHash(w)} ${w.rng}`, `${cellHash(grow().w)} ${grow().w.rng}`);
  // Golden, shared with NatureTests.ChargedPlantGrowthGoldenMatchesTs.
  assert.equal(early, "e22869f7 1489130935", "40 ticks on: grass, saplings still growing");
  assert.equal(`${cellHash(w)} ${w.rng} ${saplings}`, "527e32aa 1489130935 24");
});

test("Spike Growth lays thorns that last a minute, then wither", () => {
  const w = room(15, 15);
  castSpell(w, "spike-growth", 7, 7, 1);
  const c = cellAt(w, 9, 9)!;
  assert.equal(c.cover, "thorns");
  assert.equal(c.life, 360);
  assert.equal(cellAt(w, 12, 7)!.cover, null, "5 squares out is left");
  run(w, 359);
  assert.equal(c.cover, "thorns");
  run(w, 1);
  assert.equal(c.cover, null);
  assert.ok(w.log.some((l) => l.text === "The thorns wither away"));
});

test("thorns burn away to ash", () => {
  const w = room(15, 15);
  castSpell(w, "spike-growth", 7, 7, 1);
  castSpell(w, "fire-bolt", 7, 7, 1);
  assert.ok(cellAt(w, 7, 7)!.fire > 0, "the thorns catch");
  run(w, 120);
  const ash = w.cells.filter((c) => c.cover === "ash").length;
  assert.ok(ash > 5, `${ash} squares of ash`);
  assert.equal(cellAt(w, 7, 7)!.cover, "ash");
});

test("walking through thorns hurts and slows a creature", () => {
  const w = room(15, 9);
  const goblin = spawn(w, "goblin", 2, 4)!;
  castSpell(w, "spike-growth", 8, 4, 1);
  assert.equal(walk(w, goblin, 1), null, "onto open ground");
  assert.equal(goblin.hp, 10);
  assert.equal(walk(w, goblin, 1), null, "into the thorns");
  assert.ok(goblin.hp < 10, "cut");
  assert.equal(goblin.status.snagged, 6, "a step takes twice as long at half speed");
  assert.match(walk(w, goblin, 1)!, /snag/);
  run(w, 6);
  const hp = goblin.hp;
  assert.equal(walk(w, goblin, 1), null, "free again");
  assert.ok(goblin.hp < hp, "cut again");
  const crate = spawn(w, "crate", 8, 2)!;
  assert.equal(walk(w, crate, 2), null);
  assert.equal(crate.hp, 6, "an object isn't hurt");
});

test("broken growth rules are refused with the C# wording", () => {
  const r = composeRules([{
    terrain: { weed: { name: "Weed", tags: ["ground"], growsTo: "nope" } },
    covers: { nettles: { name: "Nettles", tags: [], damage: [1] as unknown as [number, number] } },
    reactions: { add: [{ id: "g", element: "growth", do: [{ grow: "tree" }, { grow: "sprouts", ticks: [5, 2] }] }] },
  }]);
  assert.deepEqual(validateRules(r), [
    "terrain weed: growsTo nope unknown",
    "cover nettles: damage must be [dice, sides]",
    "rule g: grow tree is blocking (let it growsTo one)",
    "rule g: ticks must be [min, max], whole numbers from 1",
  ]);
});
