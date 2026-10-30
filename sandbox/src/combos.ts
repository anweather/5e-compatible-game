// Casting by elements (docs/SPELL_COMBOS.md): each player loads one element or
// an ordered pair, a meter drains a tick at a time, and at zero the combo goes
// off and joins the player's grimoire. The d-pad's spells drain the same meter.
// C#: World.Combos.cs, line for line.
import { RULES, type ComboDef, type DpadSpellDef } from "./rules.ts";
import { fnv1a, type PlayerCast, type World } from "./world.ts";

/** A player's cast input (C#: ComboCommand), stamped like a cast. */
export type ComboCommand =
  | { player: string; kind: "load"; element: string }
  | { player: string; kind: "cancel" }
  | { player: string; kind: "cycle"; step: number }
  | { player: string; kind: "castSelected" };

/** The id of an element alone (second null) or of an ordered pair. */
export const comboId = (first: string, second: string | null): string => (second === null ? first : `${first}+${second}`);

/** The combo with this id, or undefined. */
export function comboById(id: string): ComboDef | undefined {
  const [first, second] = id.split("+") as [string, string | undefined];
  return second === undefined ? RULES.combos.singles[first] : RULES.combos.pairs[first]?.[second];
}

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
 * of a slot with no spell, a cancel with nothing to drop.
 */
export function applyCombo(w: World, command: ComboCommand): boolean {
  const c = (w.casts[command.player] ??= { player: command.player, first: null, second: null, meter: 0, total: 0, window: 0, slot: 0, dpadCasting: -1, known: [] });
  switch (command.kind) {
    case "load": return loadElement(c, command.element);
    case "cancel": return cancelCast(c);
    case "cycle": return cycleDpad(c, command.step);
    case "castSelected": return castSelected(w, c);
  }
}

function loadElement(c: PlayerCast, element: string): boolean {
  const combos = RULES.combos;
  if (!combos.elements.includes(element)) return false;
  if (c.meter === 0) {
    c.first = element;
    c.window = combos.window;
    startMeter(c, combos.singles[element]?.castTicks ?? 0);
    return true;
  }
  if (c.first === null || c.second !== null || c.window === 0) return false;
  c.second = element;
  c.window = 0;
  startMeter(c, combos.pairs[c.first]?.[element]?.castTicks ?? 0);
  return true;
}

function cancelCast(c: PlayerCast): boolean {
  if (c.meter === 0) return false;
  clearCast(c);
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
}

const inOrder = (w: World): PlayerCast[] => Object.keys(w.casts).sort().map((p) => w.casts[p]!);

/** Drains every meter, in player order, and sets off those that reach zero. */
export function tickCombos(w: World) {
  for (const c of inOrder(w)) {
    if (c.meter === 0) continue;
    if (c.window > 0) c.window--;
    if (--c.meter === 0) fireCast(w, c);
  }
}

// What a mod took away since the cast began just doesn't go off.
function fireCast(w: World, c: PlayerCast) {
  if (c.dpadCasting >= 0) {
    const d = RULES.combos.dpad[c.dpadCasting];
    if (d?.spell !== undefined) {
      const shape = RULES.spells.find((s) => s.id === d.spell)?.shape;
      w.combosFired.push({ player: c.player, combo: null, name: d.name, spell: d.spell, shape, discovered: false });
    }
  } else {
    const id = comboId(c.first!, c.second);
    const def = comboById(id);
    if (def) {
      const discovered = !c.known.includes(id);
      if (discovered) c.known = [...c.known, id].sort();
      w.combosFired.push({ player: c.player, combo: id, name: def.name, spell: def.spell, shape: def.shape, discovered });
    }
  }
  clearCast(c);
}

/**
 * The players' cast state as text, by player: "ann:fire:air:5:9:0:1:-1:fire,fire+air",
 * which is player, elements ("-" for none), meter, total, window, slot, d-pad cast, then the grimoire.
 */
export const castState = (w: World): string =>
  inOrder(w).map((c) => [c.player, c.first ?? "-", c.second ?? "-", c.meter, c.total, c.window, c.slot, c.dpadCasting, c.known.join(",")].join(":")).join("/");

/** A fingerprint (FNV-1a) of every player's cast state and grimoire (C#: World.CastHash). */
export const castHash = (w: World): string => fnv1a(castState(w));
