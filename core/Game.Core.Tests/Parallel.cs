// The Chemistry fixtures that run their own tests side by side too (#162); the assembly's settings, a fixture to a thread
// and 6 at once, are core/TestParallel.cs. Here, and not beside the tests, because Unity's test runner reads those files.
using NUnit.Framework;

namespace Chem.Tests
{
    // Whole runs, the longest fixture by far, and each test has only its own world: they go side by side too.
    [Parallelizable(ParallelScope.Children)]
    public partial class HoldTests
    {
    }

    // Each builds its own world.
    [Parallelizable(ParallelScope.Children)]
    public partial class HoldProgressTests
    {
    }

    // The same: each builds its own world, and a fixture of them is the suite's longest after HoldTests.
    [Parallelizable(ParallelScope.Children)]
    public partial class HoldWaveTests
    {
    }
}
