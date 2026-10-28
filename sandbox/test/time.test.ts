// The Time & Weather spells: Hasten Time runs the day clock faster, Control
// Weather holds a weather, Fog Cloud lays fog that hides what's behind it.
// Mirrors the C# port's TimeWeatherTests
// (unity/Assets/Prototype/Chemistry/Tests/TimeWeatherTests.cs), golden included.
import { test } from "node:test";
import assert from "node:assert/strict";
import { RULES } from "../src/rules.ts";
import { step } from "../src/sim.ts";
import { castSpell, spellById, spellCells } from "../src/actions.ts";
import { minutesAt, weatherAt } from "../src/sky.ts";
import { type World, cellAt, clockTick, createWorld, deserializeWorld, fogged, hashWorld, lineOfSight, serializeWorld } from "../src/world.ts";

const run = (w: World, ticks: number) => { for (let i = 0; i < ticks; i++) step(w); };
const minutes = (w: World) => minutesAt(RULES.sim, clockTick(w));

/** World.CellHash in the C# port. */
function cellHash(w: World): string {
  const s = w.cells.map((c) =>
    `${c.t},${c.water},${c.frozen ? 1 : 0},${c.cover ?? ""},${c.fire},${c.fuel},${c.gas ?? ""},${c.gasAmt},${c.charge}${c.h ? `,h${c.h}` : ""};`).join("");
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 0x01000193);
  return (h >>> 0).toString(16);
}

/** World.SkyState in the C# port (WeatherKind's order: clear cloudy windy fog rain storm snow). */
const KINDS = ["clear", "cloudy", "windy", "fog", "rain", "storm", "snow"];
const skyState = (w: World) =>
  `${w.clockAhead}/${w.haste ? `${w.haste.rate}x${w.haste.ticks}` : "-"}/${w.weatherSet ? `${KINDS.indexOf(w.weatherSet.kind)}:${w.weatherSet.from}:${w.weatherSet.until}` : "-"}`;

/** A dirt field walled in bedrock (TimeWeatherTests.Field). */
function field(width = 32, height = 24): World {
  const w = createWorld(width, height, 7);
  for (let x = 0; x < width; x++)
    for (let y = 0; y < height; y++)
      if (x === 0 || y === 0 || x === width - 1 || y === height - 1) cellAt(w, x, y)!.t = "bedrock";
  return w;
}

const fogUnits = (w: World) => w.cells.filter((c) => c.gas === "fog").reduce((n, c) => n + c.gasAmt, 0);

test("Hasten Time runs the clock twenty times as fast for half a minute, then at its own pace", () => {
  const w = field();
  const start = minutes(w);
  castSpell(w, "hasten-time", 5, 5, 1);
  assert.deepEqual(w.haste, { rate: 20, ticks: 180 });
  run(w, 180);
  assert.equal(minutes(w) - start, 600, "ten game hours in 30 s");
  assert.equal(w.haste, null);
  assert.equal(w.clockAhead, 180 * 19);
  const then = minutes(w);
  run(w, 60);
  assert.equal(minutes(w) - then, 10);
});

test("a held Hasten Time runs longer, and casting it again settles the clock", () => {
  const w = field();
  castSpell(w, "hasten-time", 5, 5, 1, undefined, 3);
  assert.deepEqual(w.haste, { rate: 20, ticks: 360 });
  run(w, 30);
  castSpell(w, "hasten-time", 5, 5, 1);
  assert.equal(w.haste, null);
  assert.equal(w.clockAhead, 30 * 19, "the hours gained stay");
});

test("the time spells touch no squares", () => {
  const w = field();
  assert.deepEqual(spellCells(w, spellById("hasten-time")!, 5, 5, 1), []);
  assert.deepEqual(spellCells(w, spellById("control-weather")!, 5, 5, 1), []);
  const before = cellHash(w);
  castSpell(w, "hasten-time", 5, 5, 1);
  castSpell(w, "control-weather", 5, 5, 1);
  assert.equal(cellHash(w), before);
});

test("Control Weather steps through clear, rain, snow and storm", () => {
  const w = field();
  run(w, 1);
  const seen: string[] = [];
  for (let i = 0; i < 5; i++) {
    castSpell(w, "control-weather", 5, 5, 1);
    run(w, 200);
    seen.push(w.weather.kind);
  }
  assert.deepEqual(seen, ["rain", "snow", "storm", "clear", "rain"]);
  assert.equal(w.weatherSet!.until - w.weatherSet!.from, 720);
});

test("held weather ramps in, rains even in the calm, then the natural weather resumes", () => {
  const w = field();
  castSpell(w, "control-weather", 5, 5, 1);
  run(w, 1);
  assert.equal(w.weather.kind, "rain");
  assert.ok(w.weather.strength < 5);
  run(w, 600);
  assert.ok(w.cells.some((c) => c.water > 0));
  run(w, 120);
  assert.equal(w.weatherSet, null);
  const natural = weatherAt(RULES.sim, w.seed, w.tick);
  assert.deepEqual([w.weather.kind, w.weather.strength], [natural.kind, natural.strength]);
});

test("Fog Cloud lays thick fog that hides what's behind it", () => {
  const w = field();
  assert.ok(lineOfSight(w, 3, 10, 20, 10));
  castSpell(w, "fog-cloud", 11, 10, 1);
  assert.equal(cellAt(w, 11, 10)!.gasAmt, 6);
  assert.equal(w.cells.filter((c) => c.gas === "fog").length, 81);
  assert.ok(!lineOfSight(w, 3, 10, 20, 10), "fog hides the far side");
  assert.ok(!lineOfSight(w, 3, 10, 20, 10, true), "fogOnly sees the fog too");
  assert.ok(lineOfSight(w, 3, 3, 20, 3));
  cellAt(w, 9, 10)!.gasAmt = 1;
  assert.ok(!fogged(cellAt(w, 9, 10)!));
});

test("fog lingers, then thins away", () => {
  const w = field();
  castSpell(w, "fog-cloud", 11, 10, 1);
  const start = fogUnits(w);
  run(w, 60);
  assert.ok(fogUnits(w) > (start * 3) / 5);
  run(w, 1200);
  assert.equal(fogUnits(w), 0);
});

test("fire burns the fog off, and a gust blows it away", () => {
  const w = field();
  castSpell(w, "fog-cloud", 11, 10, 1);
  castSpell(w, "fireball", 11, 10, 1);
  assert.equal(cellAt(w, 11, 10)!.gas, null);
  assert.equal(cellAt(w, 15, 10)!.gas, "fog");

  const g = field();
  castSpell(g, "fog-cloud", 8, 10, 1);
  const meanX = () => {
    let sum = 0, n = 0;
    g.cells.forEach((c, i) => { if (c.gas === "fog") { sum += (i % g.w) * c.gasAmt; n += c.gasAmt; } });
    return sum / n;
  };
  const before = meanX();
  castSpell(g, "gust-of-wind", 2, 10, 1);
  run(g, 18);
  assert.ok(meanX() - before > 3, `the fog's middle moved from ${before} to ${meanX()}`);
});

test("the time spells' state survives a snapshot, and the same casts give the C# port's world", () => {
  const scenario = () => {
    const w = field();
    castSpell(w, "hasten-time", 5, 5, 1, undefined, 2);
    castSpell(w, "control-weather", 5, 5, 1);
    castSpell(w, "fog-cloud", 11, 10, 1);
    run(w, 40);
    castSpell(w, "control-weather", 5, 5, 1);
    castSpell(w, "fire-bolt", 12, 10, 1);
    castSpell(w, "gust-of-wind", 3, 12, 1);
    return w;
  };
  const a = scenario(), b = deserializeWorld(serializeWorld(scenario()));
  assert.equal(skyState(b), skyState(a), "a late joiner's snapshot carries the clock and the held weather");
  run(a, 260);
  run(b, 260);
  assert.equal(hashWorld(a), hashWorld(b));
  // Goldens shared with TimeWeatherTests.TheTimeSpellsAreDeterministicAndMatchTheSandbox.
  assert.equal(skyState(a), "5130/-/6:40:760");
  assert.equal(cellHash(a), "a754b5cb"); // night (day.nightFreeze) since 2026-10-27
});
