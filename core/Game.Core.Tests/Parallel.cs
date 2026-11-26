// The Chemistry fixtures that run their own tests side by side too (#162); the assembly's settings, a fixture to a thread
// and 6 at once, are core/TestParallel.cs. Here, and not beside the tests, because Unity's test runner reads those files.
using NUnit.Framework;

namespace Chem.Tests
{
    [Parallelizable(ParallelScope.Children)]
    public partial class HoldTests
    {
    }

    [Parallelizable(ParallelScope.Children)]
    public partial class HoldProgressTests
    {
    }

    [Parallelizable(ParallelScope.Children)]
    public partial class HoldWaveTests
    {
    }
}
