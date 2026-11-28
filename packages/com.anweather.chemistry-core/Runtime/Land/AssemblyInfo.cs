using System.Runtime.CompilerServices;

// The land was part of the core's one assembly until it became its own (docs/design/architecture-review.md step 5), and
// the core above it still uses its internals (LandCodec, BuildingTables.Tables, BuildingRow.Resolved...): the core is
// Chemistry in Unity and Game.Core in dotnet; the rules MCP and chat tools (Chemistry.Mcp in Unity) and the game modes
// (Chem.Play) used them too.
[assembly: InternalsVisibleTo("Chemistry")]
[assembly: InternalsVisibleTo("Game.Core")]
[assembly: InternalsVisibleTo("Chemistry.Mcp")]
[assembly: InternalsVisibleTo("Chem.Play")]
