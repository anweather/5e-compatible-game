Needs: unity
Area: unity/Assets/Prototype/Scripts/World/LandStamp*, unity/Assets/Prototype/Scripts/Props*
Branch: stamp-looks

# Land stamps, the view half (#147, #148)

- #147: on a 15 m dome a crate slid 54.7 m from home at 12.9 m/s. Find whether it's a plain slide on a 54 degree
  flank or something wrong in props following land; fix it if it's wrong. Measure the per-area carry query's cost
  with crates on a stamp.
- #148: a radius 3 stamp reads as a grey rock patch with boulders. Make a small stamp read as a bump.
- Before/after screenshots at 1280 wide, per `screenshot-budget`, and the demo clip rule.
Compile, run EditMode and the land probes, then land with `land.sh here --compiled`. Comment on each issue.
