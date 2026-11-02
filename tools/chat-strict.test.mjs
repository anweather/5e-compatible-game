// node --test tools/chat-strict.test.mjs. Each check, passing and failing, on the real rules.
import assert from "node:assert/strict";
import { test } from "node:test";
import { baseRules, compose, isHue, judge, slug } from "./chat-strict.mjs";

const rules = baseRules();
const BLUE = [0.25, 0.45, 1], GREEN = [0.2, 0.85, 0.3];
const fireSpells = rules.spells.filter(s => s.element === "fire");
const allFire = color => ({ elements: { fire: { look: { color } } }, spells: fireSpells.map(s => ({ ...s, look: { ...s.look, color } })) });
const on = (...patches) => compose(rules, patches.map((patch, i) => ({ id: `m${i}`, patch })));
const quiet = { rulesChanged: null, rowsChanged: null, skyChanged: null };
const says = (must, more) => judge(must, { patches: [], record: { tools: [], changes: [] }, watch: quiet, toolCalls: [], rules, ...more });

test("a colour needs the hue well ahead of the others", () => {
  assert.equal(isHue(BLUE, "blue"), true);
  assert.equal(isHue([0.5, 0.5, 0.6], "blue"), false);
  assert.equal(isHue(GREEN, "green"), true);
  assert.equal(isHue(BLUE, "green"), false);
});

test("all fire means every fire spell and the flames, not some", () => {
  assert.deepEqual(says({ fireIs: "blue" }, { world: on(allFire(BLUE)) }), []);
  const some = allFire(BLUE);
  some.spells = some.spells.slice(0, 4);
  assert.match(says({ fireIs: "blue" }, { world: on(some) })[0], /3 of 7 fire spells/);
  assert.match(says({ fireIs: "blue" }, { world: on({ spells: allFire(BLUE).spells }) })[0], /fire element/);
});

test("the follow-up must replace the colour on all of it", () => {
  const partly = allFire(GREEN);
  partly.spells = partly.spells.slice(0, 2);
  assert.deepEqual(says({ fireIs: "green" }, { world: on(allFire(BLUE), allFire(GREEN)) }), []);
  assert.match(says({ fireIs: "green" }, { world: on(allFire(BLUE), partly) })[0], /5 of 7/);
});

test("twice as fast has the right factor, and only on grass", () => {
  const grass = (c, fuel = 8) => ({ terrain: { grass: { burn: { catch: c, fuel } } } });
  assert.deepEqual(says({ grassBurns: 2 }, { world: on(grass(0.7)), patches: [grass(0.7)] }), []);
  assert.match(says({ grassBurns: 2 }, { world: on(grass(0.5)), patches: [grass(0.5)] })[0], /1\.43×/);
  assert.deepEqual(says({ grassBurns: 2 }, { world: on(grass(0.7, 4)), patches: [grass(0.7, 4)] }), [], "halved fuel is the same speed");
  assert.match(says({ grassBurns: 2 }, { world: on(grass(0.7, 2)), patches: [grass(0.7, 2)] })[0], /fuel/);
  assert.match(says({ grassBurns: 2 }, { world: on(grass(0.35, 4)), patches: [grass(0.35, 4)] })[0], /1\.00×/);
  assert.match(says({ grassBurns: 2 }, { world: on(grass(0.7)), patches: [{ sim: {} }] })[0], /the sim/);
});

test("a cantrip is level 0, thrown, small and really casts", () => {
  const must = { cantrip: { element: "cold", mostRadius: 2 } };
  const spell = { id: "frost-ball", element: "cold", level: 0, shape: "burst", radius: 1, look: { family: "blast" } };
  const good = { selected: true, inTheList: true, onTheWheel: true, logged: true, frozen: 5 };
  assert.deepEqual(says(must, { world: on({ spells: [spell] }), cast: null }), []);
  assert.deepEqual(says(must, { world: on({ spells: [spell] }), cast: good }), []);
  assert.match(says(must, { world: on({ spells: [{ ...spell, level: 1 }] }), cast: null })[0], /not a cantrip/);
  assert.match(says(must, { world: on({ spells: [{ ...spell, shape: "line", origin: "self" }] }), cast: null })[0], /not thrown/);
  assert.match(says(must, { world: on({ spells: [spell] }), cast: { ...good, frozen: 0 } })[0], /froze nothing/);
  assert.match(says(must, { world: on(), cast: null })[0], /no new spell/);
});

test("a house has walls that close, a door, a roof, and stands by the village", () => {
  const must = { house: { near: [198, 287, 234, 324], within: 12, mostSide: 8 } };
  const row = {
    at: [209, 283],
    joins: { nw: [0, 0], ne: [10, 0], se: [10, 8], sw: [0, 8] },
    walls: { n: { from: "nw", to: "ne" }, e: { from: "ne", to: "se" }, s: { from: "se", to: "sw", openings: [{ kind: "door", at: 4 }] }, w: { from: "sw", to: "nw" } },
    roofs: { main: { over: ["nw", "ne", "se", "sw"] } },
  };
  const house = r => ({ world: { buildings: { x: r } } });
  const watch = { ...quiet, rowsChanged: 8000 };
  const check = r => says(must, { world: on(house(r)), patches: [house(r)], watch });
  assert.deepEqual(check(row), []);
  assert.match(check({ ...row, at: [100, 100] })[0], /off the village/);
  assert.match(check({ ...row, walls: { ...row.walls, s: { from: "se", to: "sw" } } })[0], /no door/);
  assert.match(check({ ...row, roofs: {} })[0], /roof/);
  assert.match(says(must, { world: on(house(row)), patches: [house(row)], watch: quiet })[0], /no row/);
});

test("a refusal offers the nearest thing and changes nothing", () => {
  const record = (o = {}) => ({ outcome: "refused", reply: "No, but try Shapeshift.", changes: [], tools: [{ ok: true, name: "outside_rules" }], ...o });
  const must = { refused: "Shapeshift" };
  assert.deepEqual(says(must, { world: on(), record: record() }), []);
  assert.match(says(must, { world: on(), record: record({ tools: [] }) })[0], /outside_rules/);
  assert.match(says(must, { world: on(), record: record({ reply: "No." }) })[0], /offer/);
  const changed = record({ tools: [{ ok: true, name: "outside_rules" }, { ok: true, name: "apply" }], changes: [{}] });
  assert.match(says(must, { world: on(), record: changed }).join(), /used apply/);
});

test("a weather answer says what the tool said, and rain has to be set and show", () => {
  const asked = [{ name: "weather", answer: "Weather: clear, as the world has it", error: false }];
  const record = reply => ({ reply, tools: [], changes: [] });
  assert.deepEqual(says({ weatherAnswer: true }, { world: on(), record: record("It's clear."), toolCalls: asked }), []);
  assert.match(says({ weatherAnswer: true }, { world: on(), record: record("It's raining."), toolCalls: asked })[0], /tool said clear/);
  const rain = [{ name: "set_weather", input: { weather: "rain" }, error: false }];
  assert.deepEqual(says({ weatherIs: "rain" }, { world: on(), toolCalls: rain, watch: { ...quiet, skyChanged: 2500 } }), []);
  assert.match(says({ weatherIs: "rain" }, { world: on(), toolCalls: rain })[0], /sky/);
});

test("a reverted mod's id is its title's slug", () => assert.equal(slug("Blue fire everywhere"), "blue-fire-everywhere"));
