using System.Runtime.CompilerServices;

// The rules MCP and chat tools (Chem.Mcp) were compiled into this assembly until the core became a package
// (docs/design/core-package/audit.md, pass A). They now have their own assembly and still use the core's internals.
[assembly: InternalsVisibleTo("Chemistry.Mcp")]
