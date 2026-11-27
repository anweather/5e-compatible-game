// Stand-ins for the few engine hooks the view's plans carry (docs/design/architecture-review.md step 6): the console a
// plan logs to, and the mark that a static is reset when Play starts. dotnet only: they do nothing more here.
namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object message) => System.Console.WriteLine(message);
    }

    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad,
        BeforeSceneLoad,
        AfterAssembliesLoaded,
        BeforeSplashScreen,
        SubsystemRegistration,
    }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : System.Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) => LoadType = loadType;

        public RuntimeInitializeLoadType LoadType { get; }
    }
}
