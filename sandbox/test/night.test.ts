// What night does in the rules (night.ts, tickWeather in sim.ts): the daylight
// by the hour, the light fires shed, ice keeping at night, critters by the hour
// and the checks. The light golden is shared with the C# port's NightTests
// (unity/Assets/Prototype/Chemistry/Tests/NightTests.cs).
import { test } from "node:test";
import assert from "node:assert/strict";
import { RULES, composeRules, validateRules } from "../src/rules.ts";
import { step } from "../src/sim.ts";
import { BRIGHT, DARK, DIM, inHours, ticksOf } from "../src/sky.ts";
import { daylightNow, fillLight, isNight, lightAt } from "../src/night.ts";
import { type World, cellAt, createWorld, hashWorld } from "../src/world.ts";

const fnv = (s: string) => {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 0x01000193);
  return (h >>> 0).toString(16);
};

/** Runs the world's clock on to this hour of the first day (NightTests.SetHour in C#). */
function setHour(w: World, hour: number) {
  const day = ticksOf(RULES.sim, RULES.sim.day.lengthMinutes);
  w.clockAhead = Math.trunc(((hour - RULES.sim.day.startHour) * day) / 24);
}

/** A field of dirt walled with bedrock (ChemistryTests.Room in C#). */
function room(w: number, h: number, seed = 7): World {
  const world = createWorld(w, h, seed);
  for (let x = 0; x < w; x++) for (let y = 0; y < h; y++) {
    if (x === 0 || y === 0 || x === w - 1 || y === h - 1) cellAt(world, x, y)!.t = "bedrock";
  }
  return world;
}

test("the daylight follows the clock", () => {
  const w = room(12, 8);
  const day: [number, number][] = [[9, BRIGHT], [17.9, BRIGHT], [18.5, DIM], [21, DARK], [27, DARK], [29.5, DIM], [31.5, BRIGHT]];
  for (const [hour, light] of day) {
    setHour(w, hour);
    assert.equal(daylightNow(w), light, `at ${hour}:00`);
    assert.equal(isNight(w), light === DARK);
  }
  assert.ok(inHours(18, 8, 23 * 60) && inHours(18, 8, 7 * 60) && !inHours(18, 8, 12 * 60), "across midnight");
  assert.ok(inHours(6, 19, 6 * 60) && !inHours(6, 19, 19 * 60), "from the first hour up to the second");
});

/** A dark field with two fires, as NightTests builds it. */
function fireField(): World {
  const w = room(30, 20);
  setHour(w, 23);
  cellAt(w, 6, 6)!.fire = 3;
  cellAt(w, 20, 12)!.fire = 1;
  return w;
}

test("fires light the night, and the light matches the C# port", () => {
  const w = fireField();
  assert.equal(lightAt(w, 6, 6), BRIGHT);
  assert.equal(lightAt(w, 10, 10), BRIGHT, "20 ft bright: four cells, diagonals too");
  assert.equal(lightAt(w, 11, 6), DIM);
  assert.equal(lightAt(w, 14, 6), DIM, "20 ft dim beyond");
  assert.equal(lightAt(w, 15, 1), DARK, "nine cells from one fire, eleven from the other");
  const map = fillLight(w, 0, 0, w.w, w.h);
  for (let y = 0; y < w.h; y++) for (let x = 0; x < w.w; x++) assert.equal(map[y * w.w + x], lightAt(w, x, y));
  assert.equal(fnv(map.join("")), "966c8aa9", "the same golden as NightTests");
  setHour(w, 12);
  assert.equal(lightAt(w, 15, 1), BRIGHT, "by day it's all bright");
  const a = fireField(), b = fireField();
  lightAt(a, 3, 3);
  assert.equal(hashWorld(a), hashWorld(b), "light is never kept: the world doesn't change");
});

/** Frozen puddles under a clear sky held from the start, at an hour (NightTests.IceLeft). */
function iceLeft(hour: number): number {
  const w = room(20, 12);
  setHour(w, hour);
  for (let x = 3; x < 17; x += 2) { const c = cellAt(w, x, 5)!; c.water = 1; c.frozen = true; }
  w.weatherSet = { kind: "clear", from: 0, until: 100000 };
  for (let t = 0; t < 1500; t++) step(w);
  let n = 0;
  for (let x = 0; x < 20; x++) if (cellAt(w, x, 5)!.frozen) n++;
  return n;
}

test("ice keeps through the night and thaws by day", () => {
  assert.equal(iceLeft(22), 7, "a clear night leaves the ice till morning");
  assert.ok(iceLeft(10) < 7, "a clear day thaws it");
});

test("the rime moth is out only from dusk to morning", () => {
  const frozenAround = (hour: number) => {
    const w = room(9, 9);
    setHour(w, hour);
    for (let x = 1; x < 8; x++) for (let y = 1; y < 8; y++) cellAt(w, x, y)!.water = 1;
    w.critters.push({ id: w.nextId++, kind: "rime-moth", x: 4, y: 4, timer: 1, dir: 0 });
    step(w);
    return cellAt(w, 4, 4)!.frozen;
  };
  assert.equal(frozenAround(23), true, "out at night: its cold freezes the water");
  assert.equal(frozenAround(12), false, "asleep at noon");
});

test("the hours are checked", () => {
  const r = composeRules([{ critters: [{ id: "owl", name: "Owl", glyph: "o", color: "#ffffff", mode: "emit", element: "air", hours: "night", blurb: "" } as never] }]);
  assert.deepEqual(validateRules(r), ["critter owl: hours must be [from, to], hours from 0 to 24"]);
});
