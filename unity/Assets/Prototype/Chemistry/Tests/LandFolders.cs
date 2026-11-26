using System.IO;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The repo's folders for the land's tests (Chem.Land.Tests, which builds on Chem.Land alone, so without
    /// ChemistryTests and its rules): found as ChemistryTests.Folders finds them, above the Unity project or above the
    /// test binary under `dotnet test`.
    /// </summary>
    internal static class LandFolders
    {
        static GameFolders folders;

        internal static GameFolders Folders => folders ??= GameFolders.InRepo(
#if UNITY_5_3_OR_NEWER
            UnityEngine.Application.dataPath);
#else
            TestContext.CurrentContext.TestDirectory);
#endif

        /// <summary>The repo's rules/ folder.</summary>
        internal static string RulesDir() => Folders.Rules;

        /// <summary>The default map's folder (world/maps/valley), as ChemistryTests.WorldDir.</summary>
        internal static string WorldDir() => Folders.World;

        /// <summary>The game's world/ folder (bands, biomes, square templates, landmarks, maps).</summary>
        internal static string GameWorldDir() => Path.Combine(Directory.GetParent(RulesDir()).FullName, "world");
    }
}
