// groups.json: the Unity game's spell radial elements. The sandbox has no
// radial, but it carries the table through mods and checks it with the same
// wording as the C# (SpellGroups.cs, RadialTests.cs), so both engines accept
// the same patches.
import { test } from "node:test";
import assert from "node:assert/strict";
import { BASE_RULES, composeRules, validateRules, type GroupDef, type SpellDef } from "../src/rules.ts";

test("every base spell is in a known group", () => {
  assert.deepEqual(Object.keys(BASE_RULES.groups), ["fire", "water", "storm", "earth", "arcane", "nature", "time"]);
  for (const s of BASE_RULES.spells) assert.ok(s.group && s.group in BASE_RULES.groups, s.id);
  assert.equal(BASE_RULES.spells.find((s) => s.id === "grease")!.group, "arcane");
});

test("a mod can add a group and regroup a spell", () => {
  const r = composeRules([{
    groups: { frost: { name: "Frost", order: 2.5, color: "#bfe6ff" } },
    spells: [{ id: "ray-of-frost", group: "frost", name: "Ray of Frost", level: 0, shape: "line", element: "cold", blurb: "" }],
  }]);
  assert.deepEqual(validateRules(r), []);
  assert.equal(r.groups.frost!.name, "Frost");
  assert.equal(r.spells.find((s) => s.id === "ray-of-frost")!.group, "frost");
  assert.equal(BASE_RULES.groups.frost, undefined, "the base rules are untouched");
});

test("unknown and broken groups are refused with the C# wording", () => {
  const spell = { id: "x", group: "nope", name: "X", level: 0, shape: "point", blurb: "" } as SpellDef;
  assert.deepEqual(validateRules(composeRules([{ spells: [spell] }])), ["spell x: unknown group nope"]);
  const bad = { order: "first", color: "red" } as unknown as GroupDef;
  assert.deepEqual(validateRules(composeRules([{ groups: { bad } }])), [
    "group bad: needs a name",
    "group bad: order must be a number",
    "group bad: color must be #rrggbb",
  ]);
});

// Mirrors LiveRulesTests.AThingsMaterialIsATerrainLikeTheSandbox.
test("a thing's material is a terrain", () => {
  const r = composeRules([{ entities: { crate: { name: "Crate", hp: 6, mass: 1, material: "cardboard" } }, items: { spyglass: { name: "Spyglass", mass: 0.45, material: "brass" } } } as never]);
  assert.deepEqual(validateRules(r), ["entity crate: material cardboard unknown", "item spyglass: material brass unknown"]);
});
