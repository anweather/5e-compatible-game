// A material's settings grouped by verb (docs/MATERIALS.md, step 1): what happens to it when an effect
// reaches it. materials.json may spell a terrain or a cover either way, and composing the rules turns the
// groups into the flat keys the rest of the sandbox reads. Mirrors MaterialGroups.cs: the same verbs, the
// same keys, the same messages (a key a group doesn't know stays in the group for validateRules to report).

type Verb = { verb: string; tag: string | null; keys: [group: string, flat: string][] };

const TERRAIN_VERBS: Verb[] = [
  { verb: "burn", tag: "flammable", keys: [["catch", "flammability"], ["fuel", "fuel"], ["heat", "heat"], ["leaves", "burnsTo"], ["log", "burnLog"], ["look", "fireLook"]] },
  { verb: "break", tag: "breakable", keys: [["leaves", "breaksTo"]] },
  { verb: "dig", tag: null, keys: [["hardness", "hardness"], ["leaves", "dugTo"]] },
  { verb: "soak", tag: null, keys: [["dry", "soakEvery"], ["wet", "soakEveryWet"]] },
  { verb: "conduct", tag: "conductive", keys: [] },
  { verb: "grow", tag: null, keys: [["into", "growsTo"], ["log", "growLog"]] },
  { verb: "uproot", tag: null, keys: [["leaves", "uprootsTo"], ["log", "uprootLog"]] },
];

const COVER_VERBS: Verb[] = [
  { verb: "burn", tag: "flammable", keys: [["catch", "flammability"], ["fuel", "fuel"], ["intensity", "burnIntensity"], ["look", "fireLook"]] },
];

/** What water on it turns into: freeze {} for ice, boil {} for steam. */
const WATER_VERBS: [verb: string, water: string][] = [["freeze", "ice"], ["boil", "steam"]];

type Entry = Record<string, unknown> & { tags?: string[] };

/** Turns the grouped spelling of every terrain and cover into the flat keys, in place. */
export function ungroupMaterials(r: { terrain: Record<string, unknown>; covers: Record<string, unknown> }) {
  for (const t of Object.values(r.terrain)) ungroup(t as Entry, TERRAIN_VERBS, true);
  for (const c of Object.values(r.covers)) ungroup(c as Entry, COVER_VERBS, false);
}

function ungroup(entry: Entry, verbs: Verb[], water: boolean) {
  for (const { verb, tag, keys } of verbs) {
    const g = entry[verb];
    if (!g || typeof g !== "object" || Array.isArray(g)) continue;
    const left: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(g)) {
      const key = keys.find(([group]) => group === k);
      if (key) entry[key[1]] = v;
      else left[k] = v;
    }
    if (Object.keys(left).length > 0) entry[verb] = left;
    else delete entry[verb];
    if (tag) {
      entry.tags ??= [];
      if (!entry.tags.includes(tag)) entry.tags.push(tag);
    }
  }
  if (!water) return;
  for (const [verb, to] of WATER_VERBS) {
    if (entry[verb] === undefined) continue;
    delete entry[verb];
    entry.waterTo = to;
  }
}

/** The keys a group didn't know (left there by ungroupMaterials), as validateRules' first errors. */
export function materialGroupErrors(r: { terrain: Record<string, unknown>; covers: Record<string, unknown> }): string[] {
  const errors: string[] = [];
  const left = (what: string, entry: Entry, verbs: Verb[]) => {
    for (const { verb } of verbs) {
      const g = entry[verb];
      if (g && typeof g === "object" && !Array.isArray(g)) for (const k of Object.keys(g)) errors.push(`${what}: ${verb}.${k} unknown`);
    }
  };
  for (const [id, t] of Object.entries(r.terrain)) left(`terrain ${id}`, t as Entry, TERRAIN_VERBS);
  for (const [id, c] of Object.entries(r.covers)) left(`cover ${id}`, c as Entry, COVER_VERBS);
  return errors;
}

/** A flat terrain (or cover) entry spelled as groups: the inverse of ungrouping, for the parity test. */
export function groupMaterial(flat: Entry, cover: boolean): Entry {
  const entry: Entry = JSON.parse(JSON.stringify(flat));
  for (const { verb, tag, keys } of cover ? COVER_VERBS : TERRAIN_VERBS) {
    const tagged = tag !== null && (entry.tags ?? []).includes(tag);
    if (tag !== null && !tagged) continue;
    const g: Record<string, unknown> = {};
    for (const [group, f] of keys) {
      if (entry[f] === undefined) continue;
      g[group] = entry[f];
      delete entry[f];
    }
    if (tag === null && Object.keys(g).length === 0) continue;
    entry[verb] = g;
    if (tagged) entry.tags = entry.tags!.filter((t) => t !== tag);
  }
  if (!cover && entry.waterTo !== undefined) {
    const w = WATER_VERBS.find(([, to]) => to === entry.waterTo);
    if (w) {
      delete entry.waterTo;
      entry[w[0]] = {};
    }
  }
  return entry;
}
