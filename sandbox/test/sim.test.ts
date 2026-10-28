import { test } from "node:test";
import assert from "node:assert/strict";
import { RULES, validateRules } from "../src/rules.ts";
import { step } from "../src/sim.ts";
import { castAt, castSpell, carry, paint, placeCritter, previewCells } from "../src/actions.ts";
import { push, spawn, walk } from "../src/entities.ts";
import { applyElement } from "../src/react.ts";
import { SCENARIOS, goalState, loadScenario } from "../src/scenarios.ts";
import { type World, NO_LAND, cellAt, setLand, createWorld, entityAt, hashWorld, playerOf } from "../src/world.ts";

const run = (w: World, ticks: number) => { for (let i = 0; i < ticks; i++) step(w); };
const direct = { power: 2, direct: true };

/** A small dirt room with bedrock walls, for isolated tests. */
function room(w = 12, h = 8): World {
  const world = createWorld(w, h, 7);
  for (let x = 0; x < w; x++) for (let y = 0; y < h; y++) {
    if (x === 0 || y === 0 || x === w - 1 || y === h - 1) cellAt(world, x, y)!.t = "bedrock";
  }
  return world;
}

test("rule tables are internally consistent", () => {
  assert.deepEqual(validateRules(RULES), []);
});

test("every scenario loads", () => {
  for (const s of SCENARIOS) assert.doesNotThrow(() => loadScenario(s), s.id);
});

test("fire on oil blazes; water can't douse it and splashes it outward", () => {
  const w = room();
  cellAt(w, 5, 4)!.cover = "oil";
  applyElement(w, 5, 4, "fire", direct);
  assert.equal(cellAt(w, 5, 4)!.fire, 3);
  applyElement(w, 5, 4, "water", direct);
  assert.equal(cellAt(w, 5, 4)!.fire, 3, "still burning");
  const lit = [[4, 4], [6, 4], [5, 3], [5, 5]].filter(([x, y]) => cellAt(w, x!, y!)!.fire > 0);
  assert.equal(lit.length, 2, "burning oil splashed onto two neighbours");
});

test("water on a grass fire puts it out in a puff of steam", () => {
  const w = room();
  paint(w, "grass", 5, 4);
  applyElement(w, 5, 4, "fire", direct);
  assert.ok(cellAt(w, 5, 4)!.fire > 0);
  applyElement(w, 5, 4, "water", direct);
  assert.equal(cellAt(w, 5, 4)!.fire, 0);
  assert.equal(cellAt(w, 5, 4)!.gas, "steam");
});

test("Ray of Frost freezes a pond into walkable, insulating ice", () => {
  const w = room();
  for (let x = 3; x <= 7; x++) Object.assign(cellAt(w, x, 4)!, { t: "pit", water: 4 });
  castSpell(w, "ray-of-frost", 3, 4, 1);
  for (let x = 3; x <= 7; x++) assert.ok(cellAt(w, x, 4)!.frozen, `x=${x} frozen`);
  applyElement(w, 5, 4, "lightning", direct);
  assert.equal(cellAt(w, 6, 4)!.charge, 0, "ice doesn't conduct");
});

test("fire melts ice, then boils the water", () => {
  const w = room();
  Object.assign(cellAt(w, 5, 4)!, { t: "pit", water: 4, frozen: true });
  applyElement(w, 5, 4, "fire", direct);
  assert.equal(cellAt(w, 5, 4)!.frozen, false);
  applyElement(w, 5, 4, "fire", direct);
  assert.equal(cellAt(w, 5, 4)!.water, 3);
  assert.equal(cellAt(w, 5, 4)!.gas, "steam");
});

test("lightning runs through water into metal and wakes a coil, which opens the gate", () => {
  const w = room(14, 6);
  Object.assign(cellAt(w, 2, 2)!, { t: "pit", water: 4 });
  for (let x = 3; x <= 8; x++) cellAt(w, x, 2)!.t = "metal";
  cellAt(w, 9, 2)!.t = "coil";
  cellAt(w, 12, 3)!.t = "gate";
  castSpell(w, "shocking-grasp", 2, 2, 1);
  assert.equal(cellAt(w, 9, 2)!.charge, 3);
  step(w);
  assert.equal(cellAt(w, 12, 3)!.t, "gate_open");
});

test("a wet goblin takes double lightning damage", () => {
  const w = room();
  const dry = spawn(w, "goblin", 3, 3)!;
  const wet = spawn(w, "goblin", 6, 3)!;
  wet.status.wet = 60;
  Object.assign(cellAt(w, 3, 3)!, { t: "metal" });
  Object.assign(cellAt(w, 6, 3)!, { t: "metal" });
  applyElement(w, 3, 3, "lightning", direct);
  applyElement(w, 6, 3, "lightning", direct);
  step(w);
  assert.equal(10 - dry.hp, RULES.sim.zapDamage);
  assert.equal(10 - wet.hp, RULES.sim.zapDamage * 2);
});

test("steam engine: frog fills the boiler, newt heats it, the gate opens", () => {
  const w = room(14, 8);
  cellAt(w, 6, 3)!.t = "boiler";
  cellAt(w, 12, 5)!.t = "gate";
  placeCritter(w, "puddle-frog", 5, 3, 1);
  placeCritter(w, "ember-newt", 6, 3, 1);
  run(w, 200);
  assert.equal(cellAt(w, 12, 5)!.t, "gate_open");
  assert.equal(w.openedBy, "Steam boiler");
});

test("Gust pushes a goblin 2 squares; a boulder doesn't budge until it levitates", () => {
  const w = room(16, 6);
  const gob = spawn(w, "goblin", 3, 2)!;
  const rock = spawn(w, "boulder", 3, 4)!;
  castSpell(w, "gust-of-wind", 2, 2, 1);
  assert.equal(gob.x, 5);
  castSpell(w, "gust-of-wind", 2, 4, 1);
  assert.equal(rock.x, 3);
  castSpell(w, "levitate", 3, 4, 1);
  castSpell(w, "gust-of-wind", 2, 4, 1);
  assert.equal(rock.x, 6, "weightless: force 2 - mass 0 + 1 = 3 squares");
});

test("a shove onto ice turns into a slide", () => {
  const w = room(16, 6);
  for (let x = 4; x <= 9; x++) Object.assign(cellAt(w, x, 2)!, { t: "pit", water: 4, frozen: true });
  const gob = spawn(w, "goblin", 3, 2)!;
  castSpell(w, "gust-of-wind", 2, 2, 1);
  assert.equal(gob.x, 8, "2 squares of push + 3 of slide");
});

test("Thunderwave knocks the boulder onto the ice, and it slides onto the plate", () => {
  const w = loadScenario(SCENARIOS.find((s) => s.id === "steam-gate")!);
  castSpell(w, "ray-of-frost", 16, 16, 1);
  const rock = entityAt(w, 15, 16)!;
  castSpell(w, "thunderwave", 14, 16, 1);
  assert.deepEqual([rock.x, rock.y], [19, 16]);
  step(w);
  assert.equal(cellAt(w, 26, 9)!.t, "gate_open");
  assert.equal(goalState(w, SCENARIOS[0]!)!.done, true);
});

test("a burning bridge collapses and the goblin on it falls", () => {
  const w = room(10, 6);
  cellAt(w, 4, 2)!.t = "wood";
  const gob = spawn(w, "goblin", 4, 2)!;
  applyElement(w, 4, 2, "fire", direct);
  run(w, 40);
  assert.equal(cellAt(w, 4, 2)!.t, "pit");
  assert.ok(gob.hp < 10, "burned and fell");
});

test("an ignited oil barrel explodes and spreads burning oil", () => {
  const w = room();
  const barrel = spawn(w, "barrel", 5, 4)!;
  castSpell(w, "fire-bolt", 5, 4, 0);
  run(w, 12);
  assert.ok(!w.entities.includes(barrel));
  assert.ok(cellAt(w, 6, 4)!.fire > 0 || cellAt(w, 6, 4)!.cover === "ash");
});

test("the mole breaks stone, fills pits and raises pillars", () => {
  const w = room();
  cellAt(w, 3, 3)!.t = "stone";
  cellAt(w, 5, 3)!.t = "pit";
  placeCritter(w, "burrow-mole", 3, 3, 0);
  placeCritter(w, "burrow-mole", 5, 3, 0);
  placeCritter(w, "burrow-mole", 7, 3, 0);
  assert.equal(cellAt(w, 3, 3)!.t, "rubble");
  assert.equal(cellAt(w, 5, 3)!.t, "dirt");
  assert.equal(cellAt(w, 7, 3)!.t, "pillar");
});

test("water poured into a channel turns the wheel", () => {
  const w = room(16, 5);
  for (let x = 2; x <= 13; x++) cellAt(w, x, 2)!.t = "pit";
  cellAt(w, 9, 2)!.t = "wheel";
  cellAt(w, 14, 3)!.t = "gate";
  castSpell(w, "create-water", 3, 2, 0);
  let opened = false;
  for (let i = 0; i < 80 && !opened; i++) {
    step(w);
    opened = cellAt(w, 14, 3)!.t === "gate_open";
  }
  assert.ok(opened);
});

test("Mage Hand lifts a crate but not a boulder (unless it's levitating)", () => {
  const w = room();
  spawn(w, "crate", 2, 2);
  spawn(w, "boulder", 2, 4);
  assert.equal(carry(w, 2, 2, 5, 2), null);
  assert.match(carry(w, 2, 4, 5, 4)!, /too heavy/);
  castSpell(w, "levitate", 2, 4, 0);
  assert.equal(carry(w, 2, 4, 5, 4), null);
});

test("a shove into a wall hurts", () => {
  const w = room();
  const gob = spawn(w, "goblin", 9, 3)!;
  push(w, gob, 1, 3, true);
  assert.equal(gob.x, 10);
  assert.equal(gob.hp, 10 - RULES.sim.push.collisionDamage);
});

test("the simulation is deterministic for a given seed", () => {
  const a = loadScenario(SCENARIOS[1]!, 42), b = loadScenario(SCENARIOS[1]!, 42);
  for (const w of [a, b]) {
    castSpell(w, "fire-bolt", 24, 7, 0);
    castSpell(w, "fire-bolt", 15, 8, 0);
    run(w, 150);
  }
  assert.equal(hashWorld(a), hashWorld(b));
});

test("every route through the Steam Gate works", async (t) => {
  const s = SCENARIOS.find((sc) => sc.id === "steam-gate")!;
  const routes: [string, (w: World) => void, string][] = [
    ["Gust into the windmill", (w) => castSpell(w, "gust-of-wind", 14, 14, 1), "Windmill"],
    ["Shocking Grasp in the pond", (w) => castSpell(w, "shocking-grasp", 5, 5, 1), "Lightning coil"],
    ["Spark eel in the pond", (w) => placeCritter(w, "spark-eel", 5, 5, 1), "Lightning coil"],
    ["Create Water in the channel", (w) => castSpell(w, "create-water", 9, 12, 1), "Water wheel"],
    ["Puddle frog at the channel head", (w) => placeCritter(w, "puddle-frog", 8, 12, 1), "Water wheel"],
    ["Fill the boiler, newt on top", (w) => { castSpell(w, "create-water", 23, 3, 1); placeCritter(w, "ember-newt", 23, 3, 1); }, "Steam boiler"],
    ["Fill the boiler, burn the brambles", (w) => { castSpell(w, "create-water", 23, 3, 1); castSpell(w, "fire-bolt", 22, 2, 1); }, "Steam boiler"],
    ["Freeze the channel, Thunderwave the boulder", (w) => { castSpell(w, "ray-of-frost", 16, 16, 1); castSpell(w, "thunderwave", 14, 16, 1); }, "Pressure plate"],
    ["Mole through the soft wall", (w) => placeCritter(w, "burrow-mole", 26, 16, 1), "tunnel"],
  ];
  for (const [name, act, via] of routes) {
    await t.test(name, () => {
      const w = loadScenario(s, 3);
      act(w);
      for (let i = 0; i < 400 && !goalState(w, s)!.done; i++) step(w);
      assert.ok(goalState(w, s)!.done, "goal reached");
      assert.equal(via === "tunnel" ? "tunnel" : w.openedBy, via);
    });
  }
  await t.test("nothing opens it on its own", () => {
    const w = loadScenario(s, 3);
    run(w, 600);
    assert.equal(goalState(w, s)!.done, false);
  });
});

test("every scenario starts with the wizard", () => {
  for (const s of SCENARIOS) assert.ok(playerOf(loadScenario(s)), s.id);
});

test("walking: blocked by walls, refuses a dry pit, slides on ice", () => {
  const w = room(14, 6);
  const me = spawn(w, "wizard", 2, 1)!;
  assert.match(walk(w, me, 0)!, /in the way/, "bedrock above");
  cellAt(w, 3, 1)!.t = "pit";
  assert.match(walk(w, me, 1)!, /pit/);
  assert.equal(me.x, 2);
  cellAt(w, 3, 1)!.t = "dirt";
  for (let x = 4; x <= 7; x++) Object.assign(cellAt(w, x, 1)!, { t: "pit", water: 4, frozen: true });
  assert.equal(walk(w, me, 1), null);
  assert.equal(walk(w, me, 1), null);
  assert.equal(me.x, 7, "one step onto the ice, then a 3-square slide");
});

test("beams start at the wizard and point at the click", () => {
  const w = room(14, 8);
  spawn(w, "wizard", 2, 4);
  for (let x = 4; x <= 8; x++) Object.assign(cellAt(w, x, 4)!, { t: "pit", water: 4 });
  assert.deepEqual(previewCells(w, "ray-of-frost", 11, 5).map(([x]) => x), [3, 4, 5, 6, 7]);
  assert.equal(castAt(w, "ray-of-frost", 11, 5), null);
  assert.ok(cellAt(w, 4, 4)!.frozen && cellAt(w, 7, 4)!.frozen);
  assert.equal(cellAt(w, 8, 4)!.frozen, false, "the beam is 5 squares long");
});

test("Thunderwave goes off around the wizard and spares the caster", () => {
  const w = room(14, 8);
  const me = spawn(w, "wizard", 5, 4)!;
  const gob = spawn(w, "goblin", 6, 4)!;
  castAt(w, "thunderwave", 9, 4);
  assert.equal(me.hp, 12);
  assert.ok(gob.x > 6, "goblin thrown away from the wizard");
});

test("Misty Step: through gate bars yes, through a wall no, and only 60 ft", () => {
  const w = room(30, 6);
  for (let y = 1; y <= 4; y++) cellAt(w, 8, y)!.t = "stone";
  cellAt(w, 8, 2)!.t = "gate";
  const me = spawn(w, "wizard", 6, 3)!;
  assert.match(castAt(w, "misty-step", 10, 3)!, /can't see/);
  me.y = 2;
  assert.equal(castAt(w, "misty-step", 10, 2), null);
  assert.equal(me.x, 10);
  assert.equal(castAt(w, "misty-step", 22, 2), null, "12 squares (60 ft) is in range");
  assert.equal(me.x, 22);
  assert.match(castAt(w, "misty-step", 9, 2)!, /Too far/, "13 squares isn't");
});

test("Misty Step between the Steam Gate's bars counts as getting through", () => {
  const s = SCENARIOS.find((sc) => sc.id === "steam-gate")!;
  const w = loadScenario(s, 3);
  const me = playerOf(w)!;
  Object.assign(me, { x: 25, y: 9 });
  assert.equal(castAt(w, "misty-step", 28, 9), null);
  assert.equal(goalState(w, s)!.via, "blink");
});

// Mirrors ChemistryTests.WaterRunsDownTheLandsSlope (playtest #31).
test("water runs down the land's slope", () => {
  const spread = (land: number[] | null) => {
    const w = createWorld(16, 9, 1);
    for (const c of w.cells) c.t = "grass";
    setLand(w, land);
    cellAt(w, 8, 4)!.water = 12;
    run(w, 300);
    let west = 0, east = 0;
    w.cells.forEach((c, i) => { if (i % 16 < 8) west += c.water; if (i % 16 > 8) east += c.water; });
    return { west, east };
  };
  const slope = Array.from({ length: 16 * 9 }, (_, k) => (k % 16) * 3);
  const onSlope = spread(slope);
  assert.equal(onSlope.east, 0, "none runs uphill");
  assert.ok(onSlope.west >= 10, "it runs down the slope");
  const onFlat = spread(null);
  assert.ok(onFlat.east > 0, "on the flat it spreads both ways");
  assert.ok(onFlat.west < onSlope.west);
  const lake = slope.map((v, k) => (k % 16 >= 8 ? NO_LAND : v));
  assert.ok(spread(lake).east > 0, "inside a water body it spreads as on the flat");
});
