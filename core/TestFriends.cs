// Every core test project links this (Chem.Tests.props): the Chemistry tests' shared helpers (ChemistryTests.Folders,
// ComboTests.Blank, BuildingTests.StuccoMod...) are internal because Unity compiles them as they are, and the projects
// above a module's (Chem.Tests.props) and the tools' test projects use them.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Chem.Rules.Tests")]
[assembly: InternalsVisibleTo("Chem.Land.Tests")]
[assembly: InternalsVisibleTo("Chem.LandWorld.Tests")]
[assembly: InternalsVisibleTo("Chem.Sim.Tests")]
[assembly: InternalsVisibleTo("Chem.Play.Tests")]
[assembly: InternalsVisibleTo("Chem.Lockstep.Tests")]
[assembly: InternalsVisibleTo("Chem.Lockstep.Kit")]
[assembly: InternalsVisibleTo("Chem.Play.Golf.Tests")]
[assembly: InternalsVisibleTo("Chem.Playtest.Tests")]
[assembly: InternalsVisibleTo("Rules.Mcp.Tests")]
[assembly: InternalsVisibleTo("Playtest.Runner.Tests")]
[assembly: InternalsVisibleTo("Chat.Bench.Tests")]
[assembly: InternalsVisibleTo("Hold.Runner.Tests")]
