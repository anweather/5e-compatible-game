Needs: core
Area: unity/Assets/Prototype/Chemistry/Tests/Hold*.cs, unity/Assets/Prototype/Chemistry/Tests/Hands*.cs,
core/Game.Core.Tests/Parallel.cs; tools/workers/gates.sh (a category filter); docs/TESTING_LAYERS.md
Branch: test-speed-core

# Make the core suite fast (docs/TESTING_LAYERS.md layer 3; the lead, 2026-11-08)

A timed run (1872 tests, 861 s of test time, 3-5 min wall at 3-6 workers): the 15 slowest tests are a third of it,
nearly all Hold runs on the full valley, 12-27 s each: TheWavesGoOnPastAnyLimit 27.3, ADamageUpgradeHitsHarderForThat
WizardOnly 24.7, AnOfferLeansIntoTheHandSet 23.8, AOneHandedWizardIsNeverOfferedATwoHandedUpgrade 23.8,
AdjectivesComeFromTheirWaveOn 22.5, EachWizardHasItsOwnOffer 20.7, TwoPeersWithDifferentPointsStayInLockstep... 19.2,
TwoPeersRunAlikeToALoss 19.2, AMomentIsTheWavePlayWouldHaveReached 19.1, TwoWorldsMatchOverSeveralWaves 18.0,
AClientAndALateJoiner...ChunkedValley 16.6, AClimberIgnoresIt 14.6, AWizardsWallRoundTheHeartIsBatteredDown 13.

1. Tests of wave, offer, upgrade and adjective logic run on a small test room or a stub map, not the valley, and as
   few ticks as the check needs. One built world shared per fixture where the tests don't mutate it.
2. The whole-run two-peer lockstep tests move to `[Category("Sweep")]`: the sweep (layer 4) runs them; the landing
   gates skip that category (`gates.sh`: `--filter "TestCategory!=Sweep"`), and `docs/TESTING_LAYERS.md` says so.
   Keep at least one short two-peer lockstep test in the landing suite.
3. Measure the suite before and after (wall and the slowest 15), aiming for about a minute at 6 workers.
Don't weaken a check: a test moved to a small map must still fail on the bug it guards (show one by breaking it).
Before landing: build -warnaserror, `dotnet test core` twice, the Sweep category once, format. Land with land.sh.
