# D2. Targets: PC first; mobile and web secondary (2026-10-13)

**Status:** Accepted

- **Decision:** PC (Windows/macOS/Linux) is the first-class target.
  Native mobile and a web build are kept *possible* but don't constrain
  early work.
- **Why:** PC has the best Unity tooling, the most performance headroom
  for a physics sandbox, and no download-size pressure.
- **What it costs later:** the web build is the weak target (tens of
  MB, no managed C# threads, needs an iframe). Mobile needs a
  touch-controls and performance pass.
