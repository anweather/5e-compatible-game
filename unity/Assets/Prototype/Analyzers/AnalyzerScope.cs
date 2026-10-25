// Scope marker for the Roslyn analyzers in this folder (see docs/QUALITY.md).
//
// Unity applies an analyzer DLL that sits next to an .asmdef only to that
// assembly and to the assemblies that reference it. An .asmdef with no
// scripts produces no assembly, so this comment-only file exists to make
// Prototype.Analyzers a real (empty) assembly. Our own assemblies reference
// it; the Asset Store packs and Unity packages don't, so they aren't analyzed.
