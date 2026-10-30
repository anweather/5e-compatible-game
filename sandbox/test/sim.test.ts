import { test } from "node:test";
import assert from "node:assert/strict";
import { RULES, type ModPatch, composeRules, installRules, validateRules } from "../src/rules.ts";
import { withoutSoaking } from "./soak.ts";
import { step } from "../src/sim.ts";
import { castAt, castSpell, carry, paint, placeCritter, previewCells } from "../src/actions.ts";
import { push, spawn, walk } from "../src/entities.ts";
import { applyElement, shatter } from "../src/react.ts";
import { reshape } from "../src/terraform.ts";
import { SCENARIOS, goalState, loadScenario } from "../src/scenarios.ts";
import { type World, NO_LAND, cellAt, setLand, createWorld, entityAt, hashWorld, isBlocking, playerOf } from "../src/world.ts";

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

// Mirrors ChemistryTests.FrostThenSpark: over 40 seeds, a goblin (wet or not) hit by Ray of Frost, then Shocking
// Grasp: how many are gone, and the sum of the worlds' rng states.
const frostThenSpark = (wet: boolean) => {
  let gone = 0, rng = 0;
  for (let seed = 1; seed <= 40; seed++) {
    const w = createWorld(20, 9, seed);
    for (const c of w.cells) c.t = "dirt";
    const e = spawn(w, "goblin", 8, 4)!;
    if (wet) e.status.wet = 60;
    castSpell(w, "ray-of-frost", 8, 4, 0);
    assert.ok(w.entities.includes(e), "the cold that froze it doesn't shatter it (status.shatter notBy)");
    castSpell(w, "shocking-grasp", 8, 4, 0);
    if (!w.entities.includes(e)) gone++;
    rng = (rng + w.rng) >>> 0;
  }
  return `${gone} ${rng}`;
};

// Mirrors ChemistryTests.AStruckFrozenGoblinShatters (docs/MATERIALS.md "Frozen things shatter").
test("a struck frozen goblin shatters", () => {
  assert.equal(`${frostThenSpark(true)} | ${frostThenSpark(false)}`, "40 496377796 | 18 496377796"); // the same in the C# core
  const w = createWorld(20, 9, 1);
  for (const c of w.cells) c.t = "dirt";
  spawn(w, "goblin", 8, 4)!.status.wet = 60;
  castSpell(w, "ray-of-frost", 8, 4, 0);
  castSpell(w, "shocking-grasp", 8, 4, 0);
  assert.ok(w.log.some((l) => l.text === "Goblin shatters"));
  assert.deepEqual(validateRules(composeRules([{ sim: { status: { shatter: { when: 3 } } } } as unknown as ModPatch])), ["sim status.shatter.when must be a name"]);
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
test("water runs down the land's slope", () => withoutSoaking(() => {
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
}));

// Mirrors ChemistryTests.ThatchFlaresAndLightsTheLogWallWhichBurnsLong: thatch at (2,1) and (3,1) against a
// log wall at (4,1), the thatch set alight; how often the logs catch in 30 ticks over 40 seeds, hot and at heat 1.
test("thatch's heat lights the log wall beside it", () => {
  const lit = (heat: number) => {
    const thatch = RULES.terrain.thatch!, was = thatch.heat;
    thatch.heat = heat;
    try {
      let n = 0;
      for (let seed = 1; seed <= 40; seed++) {
        const w = createWorld(8, 3, seed);
        for (const c of w.cells) c.t = "dirt";
        cellAt(w, 2, 1)!.t = "thatch";
        cellAt(w, 3, 1)!.t = "thatch";
        cellAt(w, 4, 1)!.t = "logwall";
        applyElement(w, 2, 1, "fire", { power: 3, direct: false });
        run(w, 30);
        const logs = cellAt(w, 4, 1)!;
        if (logs.fire > 0 || logs.t !== "logwall") n++;
      }
      return n;
    } finally {
      thatch.heat = was;
    }
  };
  const hot = lit(2.5), plain = lit(1);
  assert.ok(hot > plain, `hot ${hot}, plain ${plain}`);
  assert.equal(`${hot} ${plain}`, "32 23"); // the same in the C# core
});

// Mirrors ChemistryTests.ThatchCaught: over 40 seeds, how often the second thatch (3,1) caught from the first in
// one tick; dampUntil is its damp, and the fire is lit at tick lightAt.
const thatchCaught = (dampUntil: number, lightAt: number) => {
  let caught = 0;
  for (let seed = 1; seed <= 40; seed++) {
    const w = createWorld(8, 3, seed);
    for (const c of w.cells) c.t = "dirt";
    cellAt(w, 2, 1)!.t = "thatch";
    cellAt(w, 3, 1)!.t = "thatch";
    cellAt(w, 3, 1)!.dampUntil = dampUntil;
    run(w, lightAt);
    applyElement(w, 2, 1, "fire", { power: 3, direct: false });
    step(w);
    if (cellAt(w, 3, 1)!.fire > 0) caught++;
  }
  return caught;
};

// Mirrors ChemistryTests.SoakedThatchCatchesPoorlyUntilItDries (docs/MATERIALS.md "Soaked burns poorly").
test("soaked thatch catches poorly until it dries", () => {
  assert.equal(RULES.terrain.thatch!.dampens, 0.5);
  const dry = thatchCaught(0, 0), damp = thatchCaught(2147483647, 0);
  assert.ok(damp < dry, `damp thatch caught ${damp} times in 40, dry ${dry}`);
  assert.equal(`${dry} ${damp}`, "21 10"); // the same in the C# core
  assert.equal(thatchCaught(5, 10), thatchCaught(0, 10), "once it has dried, it catches as dry thatch does");
});

// Mirrors ChemistryTests.ThatchBurnTicks: how many ticks a lone thatch (3,1) on dirt burns once lit.
const thatchBurnTicks = (dampUntil: number) => {
  const w = createWorld(8, 3, 1);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 3, 1)!.t = "thatch";
  cellAt(w, 3, 1)!.dampUntil = dampUntil;
  applyElement(w, 3, 1, "fire", { power: 3, direct: false });
  let ticks = 0;
  while (cellAt(w, 3, 1)!.fire > 0 && ticks < 100) {
    step(w);
    ticks++;
  }
  return ticks;
};

// Mirrors ChemistryTests.SoakedThatchBurnsOutSooner: caught damp, its fuel is scaled by 1 - soak.dampens too.
test("soaked thatch burns out sooner", () => {
  const dry = thatchBurnTicks(0), damp = thatchBurnTicks(2147483647);
  assert.ok(damp < dry, `damp thatch burnt ${damp} ticks, dry ${dry}`);
  assert.equal(`${dry} ${damp}`, "18 9"); // the same in the C# core
});

/** World.CellHash in the C# port, rust included (written only when it isn't 0). */
function rustHash(w: World): string {
  const s = w.cells.map((c) =>
    `${c.t},${c.water},${c.frozen ? 1 : 0},${c.cover ?? ""},${c.fire},${c.fuel},${c.gas ?? ""},${c.gasAmt},${c.charge}${c.h ? `,h${c.h}` : ""}${c.life ? `,l${c.life}` : ""}${c.rust ? `,r${c.rust}` : ""}${c.heat ? `,t${c.heat}` : ""}${c.lit ? `,b${c.lit}` : ""};`).join("");
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 0x01000193);
  return (h >>> 0).toString(16);
}

// Mirrors ChemistryTests.WetMetal: metal 8 x 4, its left half under 2 water, run for ticks.
const wetMetal = (ticks: number) => {
  const w = createWorld(8, 4, 1);
  for (const c of w.cells) c.t = "metal";
  for (let x = 0; x < 4; x++) for (let y = 0; y < 4; y++) cellAt(w, x, y)!.water = 2;
  run(w, ticks);
  return { w, rusted: w.cells.filter((c) => c.t === "rusted-metal").length, lines: w.log.filter((l) => l.text === "The metal rusts through").length };
};

// Mirrors ChemistryTests.WetMetalRustsThroughAndDryMetalDoesNot (docs/MATERIALS.md rust).
test("wet metal rusts through and dry metal does not", () => {
  assert.equal(RULES.terrain.metal!.rustEvery, 2000);
  const early = wetMetal(1000);
  assert.equal(early.rusted, 0, "not yet");
  assert.ok(early.w.cells.some((c) => (c.rust ?? 0) > 0), "but counting");
  const { w, rusted, lines } = wetMetal(2400);
  assert.ok(rusted > 0);
  assert.ok(lines >= 1 && lines < rusted, "a line a pass, not a line a cell");
  assert.ok(RULES.terrain["rusted-metal"]!.hardness! < RULES.terrain.metal!.hardness!, "weaker");
  assert.ok(!RULES.terrain["rusted-metal"]!.tags.includes("conductive"), "and it doesn't conduct");
  assert.equal(`${rusted} ${lines} ${rustHash(w)}`, "20 1 1dc2aae5"); // the same in the C# core
  const dry = createWorld(8, 4, 1);
  for (const c of dry.cells) c.t = "metal";
  run(dry, 2400);
  assert.ok(dry.cells.every((c) => c.t === "metal" && !c.rust), "dry metal keeps");
});

test("broken rust is refused with the C# wording", () => {
  const r = composeRules([{ terrain: { tin: { name: "Tin", tags: ["ground"], rust: { every: 0.5, leaves: "nope" } } } } as unknown as ModPatch]);
  assert.deepEqual(validateRules(r), ["terrain tin: rustsTo nope unknown", "terrain tin: rustEvery must be a whole number from 1"]);
});

// Mirrors ChemistryTests.OilyPond: a 10 x 5 pond (pit under 3 water) with oil spilt at (1, 2), left to float out.
const oilyPond = (spreadTicks: number) => {
  const w = createWorld(10, 5, 1);
  for (const c of w.cells) Object.assign(c, { t: "pit", water: 3 });
  cellAt(w, 1, 2)!.cover = "oil";
  run(w, spreadTicks);
  return w;
};

// Mirrors ChemistryTests.OilFloatsOutOverWaterAndBurnsAcrossItUntilItIsUsedUp.
test("oil floats out over water and burns across it until it is used up", () => {
  const w = oilyPond(80);
  const slick = w.cells.filter((c) => c.cover === "oil").length;
  assert.ok(slick > 5, "it spread across the surface");
  assert.equal(w.cells.reduce((s, c) => s + c.water, 0), 150, "and the water stays as it was");
  applyElement(w, 1, 2, "fire", { power: 1, direct: false });
  let most = 0;
  for (let t = 0; t < 120; t++) {
    step(w);
    most = Math.max(most, w.cells.filter((c) => c.fire > 0).length);
  }
  assert.ok(most > 3, "the fire spread over the water");
  assert.equal(w.cells.filter((c) => c.fire > 0).length, 0, "and went out");
  assert.equal(w.cells.filter((c) => c.cover !== null).length, 0, "with the oil used up, and no ash on the water");
  const water = w.cells.reduce((s, c) => s + c.water, 0);
  assert.ok(water >= 1 && water <= 149, "the heat boiled some off (sim.fire.boilChance), but the water itself never burned");
  assert.equal(`${slick} ${most} ${water} ${rustHash(w)} ${w.rng}`, "29 28 74 78db8029 189079474"); // the same in the C# core
});

test("a broken float is refused with the C# wording", () => {
  const r = composeRules([{ covers: { tar: { name: "Tar", tags: [], float: { every: 0 } } } } as unknown as ModPatch]);
  assert.deepEqual(validateRules(r), ["cover tar: floatEvery must be a whole number from 1"]);
});

// Mirrors ChemistryTests.LightningFusesSandIntoGlass.
test("lightning fuses sand into glass", () => {
  const w = createWorld(9, 5, 1);
  for (const c of w.cells) c.t = "sand";
  cellAt(w, 6, 2)!.water = 2;
  castSpell(w, "shocking-grasp", 2, 2, 2);
  const glass = w.cells.filter((c) => c.t === "glass").length;
  assert.ok(glass > 0, "the strike fused some of the sand");
  assert.ok(w.log.some((l) => l.text === "The strike fuses the sand into glass"));
  const g = RULES.terrain.glass!;
  assert.ok(g.tags.includes("brittle") && !g.tags.includes("flammable") && !g.tags.includes("conductive"), "brittle; doesn't burn or conduct");
  assert.ok(["glow-ball-amber", "glow-ball-azure", "glow-ball-rose"].every((id) => RULES.items[id]!.material === "glass"));
  assert.ok(["glow-ball-amber", "glow-ball-azure", "glow-ball-rose"].every((id) => RULES.items[id]!.breaks?.speed === 8 && !RULES.items[id]!.breaks.spill), "glow balls shatter thrown hard");
  run(w, 40);
  assert.equal(`${glass} ${rustHash(w)} ${w.rng}`, "1 e8b4e694 1"); // the same in the C# core
});

// Mirrors ChemistryTests.MetalStaysHotAfterFireBurnsWhatStandsOnItAndSteamsWater.
test("metal stays hot after fire, burns what stands on it, and steams water", () => {
  const w = createWorld(9, 5, 1);
  for (const c of w.cells) c.t = "metal";
  // Stood on the metal as World.SpawnRaider stands it.
  const e = { id: w.nextId++, kind: "giant-spider", x: 4, y: 2, hp: 99, status: {} as Record<string, number> };
  w.entities.push(e);
  castSpell(w, "fire-bolt", 4, 2, 1);
  assert.equal(cellAt(w, 4, 2)!.heat, 120, "the fire left it hot");
  const hp = e.hp;
  run(w, 12);
  assert.equal(cellAt(w, 4, 2)!.heat, 108, "and it stays hot, cooling a tick at a time");
  assert.ok(e.hp < hp, "it burns what stands on it");
  castSpell(w, "create-water", 4, 2, 1);
  let steam = 0;
  for (let t = 0; t < 10; t++) {
    step(w);
    steam = Math.max(steam, w.cells.reduce((s, c) => s + (c.gas === "steam" ? c.gasAmt : 0), 0));
  }
  assert.ok(steam > 0, "the water hisses into steam");
  assert.ok(w.log.some((l) => l.text === "Water hisses into steam on the hot metal"));
  assert.ok(cellAt(w, 4, 2)!.heat! < 98, "and cools it faster than time alone");
  assert.equal(`${e.hp} ${steam} ${cellAt(w, 4, 2)!.heat ?? 0} ${rustHash(w)} ${w.rng}`, "84 3 0 519dc5ad 1639742223"); // the same in the C# core
});

// Mirrors ChemistryTests.SteamScaldsFleshNotBoneAndCondensesBackIntoWater.
test("steam scalds flesh, not bone, and condenses back into water", () => {
  const w = createWorld(9, 5, 1);
  for (const c of w.cells) c.t = "metal";
  for (const c of w.cells) Object.assign(c, { gas: "steam", gasAmt: 6 });
  // Stood as World.SpawnRaider stands them.
  const goblin = { id: w.nextId++, kind: "goblin", x: 3, y: 2, hp: 99, status: {} as Record<string, number> };
  const hound = { id: w.nextId++, kind: "bone-hound", x: 5, y: 2, hp: 99, status: {} as Record<string, number> };
  w.entities.push(goblin, hound);
  run(w, 30);
  assert.ok(goblin.hp < 99, "flesh scalds");
  assert.equal(hound.hp, 99, "bone doesn't");
  run(w, 150);
  const steam = w.cells.reduce((s, c) => s + (c.gas === "steam" ? c.gasAmt : 0), 0), water = w.cells.reduce((s, c) => s + c.water, 0);
  assert.ok(water > 0, "it cooled back into water");
  assert.equal(`${goblin.hp} ${hound.hp} ${steam} ${water} ${rustHash(w)} ${w.rng}`, "89 99 0 112 d06de483 2670964354"); // the same in the C# core
});

// Mirrors ChemistryTests.SteamByHeat: a 9 x 5 metal field full of steam, (4,2) left hot or not and the cells round
// it raised, run 60 ticks.
const steamByHeat = (hot: boolean) => {
  const w = createWorld(9, 5, 1);
  for (const c of w.cells) Object.assign(c, { t: "metal", gas: "steam", gasAmt: 6 });
  // Raised a step, so water condensed further off can't run in.
  for (let y = 1; y <= 3; y++) for (let x = 3; x <= 5; x++) cellAt(w, x, y)!.h = 1;
  if (hot) cellAt(w, 4, 2)!.heat = 120;
  run(w, 60);
  let near = 0, far = 0;
  for (let y = 0; y < 5; y++)
    for (let x = 0; x < 9; x++) {
      if (Math.abs(x - 4) <= 1 && Math.abs(y - 2) <= 1) near += cellAt(w, x, y)!.water;
      else far += cellAt(w, x, y)!.water;
    }
  return { w, near, far };
};

// Mirrors ChemistryTests.SteamCondensesOnlyAwayFromHeat.
test("steam condenses only away from heat", () => {
  const { w, near, far } = steamByHeat(true);
  assert.equal(near, 0, "beside the heat it doesn't condense");
  assert.ok(far > 0, "drifted away from it, it does");
  const coldNear = steamByHeat(false).near;
  assert.ok(coldNear > 0, "and with no heat, there too");
  assert.equal(`${near} ${far} ${coldNear} ${rustHash(w)} ${w.rng}`, "0 77 2 d6bb1743 3544326918"); // the same in the C# core
});

// Mirrors ChemistryTests.TheFireCombosCastScaldingSteamALavaPoolAndAFlameWave.
test("the fire combos cast scalding steam, a lava pool and a flame wave", () => {
  const w = createWorld(24, 9, 1);
  for (const c of w.cells) c.t = "grass";
  // Stood as World.SpawnRaider stands them.
  const goblin = { id: w.nextId++, kind: "goblin", x: 18, y: 4, hp: 99, status: {} as Record<string, number> };
  const hound = { id: w.nextId++, kind: "bone-hound", x: 10, y: 4, hp: 99, status: {} as Record<string, number> };
  w.entities.push(goblin, hound);
  castSpell(w, "flame-wave", 2, 4, 1);
  assert.deepEqual(w.wind, { dir: 1, ticks: 18 }, "the flame wave raises a wind along it");
  assert.ok(w.cells.filter((c) => c.fire > 0).length > 3, "and sets its cone alight");
  castSpell(w, "lava-pool", 10, 4, 0);
  const pool = w.cells.filter((c) => c.t === "lava-pool").length;
  assert.ok(pool > 0);
  assert.equal(cellAt(w, 10, 4)!.heat, 300, "the lava is hot");
  castSpell(w, "scalding-steam", 18, 4, 0);
  assert.ok(cellAt(w, 18, 4)!.gas === "steam" && cellAt(w, 18, 4)!.gasAmt === 6, "a burst of thick steam");
  run(w, 30);
  assert.ok(goblin.hp < 99, "the steam scalds the goblin");
  assert.ok(hound.hp < 99, "the lava burns the hound");
  run(w, 300);
  assert.equal(w.cells.filter((c) => c.t === "lava-pool").length, 0, "the lava pool cools");
  assert.ok(w.log.some((l) => l.text === "The lava pool cools into rock"));
  assert.equal(`${pool} ${goblin.hp} ${hound.hp} ${rustHash(w)} ${w.rng}`, "9 90 -1 d6bc3323 2450008921"); // the same in the C# core
});

// Mirrors ChemistryTests.TheAirCombosCastLightningBoltRainCloudSandBlastAndWhirlwind.
test("the air combos cast lightning bolt, rain cloud, sand blast and whirlwind", () => {
  const w = createWorld(24, 11, 1);
  for (const c of w.cells) c.t = "grass";
  castSpell(w, "sand-blast", 1, 2, 1);
  const sand = w.cells.filter((c) => c.t === "sand").length;
  assert.ok(sand > 0, "the blast leaves sand along its line");
  castSpell(w, "lightning-bolt", 1, 2, 1);
  const glass = w.cells.filter((c) => c.t === "glass").length;
  assert.ok(glass > 0, "the bolt fuses the sand it crosses into glass");
  assert.ok(w.cells.some((c) => c.fire > 0), "and sets the grass beyond smouldering");
  castSpell(w, "rain-cloud", 16, 2, 0);
  assert.equal(cellAt(w, 16, 2)!.gas, "cloud");
  Object.assign(cellAt(w, 10, 8)!, { gas: "smoke", gasAmt: 4 });
  castSpell(w, "whirlwind", 12, 8, 0);
  assert.equal(cellAt(w, 10, 8)!.gas, null, "the whirlwind draws the smoke in");
  let drawn = 0;
  for (let y = 7; y <= 9; y++) for (let x = 11; x <= 13; x++) drawn += cellAt(w, x, y)!.gas === "smoke" ? cellAt(w, x, y)!.gasAmt : 0;
  assert.equal(drawn, 4, "toward its middle, all of it");
  run(w, 60);
  let rained = 0;
  for (let x = 14; x <= 18; x++) rained += cellAt(w, x, 2)!.water;
  assert.ok(rained > 0, "the cloud rains on the cells under it");
  assert.equal(`${sand} ${glass} ${rained} ${rustHash(w)} ${w.rng}`, "8 8 4 49212457 399058435"); // the same in the C# core
});

// Mirrors ChemistryTests.LightLastsItsDurationThenFades.
test("light lasts its duration, then fades", () => {
  const s = RULES.spells.find((x) => x.id === "light")!;
  assert.deepEqual([s.light!.bright, s.light!.dim, s.duration], [20, 20, 21600]);
  const w = createWorld(7, 5, 1);
  for (const c of w.cells) c.t = "dirt";
  castSpell(w, "light", 3, 2, 0);
  assert.equal(cellAt(w, 3, 2)!.lit, 21600, "lit for the hour");
  assert.equal(w.cells.filter((c) => (c.lit ?? 0) > 0).length, 1, "on the cell touched only");
  run(w, 10800);
  const half = rustHash(w);
  assert.equal(cellAt(w, 3, 2)!.lit, 10800, "half way");
  run(w, 10800);
  assert.equal(cellAt(w, 3, 2)!.lit, 0, "and it fades");
  assert.equal(`${half} ${rustHash(w)} ${w.rng}`, "53f7abc0 1e6d522 2668737540"); // the same in the C# core
});

// Mirrors ChemistryTests.AGustPushesSmokeAndSteamFansTheFireAndKeepsMetalHot.
test("a gust pushes smoke and steam, fans the fire, and keeps metal hot", () => {
  const w = createWorld(16, 5, 1);
  for (const c of w.cells) c.t = "dirt";
  for (let x = 5; x <= 7; x++) cellAt(w, x, 2)!.t = "grass";
  Object.assign(cellAt(w, 10, 2)!, { t: "metal", heat: 40 });
  Object.assign(cellAt(w, 3, 2)!, { gas: "smoke", gasAmt: 4 });
  Object.assign(cellAt(w, 4, 2)!, { gas: "steam", gasAmt: 4 });
  applyElement(w, 5, 2, "fire", { power: 3, direct: false });
  assert.equal(cellAt(w, 5, 2)!.fire, 2);
  castSpell(w, "gust-of-wind", 1, 2, 1);
  assert.ok(cellAt(w, 3, 2)!.gas === null && cellAt(w, 4, 2)!.gas === null, "the gust swept the smoke and steam on");
  assert.equal(w.cells.filter((c, i) => i % 16 > 4 && c.gas !== null).reduce((s, c) => s + c.gasAmt, 0), 8, "east, all of it");
  assert.equal(cellAt(w, 5, 2)!.fire, 3, "it fanned the fire hotter");
  assert.ok(cellAt(w, 6, 2)!.fire > 0, "and on downwind");
  assert.equal(cellAt(w, 10, 2)!.heat, 120, "and kept the metal hot");
  run(w, 20);
  assert.equal(`${rustHash(w)} ${w.rng}`, "94877252 97577195"); // the same in the C# core
});

// Mirrors ChemistryTests.AWetThingOfADampenedMaterialCatchesPoorly: a spark lights a thing of thatch with its
// material's flammability, less its soak.dampens while it is wet.
test("a wet thing of a dampened material catches poorly", () => {
  const before = composeRules([]);
  installRules(composeRules([{ entities: { strawman: { name: "Straw man", material: "thatch", hp: 999, raider: { ticksPerSquare: 5 } } } } as unknown as ModPatch]));
  try {
    const caught = (wet: boolean) => {
      let n = 0;
      for (let seed = 1; seed <= 40; seed++) {
        const w = createWorld(20, 9, seed);
        for (const c of w.cells) c.t = "stone";
        // Stood on the stone as World.SpawnRaider stands it (spawn keeps off blocking cells).
        const e = { id: w.nextId++, kind: "strawman", x: 8, y: 4, hp: 999, status: {} as Record<string, number> };
        w.entities.push(e);
        if (wet) e.status["wet"] = 60;
        castSpell(w, "shocking-grasp", 8, 4, 0);
        if ((e.status["burning"] ?? 0) > 0) n++;
      }
      return n;
    };
    const dry = caught(false), soaked = caught(true);
    assert.ok(soaked < dry, `wet it caught ${soaked} times in 40, dry ${dry}`);
    assert.equal(`${dry} ${soaked}`, "25 10"); // the same in the C# core
  } finally {
    installRules(before);
  }
});

// Mirrors ChemistryTests.WindowGlassShattersUnderForceAndFallsWithItsWall: fire beside a pane does nothing to
// it; Fireball's blast and ground shaken under one break it, a heavy thing breaks one, and a pane with its only
// wall burnt away falls. What it leaves is an open gap.
test("window glass shatters under force and falls with its wall", () => {
  const w = createWorld(16, 10, 5);
  for (const c of w.cells) c.t = "dirt";
  for (let x = 3; x <= 7; x++) for (let y = 3; y <= 5; y++) cellAt(w, x, y)!.t = "logwall";
  for (const [x, y] of [[5, 3], [3, 4], [7, 4], [5, 5], [11, 3]] as const) cellAt(w, x, y)!.t = "pane";
  cellAt(w, 11, 4)!.t = "logwall";
  applyElement(w, 5, 2, "fire", { power: 2, direct: true });
  let panes = cellAt(w, 5, 3)!.t;
  castSpell(w, "fireball", 5, 1, 0);
  panes += ` ${cellAt(w, 5, 3)!.t} ${cellAt(w, 7, 4)!.t}`;
  reshape(w, 3, 4, 1);
  panes += ` ${cellAt(w, 3, 4)!.t}`;
  shatter(w, 5, 5);
  panes += ` ${cellAt(w, 5, 5)!.t}`;
  applyElement(w, 11, 4, "fire", { power: 3, direct: true });
  run(w, 400);
  panes += ` ${cellAt(w, 11, 4)!.t} ${cellAt(w, 11, 3)!.t}`;
  assert.equal(panes, "pane rubble pane rubble rubble rubble rubble");
  assert.ok(w.log.some((l) => l.text === "The window pane shatters"));
  assert.equal(isBlocking(cellAt(w, 5, 3)!), false, "an open gap");
  assert.equal(`${rustHash(w)} ${w.rng}`, "fd5c22c2 3314880483"); // the same in the C# core
});

// Mirrors ChemistryTests.ShockWaterReachesSwimmersAndTheWetOnTheBank (conduct.chainsWet): lightning into one
// end of a pond charges all of it; a goblin swimming at the far end is shocked, a wet one on the bank beside
// the water is too, a dry one on the bank isn't.
const shockWater = () => {
  const w = createWorld(14, 8, 3);
  for (const c of w.cells) c.t = "dirt";
  for (let x = 3; x <= 8; x++) for (let y = 2; y <= 4; y++) Object.assign(cellAt(w, x, y)!, { t: "pit", water: 3 });
  const swimmer = spawn(w, "goblin", 8, 3)!;
  const wetBank = spawn(w, "goblin", 9, 3)!;
  wetBank.status.wet = 60;
  const dryBank = spawn(w, "goblin", 6, 5)!;
  castSpell(w, "shocking-grasp", 3, 3, 1);
  step(w);
  return { w, swimmer, wetBank, dryBank };
};

test("shock-water: lightning in a pond shocks swimmers and the wet beside it", () => {
  const { w, swimmer, wetBank, dryBank } = shockWater();
  assert.equal(10 - swimmer.hp, RULES.sim.zapDamage * 2, "the swimmer is wet, so twice");
  assert.equal(10 - wetBank.hp, RULES.sim.zapDamage * 2, "wet beside the water: shocked too");
  assert.equal(dryBank.hp, 10, "dry on the bank: not");
  assert.equal(`${rustHash(w)} ${w.rng} ${swimmer.hp} ${wetBank.hp} ${dryBank.hp}`, "d2510429 3 4 4 10"); // the same in the C# core
  const before = RULES;
  const flesh = (chainsWet: unknown) => composeRules([{ terrain: { flesh: { ...RULES.terrain.flesh, chainsWet } } } as unknown as ModPatch]);
  assert.deepEqual(validateRules(flesh(2)), ["terrain flesh: conduct.chainsWet must be true or false"]);
  installRules(flesh(false));
  try {
    assert.equal(shockWater().wetBank.hp, 10, "without chainsWet the bank is safe");
  } finally {
    installRules(before);
  }
});
