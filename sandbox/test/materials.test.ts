// Mirrors LiveRulesTests' verb-group tests (docs/MATERIALS.md, step 1): materials spelled as verb
// groups read exactly as the flat keys, and an unknown key in a group is reported alike.
import { test } from "node:test";
import assert from "node:assert/strict";
import materials from "../../rules/materials.json" with { type: "json" };
import { BASE_RULES, composeRules, validateRules } from "../src/rules.ts";
import { groupMaterial, ungroupMaterials } from "../src/materials.ts";

// materials.json's terrain and covers, ungrouped (the old flat spelling).
const flat = () => {
  const m = JSON.parse(JSON.stringify(materials)) as { terrain: Record<string, Record<string, unknown>>; covers: Record<string, Record<string, unknown>> };
  ungroupMaterials(m);
  return m;
};

const sortTags = (e: Record<string, unknown>) => ({ ...e, tags: [...((e.tags as string[]) ?? [])].sort() });

test("materials spelled as verb groups read the same as spelled flat", () => {
  const f = flat();
  // Grouping the flat spelling again gives the committed file back.
  for (const [id, t] of Object.entries(f.terrain)) assert.deepEqual(sortTags(groupMaterial(t as never, false)), sortTags((materials.terrain as Record<string, Record<string, unknown>>)[id]!), id);
  for (const [id, c] of Object.entries(f.covers)) assert.deepEqual(sortTags(groupMaterial(c as never, true)), sortTags((materials.covers as Record<string, Record<string, unknown>>)[id]!), id);
  const grouped = f;
  for (const [id, t] of Object.entries(BASE_RULES.terrain)) assert.deepEqual(sortTags(grouped.terrain[id]!), sortTags(t as never), id);
  for (const [id, c] of Object.entries(BASE_RULES.covers)) assert.deepEqual(sortTags(grouped.covers[id]!), sortTags(c as never), id);
});

test("an unknown key in a verb group is reported", () => {
  const bad = composeRules([{ terrain: { mud: { name: "Mud", tags: ["ground"], burn: { catch: 0.4, fuel: 9, smoulder: 3 } } } } as never]);
  assert.deepEqual(validateRules(bad), ["terrain mud: burn.smoulder unknown"]);
  const ok = composeRules([{ terrain: { mud: { name: "Mud", tags: ["ground"], burn: { catch: 0.4, fuel: 9 }, soak: { dry: 30 } } } } as never]);
  const mud = ok.terrain.mud!;
  assert.deepEqual([mud.tags.includes("flammable"), mud.flammability, mud.fuel, mud.soakEvery], [true, 0.4, 9, 30]);
});
