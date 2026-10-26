// The time of day and the weather, worked out from the tick and the world's
// seed alone: no state of their own, so every peer has the same sky at the
// same tick. The C# core's Sky.cs ports this file; what the weather does to
// cells is tickWeather in sim.ts.
import type { SimParams } from "./rules.ts";

/** In the fixed order sim.json's weather.weights are read in (C#: WeatherKind). */
export const WEATHER_KINDS = ["clear", "cloudy", "windy", "fog", "rain", "storm"] as const;
export type WeatherKind = (typeof WEATHER_KINDS)[number];

export interface WeatherNow {
  kind: WeatherKind;
  /** 0..100, ramping in and out. */
  strength: number;
  /** The prevailing wind's direction (0..3, N E S W), or -1 for none. */
  windDir: number;
  /** Which stretch of weather.periodMinutes this is, from 0. */
  period: number;
  /** How thick a meteor shower is, 0..100: a clear stretch may be one (weather.meteors.showerChance). */
  meteors: number;
}

export const MINUTES_PER_DAY = 24 * 60;

/** Ticks in one stretch of sim.json minutes (whole ticks, at least 1). */
export const ticksOf = (p: SimParams, minutes: number) => Math.max(1, Math.floor(minutes * 60 * p.ticksPerSecond));

/** Game minutes since midnight of day 1 at this tick (day.startHour at tick 0; a day lasts day.lengthMinutes). */
export const minutesAt = (p: SimParams, tick: number) =>
  Math.floor(p.day.startHour * 60) + Math.floor((tick * MINUTES_PER_DAY) / ticksOf(p, p.day.lengthMinutes));

export const dayOf = (minutes: number) => Math.floor(minutes / MINUTES_PER_DAY) + 1;
export const minuteOfDay = (minutes: number) => minutes % MINUTES_PER_DAY;

/** How hard it rains, 0..100 (rain and storms). */
export const rainOf = (n: WeatherNow) => (n.kind === "rain" || n.kind === "storm" ? n.strength : 0);

export function weatherAt(p: SimParams, seed: number, tick: number): WeatherNow {
  const wp = p.weather;
  const length = ticksOf(p, wp.periodMinutes);
  const period = Math.floor(tick / length), at = tick % length;
  const kind = kindOf(p, seed, period);
  const ramp = Math.max(1, ticksOf(p, wp.rampMinutes));
  if (kind === "clear") {
    // A clear stretch after the calm may be a meteor shower, ramping in and out within it.
    const shower = period >= wp.calmPeriods && hash(seed, period, 3) / 4294967296 < wp.meteors.showerChance;
    return { kind, strength: 0, windDir: -1, period, meteors: shower ? Math.min(100, Math.floor((Math.min(at, length - at) * 100) / ramp)) : 0 };
  }
  // Ramp in from the start and out toward the end, unless the stretch next door is the same weather.
  const fromStart = period > 0 && kindOf(p, seed, period - 1) === kind ? ramp : at;
  const toEnd = kindOf(p, seed, period + 1) === kind ? ramp : length - at;
  const strength = Math.min(100, Math.floor((Math.min(fromStart, toEnd) * 100) / ramp));
  const windy = (kind === "windy" || kind === "storm") && strength >= 50;
  return { kind, strength, windDir: windy ? hash(seed, period, 2) >>> 30 : -1, period, meteors: 0 };
}

/** The weather of one stretch: clear for the first weather.calmPeriods, then picked by weather.weights with the seed's hash. */
export function kindOf(p: SimParams, seed: number, period: number): WeatherKind {
  const wp = p.weather;
  if (period < wp.calmPeriods) return "clear";
  let total = 0;
  for (const k of WEATHER_KINDS) total += Math.max(0, wp.weights[k] ?? 0);
  if (total <= 0) return "clear";
  let pick = (hash(seed, period, 1) / 4294967296) * total;
  for (const k of WEATHER_KINDS) {
    pick -= Math.max(0, wp.weights[k] ?? 0);
    if (pick < 0) return k;
  }
  return "clear";
}

/** A 32-bit hash of the seed, a stretch and a salt (murmur3's finaliser), the same as Sky.Hash. */
export function hash(seed: number, period: number, salt: number): number {
  let h = (seed ^ Math.imul(period, 0x9e3779b1) ^ Math.imul(salt, 0x85ebca77)) >>> 0;
  h ^= h >>> 16;
  h = Math.imul(h, 0x85ebca6b);
  h ^= h >>> 13;
  h = Math.imul(h, 0xc2b2ae35);
  h ^= h >>> 16;
  return h >>> 0;
}
