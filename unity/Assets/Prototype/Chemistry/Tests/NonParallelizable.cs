// [NonParallelizable] (#162): dotnet test runs fixtures side by side (core/Game.Core.Tests/Parallel.cs), and this says a test
// can't share. Unity's NUnit has no such attribute and runs one test at a time anyway, so there it's a name that does nothing.
#if UNITY_5_3_OR_NEWER
using System;

namespace Chem.Tests
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    internal sealed class NonParallelizableAttribute : Attribute
    {
    }
}
#endif
