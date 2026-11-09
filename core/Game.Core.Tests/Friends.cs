// The tools' test projects reference this one for the Chemistry tests' shared helpers (ChemistryTests.Folders,
// ComboTests.Blank, BuildingTests.StuccoMod...), which are internal because Unity compiles them as they are.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Rules.Mcp.Tests")]
[assembly: InternalsVisibleTo("Playtest.Runner.Tests")]
[assembly: InternalsVisibleTo("Chat.Bench.Tests")]
[assembly: InternalsVisibleTo("Hold.Runner.Tests")]
