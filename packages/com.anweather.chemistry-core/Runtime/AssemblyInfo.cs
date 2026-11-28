using System.Runtime.CompilerServices;

// The rules MCP and chat tools (Chem.Mcp) were compiled into this assembly until the core became a package
// (docs/design/core-package/audit.md, pass A). They now have their own assembly and still use the core's internals.
// So do the game modes above the core, Chem.Play (architecture-review.md step 9): their systems work on the world's
// cells, raiders and state, and install themselves on it.
[assembly: InternalsVisibleTo("Chemistry.Mcp")]
[assembly: InternalsVisibleTo("Chem.Play")]
