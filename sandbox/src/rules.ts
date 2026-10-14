// Typed view of ../../rules/*.json. The JSON is the asset: the future C#
// Game.Core loads the same files, so nothing about a rule lives here.
import materials from "../../rules/materials.json" with { type: "json" };
import reactions from "../../rules/reactions.json" with { type: "json" };
import spells from "../../rules/spells.json" with { type: "json" };
import critters from "../../rules/critters.json" with { type: "json" };
import entities from "../../rules/entities.json" with { type: "json" };
import machines from "../../rules/machines.json" with { type: "json" };
import sim from "../../rules/sim.json" with { type: "json" };

export interface TerrainDef {
  name: string;
  tags: string[];
  flammability?: number;
  fuel?: number;
  burnsTo?: string;
  breaksTo?: string;
  elevation?: number;
  burnLog?: string;
}
export interface CoverDef {
  name: string;
  tags: string[];
  flammability?: number;
  fuel?: number;
  burnIntensity?: number;
}
export interface GasDef {
  name: string;
  scald?: number;
}

export interface CellCond {
  cover?: string[];
  fire?: boolean | number;
  fireBelow?: number;
  water?: boolean;
  frozen?: boolean;
  gas?: string;
  terrainTag?: string;
  notTerrainTag?: string;
  conductive?: boolean;
}
export interface EntityCond {
  status?: string;
  flammable?: boolean;
}

/** One effect object; exactly one of the verb keys is set. */
export interface Effect {
  ignite?: number | "cover";
  boil?: number;
  melt?: boolean;
  freeze?: boolean;
  extinguish?: boolean;
  condense?: boolean;
  addWater?: number | "power";
  setCover?: string | null;
  spreadCover?: number;
  break?: boolean;
  setTerrain?: string;
  raise?: string;
  conduct?: number;
  fan?: boolean;
  pushGas?: boolean;
  clearGas?: boolean;
  mark?: string;
  gas?: string;
  n?: number;
  status?: Record<string, number>;
  clear?: string;
  damageMul?: number;
}
export interface CellRule {
  id: string;
  element: string;
  if?: CellCond;
  do: Effect[];
  log?: string;
  notable?: boolean;
  stop?: boolean;
}
export interface EntityRule {
  id: string;
  element: string;
  if?: EntityCond;
  do: Effect[];
  log?: string;
  notable?: boolean;
  stop?: boolean;
}

export type Shape = "point" | "line" | "square" | "burst" | "target";
export interface SpellDef {
  id: string;
  name: string;
  level: number;
  shape: Shape;
  element?: string;
  power?: number;
  damage?: [number, number];
  length?: number;
  size?: number;
  radius?: number;
  push?: number;
  wind?: number;
  effect?: "levitate" | "carry";
  duration?: number;
  maxMass?: number;
  range?: number;
  blurb: string;
}
export interface CritterDef {
  id: string;
  name: string;
  glyph: string;
  color: string;
  mode: "emit" | "act";
  element: string;
  power?: number;
  every?: number;
  radius?: number;
  length?: number;
  directional?: boolean;
  blurb: string;
}
export interface EntityDef {
  name: string;
  hp: number;
  mass: number;
  flammable: boolean;
  creature?: boolean;
  explodes?: { cover: string; radius: number; fire: number; push: number; damage: [number, number] };
}
export interface MachineDef {
  name: string;
  needs: "steam" | "mass" | "flow" | "charge" | "wind";
  build?: number;
  max?: number;
  decayEvery?: number;
  mass?: number;
  hold?: number;
  how: string;
}
export interface SimParams {
  ticksPerSecond: number;
  fire: { spreadChance: number; heatChance: number; boilChance: number; downwindBonus: number; smokeChance: number };
  water: { maxLevel: number; unitsPerStep: number };
  gas: { decayChance: number; maxAmount: number };
  status: { burnDamageEvery: number; scaldEvery: number };
  push: { slideCells: number; collisionDamage: number };
  fallDamage: number;
  zapDamage: number;
  conductLimit: number;
}

export interface Rules {
  terrain: Record<string, TerrainDef>;
  covers: Record<string, CoverDef>;
  gases: Record<string, GasDef>;
  cellRules: CellRule[];
  entityRules: EntityRule[];
  spells: SpellDef[];
  critters: CritterDef[];
  entities: Record<string, EntityDef>;
  machines: Record<string, MachineDef>;
  sim: SimParams;
}

export const RULES: Rules = {
  terrain: materials.terrain as Record<string, TerrainDef>,
  covers: materials.covers as Record<string, CoverDef>,
  gases: materials.gases as Record<string, GasDef>,
  cellRules: reactions.cell as CellRule[],
  entityRules: reactions.entity as EntityRule[],
  spells: spells as SpellDef[],
  critters: critters as CritterDef[],
  entities: entities as unknown as Record<string, EntityDef>,
  machines: machines as Record<string, MachineDef>,
  sim: sim as SimParams,
};

export const ELEMENTS = ["fire", "cold", "water", "air", "earth", "lightning", "grease", "oil", "thunder"] as const;

/** Cross-checks the tables against each other. Empty means consistent. */
export function validateRules(r: Rules): string[] {
  const errors: string[] = [];
  const el = new Set<string>(ELEMENTS);
  const terrainIds = new Set(Object.keys(r.terrain));
  const coverIds = new Set(Object.keys(r.covers));
  for (const [id, t] of Object.entries(r.terrain)) {
    if (t.burnsTo && !terrainIds.has(t.burnsTo)) errors.push(`terrain ${id}: burnsTo ${t.burnsTo} unknown`);
    if (t.breaksTo && !terrainIds.has(t.breaksTo)) errors.push(`terrain ${id}: breaksTo ${t.breaksTo} unknown`);
    if (t.tags.includes("flammable") && !(t.fuel && t.flammability)) errors.push(`terrain ${id}: flammable without fuel/flammability`);
  }
  for (const rule of [...r.cellRules, ...r.entityRules]) {
    if (!el.has(rule.element)) errors.push(`rule ${rule.id}: unknown element ${rule.element}`);
    for (const e of rule.do) {
      if (e.setTerrain && !terrainIds.has(e.setTerrain)) errors.push(`rule ${rule.id}: setTerrain ${e.setTerrain} unknown`);
      if (e.raise && !terrainIds.has(e.raise)) errors.push(`rule ${rule.id}: raise ${e.raise} unknown`);
      if (typeof e.setCover === "string" && !coverIds.has(e.setCover)) errors.push(`rule ${rule.id}: cover ${e.setCover} unknown`);
      if (e.gas && !(e.gas in r.gases)) errors.push(`rule ${rule.id}: gas ${e.gas} unknown`);
    }
    const c = (rule as CellRule).if;
    for (const cv of c?.cover ?? []) if (!coverIds.has(cv)) errors.push(`rule ${rule.id}: cover ${cv} unknown`);
  }
  for (const s of r.spells) if (s.element && !el.has(s.element)) errors.push(`spell ${s.id}: unknown element ${s.element}`);
  for (const c of r.critters) if (!el.has(c.element)) errors.push(`critter ${c.id}: unknown element ${c.element}`);
  for (const id of Object.keys(r.machines)) {
    if (!terrainIds.has(id)) errors.push(`machine ${id}: no terrain with that id`);
    else if (!r.terrain[id]!.tags.includes("machine")) errors.push(`machine ${id}: terrain lacks the machine tag`);
  }
  for (const [id, e] of Object.entries(r.entities)) {
    if (e.explodes && !coverIds.has(e.explodes.cover)) errors.push(`entity ${id}: explodes into unknown cover`);
  }
  return errors;
}
