// The repo's folders for the land's tests (Chem.Land.Tests, which builds on Chem.Land alone, so without
// ChemistryTests and its rules): found as ChemistryTests.Folders finds them, above the Unity project or above the
// test binary under `dotnet test`.
using System.IO;
using NUnit.Framework;

namespace Chem.Tests
{
    internal static class LandFolders
    {
        static GameFolders folders;

        internal static GameFolders Folders => folders ??= GameFolders.InRepo(
#if UNITY_5_3_OR_NEWER
            UnityEngine.Application.dataPath);
#else
            TestContext.CurrentContext.TestDirectory);
#endif

        internal static string RulesDir() => Folders.Rules;

        internal static string WorldDir() => Folders.World;

        internal static string GameWorldDir() => Path.Combine(Directory.GetParent(RulesDir()).FullName, "world");
    }
}
