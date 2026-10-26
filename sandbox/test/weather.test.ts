// The clock and the weather (sky.ts) and what the weather does to cells
// (tickWeather in sim.ts). The goldens are shared with the C# port's
// WeatherTests (unity/Assets/Prototype/Chemistry/Tests/WeatherTests.cs).
import { test } from "node:test";
import assert from "node:assert/strict";
import { RULES, composeRules, installRules, validateRules, type ModPatch } from "../src/rules.ts";
import { step } from "../src/sim.ts";
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
  assert.equal(forecast, "clear clear cloudy windy cloudy clear rain fog rain storm cloudy rain cloudy clear rain windy cloudy rain cloudy rain rain rain windy storm");
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
  withRules({ sim: { weather: { ...QUICK.sim!.weather as object, weights: { clear: 0, cloudy: 0, windy: 0, fog: 0, rain: 0, storm: 1 } } } } as ModPatch, () => {
    const a = field(5), b = field(5);
    run(a, 150);
    run(b, 150);
    assert.equal(hashWorld(a), hashWorld(b));
    const strikes = a.log.filter((l) => l.text === "Lightning strikes").reduce((n, l) => n + l.count, 0);
    assert.ok(strikes > 0, "lightning struck");
    assert.ok(a.log.some((l) => l.text === "The rain puts out a fire"), "the rain put a fire out");
    assert.ok(cellAt(a, 20, 2)!.water > 0 || a.cells.filter((c) => c.t === "grass" && c.water > 0).length > 10, "the grass is wet");
    assert.equal(`${cellHash(a)} ${a.rng} ${strikes}`, "9e5b6275 507542730 23");
  });
});

test("mixed quick weather over many stretches matches the C# golden", () => {
  withRules(QUICK, () => {
    const w = field(8);
    run(w, 1200);
    const kinds = Array.from({ length: 17 }, (_, k) => kindOf(RULES.sim, 8, k)).join(" ");
    assert.equal(`${cellHash(w)} ${w.rng}`, "f89063c4 952317619", kinds);
  });
});
