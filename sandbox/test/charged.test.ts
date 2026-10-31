// Holding the last element to charge a cast by elements (docs/SPELL_COMBOS.md).
// The goldens are shared with the C# port's ChargedCastTests
// (unity/Assets/Prototype/Chemistry/Tests/ChargedCastTests.cs).
import { test } from "node:test";
import assert from "node:assert/strict";
import { RULES } from "../src/rules.ts";
import { step } from "../src/sim.ts";
import { castSpell, MAX_CHARGE } from "../src/actions.ts";
import { applyCombo, castHash, castState, chargeable, comboById, heldCharge, maxChargeOf, moveFactorOf, spellAt } from "../src/combos.ts";
import { type World, cellAt, createWorld, deserializeWorld, fnv1a, serializeWorld } from "../src/world.ts";

const run = (w: World, ticks: number) => { for (let i = 0; i < ticks; i++) step(w); };
const hold = (w: World, element: string) => applyCombo(w, { player: "ann", kind: "load", element, hold: true });
const release = (w: World) => applyCombo(w, { player: "ann", kind: "release" });

/** World.CellHash in the C# port. */
const cellHash = (w: World) => fnv1a(w.cells.map((c) =>
  `${c.t},${c.water},${c.frozen ? 1 : 0},${c.cover ?? ""},${c.fire},${c.fuel},${c.gas ?? ""},${c.gasAmt},${c.charge}${c.h ? `,h${c.h}` : ""}${c.life ? `,l${c.life}` : ""}${c.rust ? `,r${c.rust}` : ""}${c.heat ? `,t${c.heat}` : ""}${c.cracked ? `,c${c.cracked}` : ""};`).join(""));

/** Steps until a cast goes off, and says how many ticks that took. */
function untilFired(w: World): number {
  for (let t = 1; t <= 60; t++) {
    step(w);
    if (w.combosFired.length > 0) return t;
  }
  throw new Error("nothing went off");
}

/** Holds a combo's elements until the meter has reached this charge, then lets go. */
function charged(w: World, combo: string, charge: number) {
  for (const element of combo.split("+")) hold(w, element);
  while (heldCharge(w.casts.ann) < charge) step(w);
  release(w);
  untilFired(w);
  return w.combosFired[0]!;
}

test("the held variant is only at full charge", () => {
  const wall = comboById("earth+earth")!;
  assert.equal(spellAt(wall, 2, 3), "wall-of-stone");
  assert.equal(spellAt(wall, 3, 3), "stone-ramp");
  assert.equal(spellAt(wall, 1, 1), "wall-of-stone", "a spell that can't charge has no held form");
  assert.equal(spellAt(comboById("water+air")!, 2, 3), "frost");
  assert.equal(spellAt(comboById("water+air")!, 3, 3), "sleet-storm");
  assert.equal(spellAt(comboById("fire+fire")!, 3, 3), "fireball", "and most combos have none");
});

for (const [combo, charge] of [["fire", 1], ["fire", 2], ["fire", 3], ["fire+fire", 1], ["fire+fire", 3]] as const) {
  test(`each level of charge adds cast time: ${combo} at ${charge}`, () => {
    const w = createWorld(8, 8, 7);
    for (const element of combo.split("+")) hold(w, element);
    let ticks = 0;
    while (heldCharge(w.casts.ann) < charge) {
      step(w);
      ticks++;
    }
    release(w);
    ticks += untilFired(w);
    assert.equal(ticks, comboById(combo)!.castTicks + (charge - 1) * RULES.combos.chargeTicks);
    assert.equal(w.combosFired[0]!.charge, charge);
  });
}

test("holding past the full charge gives no more", () => {
  const w = createWorld(8, 8, 7);
  hold(w, "fire");
  run(w, 200);
  assert.equal(heldCharge(w.casts.ann), MAX_CHARGE);
  release(w);
  assert.equal(untilFired(w), 1);
  assert.equal(w.combosFired[0]!.charge, MAX_CHARGE);
});

test("every combo on a real spell charges to the max", () => {
  assert.equal(maxChargeOf(comboById("fire")), MAX_CHARGE);
  assert.equal(chargeable(undefined), false);
  assert.equal(maxChargeOf(undefined), 1);
});

test("a tap goes off when a tap always did", () => {
  const plain = createWorld(8, 8, 7);
  applyCombo(plain, { player: "ann", kind: "load", element: "fire" });
  const expected = untilFired(plain);
  const tapped = createWorld(8, 8, 7);
  hold(tapped, "fire");
  step(tapped);
  assert.ok(release(tapped));
  assert.equal(untilFired(tapped), expected - 1);
  assert.equal(tapped.combosFired[0]!.charge, 1);
  assert.equal(castState(tapped), castState(plain), "a tap leaves nothing behind");
});

test("a held cast waits at the end of its meter for the release", () => {
  const w = createWorld(8, 8, 7);
  hold(w, "fire");
  run(w, 40);
  assert.equal(w.combosFired.length, 0);
  const c = w.casts.ann!;
  assert.deepEqual([c.meter, c.holding], [1, true]);
  assert.equal(moveFactorOf(w, "ann"), RULES.combos.moveFactor, "still slowed while held");
  release(w);
  step(w);
  assert.equal(w.combosFired[0]!.charge, MAX_CHARGE);
  assert.equal(w.casts.ann!.charge, 1, "and it's spent");
});

test("released before a level, it is a tap", () => {
  const w = createWorld(8, 8, 7);
  hold(w, "fire");
  step(w);
  release(w);
  assert.equal(untilFired(w), comboById("fire")!.castTicks - 1);
  assert.equal(w.combosFired[0]!.charge, 1);
});

test("a release is only taken for a hold", () => {
  const w = createWorld(8, 8, 7);
  assert.equal(release(w), false, "nothing held");
  hold(w, "fire");
  assert.ok(release(w));
  assert.equal(release(w), false, "let go once");
});

test("the second element takes the hold over, and cancel drops it", () => {
  const w = createWorld(8, 8, 7);
  hold(w, "earth");
  hold(w, "water");
  run(w, 20);
  assert.equal(w.combosFired.length, 0, "still held");
  assert.equal(w.casts.ann!.second, "water");
  assert.ok(applyCombo(w, { player: "ann", kind: "cancel" }));
  assert.equal(w.casts.ann!.holding, false);
  assert.equal(release(w), false, "a cancelled cast is gone");
  run(w, 20);
  assert.equal(w.combosFired.length, 0);
});

test("an element the core refuses takes no hold", () => {
  const w = createWorld(8, 8, 7);
  applyCombo(w, { player: "ann", kind: "load", element: "fire" });
  run(w, RULES.combos.window);
  assert.equal(hold(w, "water"), false);
  assert.equal(w.casts.ann!.holding, false);
});

test("a held cast goes off as a tap after a snapshot; a charge let go is kept", () => {
  const w = createWorld(8, 8, 7);
  hold(w, "fire");
  step(w);
  const back = deserializeWorld(serializeWorld(w));
  assert.equal(back.casts.ann!.holding, false, "no button is down on a loaded game");
  assert.equal(untilFired(back), comboById("fire")!.castTicks - 1);
  run(w, 20);
  release(w);
  const kept = deserializeWorld(serializeWorld(w));
  assert.equal(castState(kept), castState(w));
  assert.equal(castHash(kept), castHash(w));
});

test("the charge is in the checksum", () => {
  const a = createWorld(8, 8, 7), b = createWorld(8, 8, 7);
  const plain = castHash(a);
  hold(a, "fire");
  hold(b, "fire");
  assert.notEqual(castHash(a), plain);
  assert.equal(castHash(b), castHash(a));
  run(a, 2);
  release(a);
  run(b, 9);
  release(b);
  assert.notEqual(castHash(a), castHash(b));
});

test("held to the max, Wall of Stone is the ramp", () => {
  const f = charged(createWorld(24, 12, 7), "earth+earth", MAX_CHARGE);
  assert.deepEqual([f.spell, f.shape, f.name, f.charge], ["stone-ramp", "line", "Wall of Stone", 3]);
  assert.equal(charged(createWorld(24, 12, 7), "earth+earth", MAX_CHARGE - 1).spell, "wall-of-stone");
});

test("held to the max, Raise Earth raises it wall high", () => {
  const w = createWorld(12, 12, 7);
  const f = charged(w, "earth", MAX_CHARGE);
  assert.equal(f.spell, "mold-earth-heap");
  castSpell(w, f.spell, 5, 5, 1, undefined, f.charge);
  assert.equal(cellAt(w, 5, 5)!.h, 3, "as high as a wall");
});

// Plant Growth by elements on burnt ground and meadow: what a tap can't reach.
function saplings(charge: number): number {
  const w = createWorld(19, 19, 7);
  for (let x = 0; x < 19; x++) for (let y = 0; y < 19; y++) {
    const c = cellAt(w, x, y)!;
    if (x === 0 || y === 0 || x === 18 || y === 18) c.t = "bedrock";
    else {
      c.t = y < 9 ? "dirt" : "grass";
      c.cover = y < 9 ? "ash" : null;
    }
  }
  const f = charged(w, "earth+water", charge);
  castSpell(w, f.spell, 9, 9, 1, undefined, f.charge);
  return w.cells.filter((c) => c.t === "sapling").length;
}

test("Plant Growth at full charge seeds saplings that a tap does not", () => {
  assert.equal(saplings(1), 0);
  assert.ok(saplings(MAX_CHARGE) > 5);
});

// The goldens: the same steps as ChargedCastTests.cs give the same text and hashes.

test("golden: charged Plant Growth", () => {
  const w = createWorld(16, 12, 7);
  for (let x = 0; x < 16; x++) cellAt(w, x, 6)!.t = "grass";
  hold(w, "earth");
  run(w, 2);
  hold(w, "water");
  run(w, 14);
  assert.equal(castState(w), "ann:earth:water:1:15:0:0:-1::hold:c1");
  assert.equal(heldCharge(w.casts.ann), 3);
  release(w);
  assert.equal(castState(w), "ann:earth:water:1:15:0:0:-1::c3");
  assert.equal(untilFired(w), 1);
  const f = w.combosFired[0]!;
  assert.deepEqual([f.spell, f.charge], ["plant-growth", 3]);
  assert.equal(castState(w), "ann:-:-:0:0:0:0:-1:earth+water");
  castSpell(w, f.spell, 8, 6, 1, undefined, f.charge);
  run(w, 6);
  assert.equal(`${castHash(w)} ${cellHash(w)} ${w.rng}`, "ff240b66 f5d5e3aa 4167084909");
});

test("golden: held Stone Ramp", () => {
  const w = createWorld(24, 12, 7);
  const f = charged(w, "earth+earth", 3);
  assert.equal(f.spell, "stone-ramp");
  castSpell(w, f.spell, 4, 6, 1, undefined, f.charge);
  run(w, 2);
  assert.equal(`${castHash(w)} ${cellHash(w)} ${w.rng}`, "cdeed8e1 9aa186c6 7");
});

// Frost held to full charge glazes the field Sleet Storm does (terraform.test.ts's sleetField ground).
test("golden: Frost held is Sleet Storm", () => {
  const w = createWorld(20, 20, 7);
  for (let y = 3; y <= 6; y++) cellAt(w, 5, y)!.t = "grass";
  cellAt(w, 7, 8)!.water = 8;
  cellAt(w, 9, 9)!.fire = 3;
  cellAt(w, 9, 9)!.fuel = 4;
  const f = charged(w, "water+air", 3);
  assert.deepEqual([f.spell, f.name], ["sleet-storm", "Frost"]);
  castSpell(w, f.spell, 8, 8, 1, undefined, f.charge);
  const glazed = w.cells.filter((c) => c.cover === "frost").length;
  assert.ok(glazed > 200, `${glazed} cells glazed, wider than a tap's`);
  assert.equal(cellAt(w, 7, 8)!.frozen, true);
  assert.equal(cellAt(w, 9, 9)!.fire, 0);
  assert.equal(charged(createWorld(20, 20, 7), "water+air", MAX_CHARGE - 1).spell, "frost", "a part charge stays Frost");
  run(w, 4);
  assert.equal(`${castHash(w)} ${cellHash(w)} ${w.rng}`, "bdd3d4c6 20520162 1511859835");
});

test("golden: Raise Earth held", () => {
  const w = createWorld(12, 12, 7);
  const f = charged(w, "earth", 3);
  castSpell(w, f.spell, 5, 5, 1, undefined, f.charge);
  run(w, 2);
  assert.equal(`${castHash(w)} ${cellHash(w)} ${w.rng}`, "edf99516 a2a683ad 7");
});
