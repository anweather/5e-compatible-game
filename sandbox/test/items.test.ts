// items.json: the sandbox has no items on its board, but it carries the table
// through mods and checks it with the same wording as the C# (Rules.CheckItems.cs),
// so a patch either engine accepts, the other accepts too.
import { test } from "node:test";
import assert from "node:assert/strict";
import { BASE_RULES, composeRules, validateRules, type ItemDef } from "../src/rules.ts";

const errorsWith = (items: Record<string, unknown>) => validateRules(composeRules([{ items: items as Record<string, ItemDef> }]));

test("the base items are consistent and a mod can replace one by id", () => {
  assert.deepEqual(validateRules(composeRules([])), []);
  assert.ok(Object.keys(BASE_RULES.items).length >= 5);
  const r = composeRules([{ items: { spyglass: { name: "Long spyglass", use: "zoom", zoom: 4 } } }]);
  assert.equal(r.items.spyglass!.zoom, 4);
  assert.equal(BASE_RULES.items.spyglass!.zoom, 2, "the base rules are untouched");
});

test("a broken item is refused with the C# wording", () => {
  assert.deepEqual(errorsWith({
    crate: { name: "Crate", mass: 0 },
    torch: { name: "", use: "light", light: { bright: -1, color: "orange", cone: 200 } },
    flask: { name: "Flask", breaks: { spill: { cover: "lava", radius: -2 } } },
    stone: { name: "Stone", use: "send" },
    glass: { name: "Glass", use: "zoom", zoom: 1 },
    mote: { name: "Mote", use: "point", reach: "far" },
    wand: { name: "Wand", use: "wave", light: 3 },
  }), [
    "item crate: id is taken by an entity",
    "item crate: mass must be a number above 0",
    "item torch: needs a name",
    "item torch: light.bright must be a number from 0",
    "item torch: light.cone must be a number from 0 to 179",
    "item torch: light.color must be #rrggbb",
    "item flask: breaks.speed must be a number above 0",
    "item flask: breaks.spill.cover lava unknown",
    "item flask: breaks.spill.radius must be a number from 0",
    "item stone: use send needs a message",
    "item glass: use zoom needs a zoom above 1",
    "item mote: reach must be a number from 0",
    "item mote: use point needs a light",
    "item mote: use point needs a reach above 0",
    "item wand: light must be an object",
    "item wand: unknown use wave",
  ]);
});
