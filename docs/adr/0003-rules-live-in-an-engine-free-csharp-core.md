# D3. Rules live in an engine-free C# core (2026-10-13)

**Status:** Accepted

- **Decision:** the 5E rules, the chemistry tables and the field grid
  are a `Game.Core` library:
  - `netstandard2.1`, using C# 9 features or below (that is what Unity
    supports until CoreCLR lands);
  - a Unity `.asmdef` with `noEngineReferences: true`;
  - xUnit tests run under `dotnet test`.
- **Why:**
  - It keeps one rules implementation that can serve the Unity client,
    a headless Unity server, or an ASP.NET Core platform plugin.
  - It can be tested fast, without the Editor.
  - It is the same "pure core, rules as data" shape as the platform's
    plugins and Pocket Tactics.
- **Rules for the core:**
  - no `UnityEngine`;
  - no mutable statics (Unity 6.6 skips domain reload by default, so
    statics leak between runs);
  - all randomness from a seeded PRNG stored in state;
  - integer or fixed-point maths for anything that must agree across
    machines.
