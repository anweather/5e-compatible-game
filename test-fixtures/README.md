# Test fixtures

Data the tests and benches read that the game never loads. Builds copy all of `world/` into StreamingAssets, and a
joining peer hashes every map folder under `world/maps/` (`MapIdentity.Offered`), so anything only for tests lives here.

- `maps/flat-1728/`: a flat, empty 1728 x 1728 map (3 x 3 valley squares) for measuring a big world's costs
  (`MapSizeBench`). It passes `MapCheck` (`MapTests.TheBigTestMapPassesItsChecks`).
