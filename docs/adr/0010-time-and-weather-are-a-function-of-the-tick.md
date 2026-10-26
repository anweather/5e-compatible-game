# D10. Time and weather are a function of the tick (2026-10-25)

**Status:** Accepted

- **Context:** day and night and weather (`BACKLOG.md` 1 and 2) change
  shared state: rain puts out fires and fills hollows, the wind steers
  fire and gas, storms strike lightning. In lockstep (D7) everything that
  changes cells has to be the same on every machine at the same tick, and
  a late joiner rebuilds the world by replaying from tick 0.
- **Decision:**
  - **The clock and the weather live in the Chem core, as pure functions
    of the tick, the world's seed and `sim.json`** (`Sky.cs`, ported from
    `sandbox/src/sky.ts`). They add no state: nothing to send, save or
    hash, and a late joiner's sky is right by construction.
  - The forecast is in stretches (`weather.periodMinutes`), each picked by
    a hash of the seed and the stretch with `weather.weights`, ramping in
    and out. The world starts with `weather.calmPeriods` clear stretches.
  - **What the weather does to cells is a system in the tick** (after gas;
    `World.Weather.cs`, `tickWeather` in `sim.ts`), using the world's RNG
    like every other system, with shared goldens in both engines.
  - Players and tests change the time and the weather the way they change
    anything else in the rules: a mod (a rule switch, which travels in the
    tick batches). The `sky` probe does exactly that.
  - **The look is the view's** (`SkyLight`, `WeatherFx`): the sun, moon,
    sky, haze, rain, bolts and sounds, read from the core each frame.
- **Why:** a function of the tick is the cheapest thing that is
  deterministic, network-free and replayable. Weather as world-wide
  stretches was enough to make rain, wind and storms part of the
  chemistry now; the calm start keeps every existing golden and probe
  untouched.
- **Reopen if:** the weather has to move across the world (fronts per
  chunk, `WORLD_STREAMING.md` part 2), react to the world (a big fire
  making its own updraft and rain), or be set by a spell (Control
  Weather). Those need weather state, which then has to be in the
  snapshot and the checksum. Also if one seed's forecast (every session
  sees the same weather) gets boring: the host could pick the seed.
