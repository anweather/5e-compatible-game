// Every core test project's (each links this): fixtures side by side, a fixture to a thread (#162). Not beside the tests
// they run, because Unity's test runner reads those same files. A test that can't share says so with [NonParallelizable].
using NUnit.Framework;

[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(6)] // past this the fixtures, who all make whole worlds, slow each other down
