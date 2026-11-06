// The core tests run side by side, a fixture to a thread (#162). Here, and not beside the tests they run, because Unity's test
// runner reads those same files: this file is only this project's. A test that can't share says so with [NonParallelizable].
using NUnit.Framework;

[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(6)] // past this the fixtures, who all make whole worlds, slow each other down

namespace Chem.Tests
{
    // Whole runs, the longest fixture by far, and each test has only its own world: they go side by side too.
    [Parallelizable(ParallelScope.Children)]
    public partial class HoldTests
    {
    }

    // The same: each builds its own world, and a fixture of them is the suite's longest after HoldTests.
    [Parallelizable(ParallelScope.Children)]
    public partial class HoldWaveTests
    {
    }
}
