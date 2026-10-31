// Gates linked to their own machine (linkGate, sim.ts): two puzzles side by side don't open each other's gates.
// Mirrors PuzzleTests.TwoLinkedPuzzlesDontOpenEachOther in the C# core, with the same golden.
import { test } from "node:test";
import assert from "node:assert/strict";
import { linkGate, step } from "../src/sim.ts";
import { type World, cellAt, createWorld, fnv1a, serializeWorld, deserializeWorld } from "../src/world.ts";

const cellHash = (w: World) => fnv1a(w.cells.map((c) =>
  `${c.t},${c.water},${c.frozen ? 1 : 0},${c.cover ?? ""},${c.fire},${c.fuel},${c.gas ?? ""},${c.gasAmt},${c.charge}${c.h ? `,h${c.h}` : ""}${c.life ? `,l${c.life}` : ""}${c.rust ? `,r${c.rust}` : ""}${c.heat ? `,t${c.heat}` : ""}${c.cracked ? `,c${c.cracked}` : ""};`).join(""));

const run = (w: World, n: number) => { for (let i = 0; i < n; i++) step(w); };
const has = (w: World, text: string) => w.log.some((l) => l.text === text);

test("two linked puzzles don't open each other's gates, nor the gate no one linked, matching the C# golden", () => {
  const w = createWorld(20, 9, 12);
  for (const c of w.cells) c.t = "dirt";
  // Puzzle A (north) and puzzle B (south): a coil and its gate each; a coil and a gate no one linked to the east.
  cellAt(w, 4, 1)!.t = "coil";
  cellAt(w, 9, 1)!.t = "gate";
  cellAt(w, 4, 7)!.t = "coil";
  cellAt(w, 9, 7)!.t = "gate";
  cellAt(w, 16, 1)!.t = "coil";
  cellAt(w, 16, 7)!.t = "gate";
  linkGate(w, 9, 1, 4, 1);
  linkGate(w, 9, 7, 4, 7);
  step(w);

  cellAt(w, 4, 1)!.charge = 2;
  step(w);
  assert.equal(cellAt(w, 9, 1)!.t, "gate_open", "A's coil opens A's gate");
  assert.equal(cellAt(w, 9, 7)!.t, "gate", "but not B's");
  assert.equal(cellAt(w, 16, 7)!.t, "gate", "nor the gate no one linked");
  assert.equal(w.gatesOpen, false);
  assert.ok(has(w, "The gate grinds open, powered by the lightning coil"));

  cellAt(w, 16, 1)!.charge = 2;
  step(w);
  assert.equal(cellAt(w, 16, 7)!.t, "gate_open", "the coil no one linked opens the gate no one linked");
  assert.equal(cellAt(w, 9, 7)!.t, "gate", "and still not B's");

  // Through a save, the links hold.
  const back = deserializeWorld(serializeWorld(w));
  cellAt(back, 4, 7)!.charge = 2;
  step(back);
  assert.equal(cellAt(back, 9, 7)!.t, "gate_open", "B's coil opens B's gate");

  run(w, 30);
  assert.equal(cellAt(w, 9, 1)!.t, "gate", "A's shuts once its coil's hold runs out");
  assert.equal(cellAt(w, 16, 7)!.t, "gate");
  assert.ok(has(w, "The lightning coil falls quiet and its gate slams shut"));
  assert.equal(`${cellHash(w)} ${w.rng} ${cellHash(back)}`, "390cf655 12 82311aad"); // the same in the C# core
});

test("linking a gate again moves its link", () => {
  const w = createWorld(10, 3, 1);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 1, 1)!.t = "coil";
  cellAt(w, 3, 1)!.t = "coil";
  cellAt(w, 6, 1)!.t = "gate";
  linkGate(w, 6, 1, 1, 1);
  linkGate(w, 6, 1, 3, 1);
  assert.equal(w.gateLinks.length, 1);
  cellAt(w, 1, 1)!.charge = 2;
  step(w);
  assert.equal(cellAt(w, 6, 1)!.t, "gate", "the old coil no longer opens it");
  cellAt(w, 3, 1)!.charge = 2;
  step(w);
  assert.equal(cellAt(w, 6, 1)!.t, "gate_open");
});
