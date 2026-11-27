using Chem;
#if !UNITY_5_3_OR_NEWER
using NUnit.Framework;
#endif

// The repo's folders for the plans' tests, found above the Unity project in the Editor, or above the test binary under
// `dotnet test` (core/Prototype.Plans.Tests), as ChemistryTests.Folders does for the core's.
static class PlansFolders
{
    static GameFolders folders;

    internal static GameFolders Folders => folders ??= GameFolders.InRepo(
#if UNITY_5_3_OR_NEWER
        UnityEngine.Application.dataPath);
#else
        TestContext.CurrentContext.TestDirectory);
#endif
}
