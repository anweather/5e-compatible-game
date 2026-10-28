// Mirrors LiveRulesTests' verb-group tests (docs/MATERIALS.md, step 1): materials spelled as verb
// groups read exactly as the flat keys, and an unknown key in a group is reported alike.
import { test } from "node:test";
import assert from "node:assert/strict";
import materials from "../../rules/materials.json" with { type: "json" };
import { BASE_RULES, composeRules, validateRules } from "../src/rules.ts";
import { groupMaterial, ungroupMaterials } from "../src/materials.ts";

const sortTags = (e: Record<string, unknown>) => ({ ...e, tags: [...((e.tags as string[]) ?? [])].sort() });

test("materials spelled as verb groups read the same", () => {
  const grouped = {
    terrain: Object.fromEntries(Object.entries(materials.terrain).map(([k, v]) => [k, groupMaterial(v as never, false)])),
    covers: Object.fromEntries(Object.entries(materials.covers).map(([k, v]) => [k, groupMaterial(v as never, true)])),
  };
  assert.ok(Object.values(grouped.terrain).some((t) => "burn" in t), "some terrain is spelled with a burn group");
  ungroupMaterials(grouped);
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
