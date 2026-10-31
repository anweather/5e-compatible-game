// Casting by elements (docs/SPELL_COMBOS.md): each player loads one element or
// an ordered pair, a meter drains a tick at a time, and at zero the combo goes
// off and joins the player's grimoire. The d-pad's spells drain the same meter.
// C#: World.Combos.cs, line for line.
import { MAX_CHARGE } from "./actions.ts";
import { RULES, type ComboDef, type DpadSpellDef, type SpellDef } from "./rules.ts";
import { fnv1a, type PlayerCast, type World } from "./world.ts";

/** A player's cast input (C#: ComboCommand), stamped like a cast. */
export type ComboCommand =
  | { player: string; kind: "load"; element: string; hold?: boolean }
  | { player: string; kind: "cancel" }
  | { player: string; kind: "cycle"; step: number }
  | { player: string; kind: "castSelected" }
  | { player: string; kind: "release"; charge: number };

/** The id of an element alone (second null) or of an ordered pair. */
export const comboId = (first: string, second: string | null): string => (second === null ? first : `${first}+${second}`);

/** The combo with this id, or undefined. */
export function comboById(id: string): ComboDef | undefined {
  const [first, second] = id.split("+") as [string, string | undefined];
  return second === undefined ? RULES.combos.singles[first] : RULES.combos.pairs[first]?.[second];
}

/** The combo a cast has loaded, or undefined (nothing loaded, or a d-pad spell). */
const comboOf = (c: PlayerCast): ComboDef | undefined => (c.first === null ? undefined : comboById(comboId(c.first, c.second)));

/** Whether holding a cast makes this spell stronger: every spell does (C#: World.Chargeable). */
export const chargeable = (s: SpellDef | undefined): boolean => s !== undefined;

/** The most a combo can be charged: MAX_CHARGE if its spell is chargeable, else 1. */
export const maxChargeOf = (combo: ComboDef | undefined): number => (chargeable(RULES.spells.find((s) => s.id === combo?.spell)) ? MAX_CHARGE : 1);

/** The spell a cast of this charge goes off as: held at maxCharge, if the combo has one. */
export const spellAt = (combo: ComboDef, charge: number, maxCharge: number): string =>
  combo.held !== undefined && maxCharge > 1 && charge >= maxCharge ? combo.held : combo.spell;

/** A load would be taken now: the first element, or the second inside the window. */
const takesElement = (c: PlayerCast): boolean => c.meter === 0 || (c.first !== null && c.second === null && c.window > 0);

/** What a view multiplies this player's walking speed by: the rules' factor while the meter drains, else 1. */
export const moveFactorOf = (w: World, player: string): number => ((w.casts[player]?.meter ?? 0) > 0 ? RULES.combos.moveFactor : 1);

/** Whether the combo is in this player's grimoire. */
export const knowsCombo = (w: World, player: string, combo: string): boolean => w.casts[player]?.known.includes(combo) ?? false;

/** The d-pad's selected spell for this player, or undefined if the list is empty. */
export function selectedDpad(w: World, player: string): DpadSpellDef | undefined {
  const dpad = RULES.combos.dpad;
  return dpad.length === 0 ? undefined : dpad[(w.casts[player]?.slot ?? 0) % dpad.length];
}

/**
 * Applies a player's cast input, between ticks. False if it was refused: an
 * element outside the window or past the second, a d-pad cast during a cast or
 * of a slot with no spell, a cancel with nothing to drop, a release with nothing held.
 */
export function applyCombo(w: World, command: ComboCommand): boolean {
  const c = (w.casts[command.player] ??= { player: command.player, first: null, second: null, meter: 0, total: 0, window: 0, slot: 0, dpadCasting: -1, holding: false, charge: 1, known: [] });
  switch (command.kind) {
    case "load": return loadElement(c, command.element, command.hold ?? false);
    case "cancel": return cancelCast(c);
    case "cycle": return cycleDpad(c, command.step);
    case "castSelected": return castSelected(w, c);
    case "release": return releaseHold(c, command.charge);
  }
}

function loadElement(c: PlayerCast, element: string, hold: boolean): boolean {
  const combos = RULES.combos;
  if (!combos.elements.includes(element) || !takesElement(c)) return false;
  c.holding = hold;
  if (c.meter === 0) {
    c.first = element;
    c.window = combos.window;
    startMeter(c, combos.singles[element]?.castTicks ?? 0);
    return true;
  }
  c.second = element;
  c.window = 0;
  startMeter(c, combos.pairs[c.first!]?.[element]?.castTicks ?? 0);
  return true;
}

function cancelCast(c: PlayerCast): boolean {
  if (c.meter === 0) return false;
  clearCast(c);
  return true;
}

// The charge is kept to what the spell takes, here as well as by the view, so a client can't send more.
function releaseHold(c: PlayerCast, charge: number): boolean {
  if (!c.holding) return false;
  c.holding = false;
  c.charge = Math.max(1, Math.min(maxChargeOf(comboOf(c)), charge));
  return true;
}

function cycleDpad(c: PlayerCast, step: number): boolean {
  const n = RULES.combos.dpad.length;
  if (n === 0 || step === 0) return false;
  c.slot = (((c.slot + Math.sign(step)) % n) + n) % n;
  return true;
}

function castSelected(w: World, c: PlayerCast): boolean {
  const d = selectedDpad(w, c.player);
  if (c.meter > 0 || d?.spell === undefined) return false;
  c.dpadCasting = c.slot % RULES.combos.dpad.length;
  startMeter(c, d.castTicks);
  return true;
}

function startMeter(c: PlayerCast, ticks: number) {
  c.meter = ticks;
  c.total = ticks;
}

function clearCast(c: PlayerCast) {
  c.first = c.second = null;
  c.meter = c.total = c.window = 0;
  c.dpadCasting = -1;
  c.holding = false;
  c.charge = 1;
}

const inOrder = (w: World): PlayerCast[] => Object.keys(w.casts).sort().map((p) => w.casts[p]!);

/**
 * Drains every meter, in player order, and sets off those that reach zero. A
 * held cast waits at the meter's last tick, so the tick after its release sets it off.
 */
export function tickCombos(w: World) {
  for (const c of inOrder(w)) {
    if (c.meter === 0) continue;
    if (c.window > 0) c.window--;
    if (c.holding && c.meter === 1) continue;
    if (--c.meter === 0) fireCast(w, c);
  }
}

// What a mod took away since the cast began just doesn't go off.
function fireCast(w: World, c: PlayerCast) {
  if (c.dpadCasting >= 0) {
    const d = RULES.combos.dpad[c.dpadCasting];
    if (d?.spell !== undefined) {
      const shape = RULES.spells.find((s) => s.id === d.spell)?.shape;
      w.combosFired.push({ player: c.player, combo: null, name: d.name, spell: d.spell, shape, discovered: false, charge: 1 });
    }
  } else {
    const id = comboId(c.first!, c.second);
    const def = comboById(id);
    if (def) {
      const discovered = !c.known.includes(id);
      if (discovered) c.known = [...c.known, id].sort();
      const spell = spellAt(def, c.charge, maxChargeOf(def));
      const shape = spell === def.spell ? def.shape : RULES.spells.find((s) => s.id === spell)?.shape;
      w.combosFired.push({ player: c.player, combo: id, name: def.name, spell, shape, discovered, charge: c.charge });
    }
  }
  clearCast(c);
}

/**
 * The players' cast state as text, by player: "ann:fire:air:5:9:0:1:-1:fire,fire+air",
 * which is player, elements ("-" for none), meter, total, window, slot, d-pad cast, then the grimoire;
 * then, only while a cast is held or charged, ":hold:c1" or ":c3" (so other casts hash as they did).
 */
export const castState = (w: World): string =>
  inOrder(w).map((c) => [c.player, c.first ?? "-", c.second ?? "-", c.meter, c.total, c.window, c.slot, c.dpadCasting, c.known.join(",")].join(":") + chargeState(c)).join("/");

const chargeState = (c: PlayerCast): string => (c.holding ? ":hold" : "") + (c.holding || c.charge > 1 ? `:c${c.charge}` : "");

/** The whirlwinds as text, "" when there are none: where, its age, and what it carries (C#: World.WhirlState). */
export const whirlState = (w: World): string => w.whirls.map((ww) => `|${ww.x}:${ww.y}:${ww.age}:${ww.carrying ?? "-"}`).join("");

/** The waves as text, "" when there are none: where each front is, the cells it has left and its age (C#: World.WaveState). */
export const waveState = (w: World): string => w.waves.map((wv) => `|~${wv.x}:${wv.y}:${wv.left}:${wv.age}`).join("");

/** The boulders as text, "" when there are none: where each is, the cells it has left and its age (C#: World.BoulderState). */
export const boulderState = (w: World): string => w.boulders.map((b) => `|o${b.x}:${b.y}:${b.left}:${b.age}`).join("");

/** A fingerprint (FNV-1a) of every player's cast state and grimoire, the whirlwinds still whirling and the waves and boulders still rolling (C#: World.CastHash). */
export const castHash = (w: World): string => fnv1a(castState(w) + whirlState(w) + waveState(w) + boulderState(w));
