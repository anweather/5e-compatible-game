// What the night does in the rules: the light on each cell, from the day clock
// (daylight in sky.ts) and the fires burning near it, in the SRD's three
// levels. Worked out from the clock and the cells when asked, never kept, so
// it is the same on every peer. (Night's hold on water is tickWeather in
// sim.ts.) The C# core's World.Night.cs ports this file.
import { RULES } from "./rules.ts";
import { type LightLevel, BRIGHT, daylight, minutesAt } from "./sky.ts";
import { type World, cellAt, clockTick } from "./world.ts";

/** The light the sky gives now (C#: World.Daylight). */
export const daylightNow = (w: World): LightLevel => daylight(RULES.sim.day, minutesAt(RULES.sim, clockTick(w)));

/** Whether it's night: dark between dusk and dawn (C#: World.IsNight). */
export const isNight = (w: World) => daylightNow(w) === 0;

const feetToCells = (feet: number) => Math.floor(feet / 5);

/**
 * The light on each cell of a bw by bh block from (x0, y0), row by row: the daylight, raised
 * by every burning cell within reach, bright out to day.fireLight.bright feet and dim beyond.
 * Distance is the SRD's grid distance (a diagonal step is 5 ft).
 */
export function fillLight(w: World, x0: number, y0: number, bw: number, bh: number): LightLevel[] {
  const day = daylightNow(w);
  const into: LightLevel[] = new Array<LightLevel>(bw * bh).fill(day);
  if (day === BRIGHT) return into;
  const d = RULES.sim.day;
  const bright = feetToCells(d.fireLight.bright), reach = feetToCells(d.fireLight.bright + d.fireLight.dim);
  const shine = (fx: number, fy: number) => {
    for (let y = Math.max(y0, fy - reach); y <= Math.min(y0 + bh - 1, fy + reach); y++)
      for (let x = Math.max(x0, fx - reach); x <= Math.min(x0 + bw - 1, fx + reach); x++) {
        const level: LightLevel = Math.max(Math.abs(x - fx), Math.abs(y - fy)) <= bright ? 2 : 1;
        const i = (y - y0) * bw + (x - x0);
        if (level > into[i]!) into[i] = level;
      }
  };
  for (let y = y0 - reach; y < y0 + bh + reach; y++)
    for (let x = x0 - reach; x < x0 + bw + reach; x++) if ((cellAt(w, x, y)?.fire ?? 0) > 0) shine(x, y);
  return into;
}

/** The light on one cell (C#: World.LightAt). */
export const lightAt = (w: World, x: number, y: number): LightLevel => fillLight(w, x, y, 1, 1)[0]!;
