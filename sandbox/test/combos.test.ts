// Element combos (docs/SPELL_COMBOS.md). The goldens are shared with the C#
// port's ComboTests (unity/Assets/Prototype/Chemistry/Tests/ComboTests.cs).
import { test } from "node:test";
import assert from "node:assert/strict";
import { BASE_RULES, RULES, composeRules, installRules, validateRules, type CombosDef, type ModPatch } from "../src/rules.ts";
import { step } from "../src/sim.ts";
import { castSpell } from "../src/actions.ts";
import { applyCombo, castHash, castState, comboById, knowsCombo, moveFactorOf, selectedDpad } from "../src/combos.ts";
import { type World, cellAt, createWorld, deserializeWorld, fnv1a, hashWorld, serializeWorld } from "../src/world.ts";

const ELEMENTS = ["fire", "water", "earth", "air"];
const run = (w: World, ticks: number) => { for (let i = 0; i < ticks; i++) step(w); };
const load = (w: World, player: string, element: string) => applyCombo(w, { player, kind: "load", element });
const cancel = (w: World, player: string) => applyCombo(w, { player, kind: "cancel" });
const cycle = (w: World, player: string, by: number) => applyCombo(w, { player, kind: "cycle", step: by });
const castSelected = (w: World, player: string) => applyCombo(w, { player, kind: "castSelected" });

/** World.CellHash in the C# port. */
const cellHash = (w: World) => fnv1a(w.cells.map((c) =>
  `${c.t},${c.water},${c.frozen ? 1 : 0},${c.cover ?? ""},${c.fire},${c.fuel},${c.gas ?? ""},${c.gasAmt},${c.charge}${c.h ? `,h${c.h}` : ""}${c.life ? `,l${c.life}` : ""}${c.rust ? `,r${c.rust}` : ""}${c.heat ? `,t${c.heat}` : ""};`).join(""));

/** Steps until a cast goes off, and says how many ticks that took. */
function untilFired(w: World): number {
  for (let t = 1; t <= 60; t++) {
    step(w);
    if (w.combosFired.length > 0) return t;
  }
  throw new Error("nothing went off");
}

function patched(combos: Record<string, unknown>): string[] {
  return validateRules(composeRules([{ combos } as ModPatch]));
}

test("the table has 4 singles and 16 ordered pairs, all on real spells", () => {
  assert.deepEqual(validateRules(RULES), []);
  const c = BASE_RULES.combos;
  assert.deepEqual(c.elements, ELEMENTS);
  assert.deepEqual(Object.keys(c.singles), ELEMENTS);
  assert.deepEqual(Object.keys(c.pairs), ELEMENTS);
  for (const e of ELEMENTS) assert.deepEqual(Object.keys(c.pairs[e]!), ELEMENTS);
  assert.equal(c.moveFactor, 0.6);
  assert.equal(comboById("fire+air")!.name, "Flame Wave");
  assert.equal(comboById("air+fire")!.name, "Lightning");
  assert.equal(comboById("fire+fire")!.held, "meteor");
});

test("a combo with no todo casts its real spell, in that spell's shape", () => {
  const c = BASE_RULES.combos;
  const all = [...Object.values(c.singles), ...Object.values(c.pairs).flatMap((p) => Object.values(p))];
  assert.equal(all.length, 20);
  for (const d of all) assert.equal(d.shape, BASE_RULES.spells.find((s) => s.id === d.spell)!.shape, d.name);
<<<<<<< HEAD
  assert.equal(all.filter((d) => d.todo).length, 7);
  assert.equal(comboById("fire+water")!.spell, "fog-cloud");
  assert.ok(comboById("fire+water")!.todo);
=======
  assert.equal(all.filter((d) => d.todo).length, 8);
  assert.equal(comboById("fire+water")!.spell, "scalding-steam");
  assert.ok(!comboById("fire+water")!.todo, "a real spell now");
  assert.equal(comboById("water+water")!.spell, "create-water");
  assert.ok(comboById("water+water")!.todo, "still a stand-in");
>>>>>>> origin/dev
});

test("the d-pad lists the spells with no element", () => {
  assert.deepEqual(BASE_RULES.combos.dpad.map((d) => d.name), ["Hasten Time", "Shapeshift", "Light", "Control Weather"]);
  assert.deepEqual(BASE_RULES.combos.dpad.map((d) => d.spell), ["hasten-time", "shapeshift", undefined, "control-weather"]);
});

test("one element goes off when its meter drains, and the player walks slower meanwhile", () => {
  const w = createWorld(8, 8);
  assert.equal(moveFactorOf(w, "ann"), 1);
  assert.ok(load(w, "ann", "fire"));
  assert.equal(moveFactorOf(w, "ann"), 0.6);
  run(w, 5);
  assert.equal(w.casts.ann!.meter, 1);
  assert.equal(w.combosFired.length, 0);
  step(w);
  assert.deepEqual(w.combosFired, [{ player: "ann", combo: "fire", name: "Fire Bolt", spell: "fire-bolt", shape: "point", discovered: true }]);
  assert.equal(moveFactorOf(w, "ann"), 1);
  assert.equal(w.casts.ann!.first, null);
  step(w);
  assert.equal(w.combosFired.length, 0, "only for the tick it went off");
});

test("a second element inside the window makes it the ordered pair, on the pair's meter", () => {
  const w = createWorld(8, 8);
  load(w, "ann", "air");
  run(w, 3);
  assert.ok(load(w, "ann", "fire"));
  assert.equal(w.casts.ann!.second, "fire");
  assert.equal(w.casts.ann!.meter, 9);
  assert.equal(w.casts.ann!.total, 9);
  assert.equal(untilFired(w), 9);
  assert.equal(w.combosFired[0]!.combo, "air+fire");
  assert.equal(w.combosFired[0]!.spell, "shocking-grasp");
});

test("the window shuts, a third element is refused, and so is an unknown one", () => {
  const w = createWorld(8, 8);
  assert.equal(load(w, "ann", "cold"), false);
  load(w, "ann", "fire");
  run(w, 4);
  assert.equal(load(w, "ann", "air"), false, "the window was 4 ticks");
  assert.equal(untilFired(w), 2);
  assert.equal(w.combosFired[0]!.combo, "fire");
  load(w, "ann", "fire");
  assert.ok(load(w, "ann", "air"));
  assert.equal(load(w, "ann", "water"), false);
  assert.equal(w.casts.ann!.second, "air");
});

test("cancel drops the elements and nothing casts", () => {
  const w = createWorld(8, 8);
  assert.equal(cancel(w, "ann"), false, "nothing to drop");
  load(w, "ann", "earth");
  load(w, "ann", "earth");
  run(w, 3);
  assert.ok(cancel(w, "ann"));
  assert.equal(w.casts.ann!.first, null);
  assert.equal(moveFactorOf(w, "ann"), 1);
  run(w, 20);
  assert.equal(w.combosFired.length, 0);
  assert.equal(knowsCombo(w, "ann", "earth+earth"), false);
});

test("a combo is unknown until cast, per player, and only the first cast discovers it", () => {
  const w = createWorld(8, 8);
  assert.equal(knowsCombo(w, "ann", "fire"), false);
  load(w, "ann", "fire");
  load(w, "bob", "water");
  run(w, 6);
  assert.deepEqual(w.combosFired.map((f) => [f.player, f.combo, f.discovered]), [["ann", "fire", true], ["bob", "water", true]]);
  assert.ok(knowsCombo(w, "ann", "fire"));
  assert.equal(knowsCombo(w, "bob", "fire"), false);
  load(w, "ann", "fire");
  run(w, 6);
  assert.equal(w.combosFired[0]!.discovered, false);
  assert.deepEqual(w.casts.ann!.known, ["fire"]);
});

test("the d-pad cycles both ways, casts through the meter, and refuses a slot with no spell", () => {
  const w = createWorld(8, 8);
  assert.equal(selectedDpad(w, "ann")!.name, "Hasten Time");
  cycle(w, "ann", -1);
  assert.equal(selectedDpad(w, "ann")!.name, "Control Weather");
  cycle(w, "ann", 1);
  cycle(w, "ann", 1);
  assert.equal(w.casts.ann!.slot, 1);
  cycle(w, "ann", 1);
  assert.equal(selectedDpad(w, "ann")!.name, "Light");
  assert.equal(castSelected(w, "ann"), false, "Light has no spell yet");
  cycle(w, "ann", -1);
  assert.ok(castSelected(w, "ann"));
  assert.equal(moveFactorOf(w, "ann"), 0.6);
  assert.equal(load(w, "ann", "fire"), false, "no element during a d-pad cast");
  assert.equal(castSelected(w, "ann"), false);
  assert.equal(untilFired(w), 12);
  assert.deepEqual(w.combosFired, [{ player: "ann", combo: null, name: "Shapeshift", spell: "shapeshift", shape: "target", discovered: false }]);
  assert.deepEqual(w.casts.ann!.known, []);
});

test("cast state survives a snapshot and joins the world hash", () => {
  const w = createWorld(8, 8);
  const plain = hashWorld(w);
  load(w, "ann", "fire");
  assert.notEqual(hashWorld(w), plain);
  const back = deserializeWorld(serializeWorld(w));
  assert.equal(castState(back), castState(w));
  assert.equal(hashWorld(back), hashWorld(w));
  assert.equal(untilFired(back), 6);
});

test("the table's checks use the C# wording", () => {
  assert.deepEqual(patched({ window: 0, moveFactor: 2 }), ["combos: window must be a whole number from 1", "combos: moveFactor must be above 0 and at most 1"]);
  assert.deepEqual(patched({ window: 7 }), ["combo fire: castTicks must be at least the window", "combo water: castTicks must be at least the window", "combo earth: castTicks must be at least the window", "combo air: castTicks must be at least the window"]);
  assert.deepEqual(patched({ singles: { fire: { name: "", spell: "nope", shape: "blob", castTicks: 0, held: "nada" } } }), [
    "combo fire: needs a name",
    "combo fire: unknown spell nope",
    "combo fire: unknown shape blob",
    "combo fire: castTicks must be a whole number from 1",
    "combo fire: held spell nada unknown",
    "combo fire: castTicks must be at least the window",
  ]);
  assert.deepEqual(patched({ elements: ["fire", "void"], pairs: { fire: { void: { name: "V", spell: "fireball", shape: "burst", castTicks: 3 } } } }), [
    "combos: unknown element void",
    "combo void: missing",
    "combo void+fire: missing",
    "combo void+void: missing",
  ]);
  assert.deepEqual(patched({ dpad: [{ spell: "nope", castTicks: 0 }] }), ["dpad 0: needs a name", "dpad 0: unknown spell nope", "dpad 0: castTicks must be a whole number from 1"]);
});

test("a mod can change a combo, and a cast its mod took away doesn't go off", () => {
  const before = composeRules([]);
  const w = createWorld(8, 8);
  try {
    load(w, "ann", "fire");
    const r = composeRules([{ combos: { singles: { fire: { name: "Spark", spell: "shocking-grasp", shape: "point", castTicks: 6 } } } } as ModPatch]);
    assert.deepEqual(validateRules(r), []);
    assert.equal((BASE_RULES.combos as CombosDef).singles.fire!.name, "Fire Bolt", "the base rules are untouched");
    installRules(r);
    assert.equal(untilFired(w), 6);
    assert.equal(w.combosFired[0]!.name, "Spark");
    castSelected(w, "bob");
    installRules(composeRules([{ combos: { dpad: [] } } as ModPatch]));
    run(w, 12);
    assert.equal(w.casts.bob!.meter, 0);
    assert.equal(w.combosFired.length, 0);
  } finally {
    installRules(before);
  }
});

// The goldens: the same steps as ComboTests.cs give the same text and hashes.

test("golden: a combo cast", () => {
  const w = createWorld(16, 12, 7);
  for (let x = 0; x < 16; x++) cellAt(w, x, 6)!.t = "grass";
  load(w, "ann", "fire");
  run(w, 2);
  load(w, "ann", "fire");
  assert.equal(castState(w), "ann:fire:fire:12:12:0:0:-1:");
  assert.equal(untilFired(w), 12);
  assert.equal(castState(w), "ann:-:-:0:0:0:0:-1:fire+fire");
  const f = w.combosFired[0]!;
  castSpell(w, f.spell, 8, 6, 1);
  run(w, 6);
  assert.equal(`${castHash(w)} ${cellHash(w)} ${w.rng}`, "55751533 6184569 368494589");
});

test("golden: a cancel", () => {
  const w = createWorld(16, 12, 7);
  load(w, "ann", "water");
  step(w);
  load(w, "ann", "earth");
  run(w, 3);
  load(w, "bob", "air");
  assert.equal(castState(w), "ann:water:earth:6:9:0:0:-1:/bob:air:-:6:6:4:0:-1:");
  cancel(w, "ann");
  run(w, 20);
  assert.equal(castState(w), "ann:-:-:0:0:0:0:-1:/bob:-:-:0:0:0:0:-1:air");
  assert.equal(`${castHash(w)} ${cellHash(w)} ${w.rng}`, "2f3926dc 1c93d7c5 7");
});

test("golden: discovery", () => {
  const w = createWorld(16, 12, 7);
  load(w, "ann", "fire");
  load(w, "bob", "air");
  run(w, 6);
  load(w, "ann", "air");
  load(w, "ann", "fire");
  cycle(w, "bob", -1);
  castSelected(w, "bob");
  run(w, 9);
  load(w, "ann", "fire");
  run(w, 9);
  assert.equal(castState(w), "ann:-:-:0:0:0:0:-1:air+fire,fire/bob:-:-:0:0:0:3:-1:air");
  assert.equal(`${castHash(w)} ${cellHash(w)} ${w.rng}`, "f2b02f12 1c93d7c5 7");
});
