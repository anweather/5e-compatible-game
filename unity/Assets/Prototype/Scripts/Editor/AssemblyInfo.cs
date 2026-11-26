// Lets the Unity-only tests (Prototype.Game.Tests, Editor/Tests and Playtest/Editor)
// reach the Editor tools' internal members, as they did when both compiled into
// Assembly-CSharp-Editor.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Prototype.Game.Tests")]
