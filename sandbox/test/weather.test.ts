// The clock and the weather (sky.ts) and what the weather does to cells
// (tickWeather in sim.ts). The goldens are shared with the C# port's
// WeatherTests (unity/Assets/Prototype/Chemistry/Tests/WeatherTests.cs).
import { test } from "node:test";
import assert from "node:assert/strict";
import { RULES, composeRules, installRules, validateRules, type ModPatch } from "../src/rules.ts";
import { step } from "../src/sim.ts";
import { applyElement } from "../src/react.ts";
import { castSpell } from "../src/actions.ts";
import { dayOf, kindOf, minuteOfDay, minutesAt, weatherAt } from "../src/sky.ts";
import { type World, cellAt, createWorld, hashWorld } from "../src/world.ts";
import stormSeason from "../../rules/mods/storm-season.json" with { type: "json" };

const run = (w: World, ticks: number) => { for (let i = 0; i < ticks; i++) step(w); };

/** World.CellHash in the C# port. */
function cellHash(w: World): string {
  const s = w.cells.map((c) =>
    `${c.t},${c.water},${c.frozen ? 1 : 0},${c.cover ?? ""},${c.fire},${c.fuel},${c.gas ?? ""},${c.gasAmt},${c.charge}${c.h ? `,h${c.h}` : ""};`).join("");
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 0x01000193);
  return (h >>> 0).toString(16);
}

function withRules(patch: ModPatch, body: () => void) {
  const before = composeRules([]);
  installRules(composeRules([patch]));
  try { body(); } finally { installRules(before); }
}

/** Short, quick weather for the goldens: stretches of 0.2 min (72 ticks), no calm start. */
const QUICK: ModPatch = {
  sim: { weather: { periodMinutes: 0.2, rampMinutes: 0.05, calmPeriods: 0, rain: { fillEvery: 4, fillChance: 0.2, douseChance: 0.3 }, storm: { strikeEvery: 6, strikeChance: 1 } } },
};

/** A walled field of grass with a pond, a fire lit on it. */
function field(seed: number): World {
  const w = createWorld(24, 12, seed);
  for (let x = 0; x < 24; x++) for (let y = 0; y < 12; y++) {
    const c = cellAt(w, x, y)!;
    c.t = x === 0 || y === 0 || x === 23 || y === 11 ? "bedrock" : x < 6 ? "pit" : "grass";
    if (x > 0 && x < 6 && y > 0 && y < 11) c.water = 4;
  }
  castSpell(w, "fire-bolt", 14, 6, 1);
  return w;
}

test("the sky's rules validate, with storm season on", () => {
  assert.deepEqual(validateRules(RULES), []);
  assert.deepEqual(validateRules(composeRules([stormSeason.patch as ModPatch])), []);
});

test("the clock starts at day 1, 09:00, and a day lasts 24 real minutes", () => {
  const p = RULES.sim;
  assert.equal(minutesAt(p, 0), 9 * 60);
  const perDay = 24 * 60 * p.ticksPerSecond;
  assert.equal(minuteOfDay(minutesAt(p, perDay / 2)), 21 * 60, "half a day later it's 21:00");
  assert.equal(dayOf(minutesAt(p, perDay)), 2);
  assert.equal(minuteOfDay(minutesAt(p, perDay)), 9 * 60);
});

test("the world starts calm, then the forecast matches the C# golden", () => {
  const p = RULES.sim;
  assert.equal(kindOf(p, 1, 0), "clear");
  assert.equal(kindOf(p, 1, 1), "clear");
  const forecast = Array.from({ length: 24 }, (_, k) => kindOf(p, 1, k)).join(" ");
  assert.equal(forecast, "clear clear cloudy windy cloudy clear storm rain rain storm cloudy rain cloudy clear rain fog windy rain windy rain rain storm windy snow");
  // Same seed, same tick: the same weather.
  assert.deepEqual(weatherAt(p, 9, 5000), weatherAt(p, 9, 5000));
});

test("weather ramps in and out, and storms bring wind", () => {
  withRules(stormSeason.patch as ModPatch, () => {
    const p = RULES.sim;
    const length = 4 * 60 * p.ticksPerSecond;
    let sawStorm = false;
    for (let k = 0; k < 12; k++) {
      const start = weatherAt(p, 3, k * length), mid = weatherAt(p, 3, k * length + length / 2);
      assert.equal(mid.strength, 100);
      if (k === 0) assert.equal(start.strength, 0, "the first stretch ramps in");
      if (mid.kind === "storm") { sawStorm = true; assert.ok(mid.windDir >= 0 && mid.windDir < 4); }
      else assert.equal(mid.windDir, -1);
    }
    assert.ok(sawStorm);
  });
});

test("rain douses a fire and fills the field; storms strike; matches the C# golden", () => {
  withRules({ sim: { weather: { ...QUICK.sim!.weather as object, weights: { clear: 0, cloudy: 0, windy: 0, fog: 0, rain: 0, storm: 1, snow: 0 } } } } as ModPatch, () => {
    const a = field(5), b = field(5);
    run(a, 150);
    run(b, 150);
    assert.equal(hashWorld(a), hashWorld(b));
    const strikes = a.log.filter((l) => l.text === "Lightning strikes").reduce((n, l) => n + l.count, 0);
    assert.ok(strikes > 0, "lightning struck");
    assert.ok(a.log.some((l) => l.text === "The rain puts out a fire"), "the rain put a fire out");
    assert.ok(cellAt(a, 20, 2)!.water > 0 || a.cells.filter((c) => c.t === "grass" && c.water > 0).length > 10, "the grass is wet");
    assert.equal(`${cellHash(a)} ${a.rng} ${strikes}`, "a80f8ead 275421548 24");
  });
});

test("mixed quick weather over many stretches matches the C# golden", () => {
  withRules(QUICK, () => {
    const w = field(8);
    run(w, 1200);
    const kinds = Array.from({ length: 17 }, (_, k) => kindOf(RULES.sim, 8, k)).join(" ");
    assert.equal(`${cellHash(w)} ${w.rng}`, "6d146020 1611455719", kinds);
  });
});

test("a meteor shower brings meteors down a little later as craters, and matches the C# golden", () => {
  withRules({ sim: { weather: { ...QUICK.sim!.weather as object, weights: { clear: 1, cloudy: 0, windy: 0, fog: 0, rain: 0, storm: 0, snow: 0 },
    meteors: { showerChance: 1, every: 6, chance: 1, fallTicks: 11 } } } } as ModPatch, () => {
    const w = field(6);
    let seen: [number, number, number] | undefined, appeared = -1;
    for (let i = 0; i < 150; i++) {
      step(w);
      if (!seen && w.newMeteors.length) [seen, appeared] = [w.newMeteors[0], w.tick];
    }
    assert.ok(seen, "a meteor appeared");
    assert.equal(seen![2], appeared + 11, "it lands fallTicks after it appears");
    const strikes = w.log.filter((l) => l.text === "A meteor strikes").reduce((n, l) => n + l.count, 0);
    assert.ok(strikes > 0, "meteors landed");
    assert.ok(w.cells.some((c) => c.h < 0), "and left craters");
    assert.equal(`${cellHash(w)} ${w.rng} ${strikes}`, "e9d48abc 2710019217 19");
  });
});

test("snow freezes shallow water, lies on the field and banks against the walls; a dry spell thaws and melts it; matches the C# golden", () => {
  const snowy = { sim: { weather: { ...QUICK.sim!.weather as object, weights: { clear: 0, cloudy: 0, windy: 0, fog: 0, rain: 0, storm: 0, snow: 1 },
    snow: { freezeEvery: 4, freezeChance: 0.5 } } } } as ModPatch;
  const clear = { sim: { weather: { ...QUICK.sim!.weather as object, weights: { clear: 1, cloudy: 0, windy: 0, fog: 0, rain: 0, storm: 0, snow: 0 },
    meteors: { showerChance: 0 }, rain: { dryEvery: 6, dryChance: 0 }, snow: { thawChance: 0.5 } } } } as ModPatch;
  const w = field(7);
  for (let x = 8; x < 12; x++) for (let y = 2; y < 10; y++) cellAt(w, x, y)!.water = 1 + (x % 2);
  let frozen = 0;
  withRules(snowy, () => {
    run(w, 150);
    frozen = w.cells.filter((c) => c.frozen && c.water > 0).length;
    assert.ok(frozen > 10, `the puddles froze (${frozen})`);
    assert.ok(w.cells.filter((c) => c.water > 2 && c.frozen).length === 0, "the deep pond did not");
    const snowy = w.cells.filter((c) => c.cover === "snow" || c.cover === "snowbank").length;
    const banks = w.cells.filter((c) => c.cover === "snowbank").length;
    assert.ok(snowy > 50, `snow lies on the field (${snowy})`);
    assert.ok(banks > 0, `and drifts into banks against the walls (${banks})`);
    assert.equal(`${cellHash(w)} ${w.rng} ${frozen} ${snowy} ${banks}`, "e1e5624 4247680091 39 121 26");
  });
  withRules(clear, () => {
    run(w, 150);
    const still = w.cells.filter((c) => c.frozen && c.water > 0).length;
    assert.ok(still < frozen / 2, `a dry spell thawed most of it (${still} of ${frozen} still ice)`);
    const left = w.cells.filter((c) => c.cover === "snow" || c.cover === "snowbank").length;
    assert.ok(left < 20, `the snow melted (${left} left)`);
    assert.equal(`${cellHash(w)} ${w.rng} ${still} ${left}`, "b2481d88 2377318072 0 0");
  });
});

test("fire melts snow into water instead of lighting the grass under it", () => {
  const w = field(9);
  const c = cellAt(w, 18, 3)!;
  c.cover = "snowbank";
  applyElement(w, 18, 3, "fire", { power: 1, direct: false });
  assert.equal(c.cover, "snow", "a bank slumps to snow");
  assert.equal(c.fire, 0);
  applyElement(w, 18, 3, "fire", { power: 1, direct: false });
  assert.equal(c.cover, null, "then melts");
  assert.equal(c.water, 2, "into water");
  assert.equal(c.fire, 0, "and the grass doesn't catch");
});

// Mirrors ChemistryTests.SoakingMatchesTs: thin water soaks in on dirt, arid ground and rock; a pond stays.
test("thin water soaks in and a pond stays, matching the C# golden", () => {
  const w = createWorld(14, 9, 1);
  for (const c of w.cells) c.t = "dirt";
  cellAt(w, 3, 3)!.water = 1;
  cellAt(w, 3, 6)!.water = 1;
  for (let y = 3; y <= 5; y++)
    for (let x = 8; x <= 10; x++) {
      cellAt(w, x, y)!.t = "pit";
      cellAt(w, x, y)!.water = 4;
    }
  cellAt(w, 5, 1)!.t = "arid";
  cellAt(w, 5, 1)!.water = 2;
  cellAt(w, 12, 7)!.t = "rock";
  cellAt(w, 12, 7)!.water = 1;
  for (let i = 0; i < 20; i++) step(w);
  assert.equal(`${cellHash(w)} ${w.rng}`, "20661f72 1");
});
