Needs: core
Area: core/Rules.Mcp/, its tests in core/
Branch: mcp-blue-fire

# The rules MCP refuses the lookfx probe's blue fire patch (#167)

The Unity probe `lookfx` starts by applying a "Blue fire" rule change through the rules MCP, and the MCP answers
"That isn't JSON." It failed the same way on dev at 684b54b7 and cc47375c, so every later look check fails too.

- Find the exact text the probe sends: search `unity/Assets/Prototype/Scripts/Probe*.cs` (ProbeRoom.cs,
  ProbeScenarios.cs, ProbeChat.cs) for "Blue fire" and follow it to the MCP call.
- Replay that request against `core/Rules.Mcp` headless, and find why it's refused. Suspects are recent changes to the
  MCP's fields and parsing: `mcp-controls-text` (e77f8e3e) and `air-tuning-rules` (cacf1b54). The fault may also be
  the probe's own JSON.
- Fix it where the fault is. If it's the MCP, add a test in core that sends the probe's request. If it's only the
  probe's text, fix the probe (Unity C#) and say so; the PC compiles it.
- Comment on #167 with the cause, ending with a `Discovery-Cost:` line.

Before landing: `dotnet build core -c Release -warnaserror`, `dotnet format core --verify-no-changes`,
`dotnet test core`. If only `core/` changed (nothing under `unity/`), land with `land.sh here`. Otherwise push the
branch and report; the PC compiles and lands it. Remove this claim file in your landing, or on the branch.
