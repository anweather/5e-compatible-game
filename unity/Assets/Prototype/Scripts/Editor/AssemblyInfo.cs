// Lets the Unity-only tests (Prototype.Game.Tests, Editor/Tests and Playtest/Editor)
// and the probes' CLI (Prototype.Probes.Editor, Probes/Editor: DemoRecorder's gizmos,
// FocusKeeper) reach the Editor tools' internal members, as they did when all of them
// compiled into Assembly-CSharp-Editor.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Prototype.Game.Tests")]
[assembly: InternalsVisibleTo("Prototype.Probes.Editor")]
