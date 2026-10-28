// Components (rules/components.json): what entities and items are made of,
// entity rules that match on them ("has" / "lacks"), the old flammable /
// creature keys, and the checks. The golden and the error list are shared with
// the C# port's ComponentTests (unity/Assets/Prototype/Chemistry/Tests/ComponentTests.cs).
import { test } from "node:test";
import assert from "node:assert/strict";
import { BASE_RULES, RULES, componentNames, composeRules, entityCondHolds, hasComponent, installRules, validateRules, type ModPatch, type Rules } from "../src/rules.ts";
import { castSpell } from "../src/actions.ts";
import { cellAt, createWorld } from "../src/world.ts";

const fnv = (s: string) => {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 0x01000193);
  return (h >>> 0).toString(16);
};

const withRules = (patch: ModPatch, body: () => void) => {
  const before = composeRules([]);
  installRules(composeRules([patch]));
  try { body(); } finally { installRules(before); }
};

/** Every thing's component bits, bit i the i-th sorted name: ComponentBitsMatchTheSandbox in C#. */
function componentGolden(r: Rules): string {
  const names = componentNames(r);
  const bits = (def: { components?: string[]; flammable?: boolean; creature?: boolean }) =>
    names.reduce((b, n, i) => (hasComponent(def, n) ? b | (1n << BigInt(i)) : b), 0n).toString();
  const table = (t: Record<string, { components?: string[] }>) => Object.keys(t).sort().map((k) => `${k}:${bits(t[k]!)}`).join(";");
  return fnv(`${names.join(",")}|${table(r.entities)}|${table(r.items)}`);
}

test("things carry their components, and the bits match the C# port", () => {
  assert.deepEqual(validateRules(BASE_RULES as Rules), []);
  assert.equal(componentGolden(BASE_RULES as Rules), "92654856");
  const e = BASE_RULES.entities;
  assert.ok(hasComponent(e.crate!, "flammable") && hasComponent(e.crate!, "breakable"));
  assert.ok(!hasComponent(e.boulder!, "flammable") && hasComponent(e.boulder!, "heavy"));
  assert.ok(hasComponent(BASE_RULES.items["bullseye-lantern"]!, "brittle"));
  assert.deepEqual(Object.keys(e).filter((k) => hasComponent(e[k]!, "creature")).sort(), ["ash-crab", "bone-hound", "giant-spider", "goblin", "wizard"]);
});

test("the old flammable and creature keys still load", () => {
  const r = composeRules([{ entities: { hay: { name: "Hay bale", hp: 2, mass: 1, flammable: true }, imp: { name: "Imp", hp: 4, mass: 1, creature: true, components: ["floats"] } } }]);
  assert.deepEqual(validateRules(r), []);
  assert.ok(hasComponent(r.entities.hay!, "flammable"));
  assert.ok(hasComponent(r.entities.imp!, "creature") && hasComponent(r.entities.imp!, "floats"));
  const def = r.entities.crate!;
  assert.ok(entityCondHolds({ flammable: true }, def, {}) && !entityCondHolds({ flammable: false }, def, {}));
});

test("entity rules match on components, not ids", () => {
  const burning = () => {
    const w = createWorld(20, 9, 3);
    for (let x = 0; x < 20; x++) for (let y = 0; y < 9; y++) cellAt(w, x, y)!.t = "stone";
    w.entities.push({ id: w.nextId++, kind: "giant-spider", x: 8, y: 4, hp: 99, status: {}, maxHp: 99, wait: 0 });
    castSpell(w, "fire-bolt", 8, 4, 1);
    return w.entities[0]!.status.burning ?? 0;
  };
  assert.equal(burning(), 9, "a spider isn't flammable: it burns as a creature does");
  const spider = (components: string[]) => ({ "giant-spider": { ...RULES.entities["giant-spider"]!, components } });
  withRules({ entities: spider(["creature", "flammable"]) }, () => assert.equal(burning(), 24, "given the flammable component, the flammable rule takes it"));
  withRules({
    components: { furred: "Has fur, which burns long." },
    entities: spider(["creature", "furred"]),
    reactions: { addEntity: [{ id: "e-fire-fur", element: "fire", if: { has: ["creature", "furred"] }, do: [{ status: { burning: 40 } }], stop: true }] },
  }, () => assert.equal(burning(), 40, "a mod's own component, matched by a mod's own rule"));
});

test("the checks name unknown components (the same list as the C# port)", () => {
  const r = composeRules([{
    components: { shiny: "" },
    entities: { imp: { name: "Imp", components: ["creature", "glowing"] }, elf: { name: "Elf", components: "creature" } },
    items: { rock: { name: "Rock", components: ["sharp"] } },
    reactions: { addEntity: [{ id: "e-x", element: "fire", if: { has: "wooden", lacks: [3] }, do: [] }] },
  } as unknown as ModPatch]);
  assert.deepEqual(validateRules(r).sort(), [
    "rule e-x: unknown component wooden",
    "rule e-x: lacks must be a component name or a list of them",
    "entity imp: unknown component glowing",
    "entity elf: components must be a list of component names",
    "item rock: unknown component sharp",
    "component shiny: needs a description",
  ].sort());
  const many = Object.fromEntries(Array.from({ length: 60 }, (_, i) => [`c${i}`, "x"]));
  assert.ok(validateRules(composeRules([{ components: many }])).includes(`components: at most 64, not ${componentNames(BASE_RULES as Rules).length + 60}`));
});
