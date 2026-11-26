// Keeps the Editor's test runs out of the shipped game's data folder (#78). The Performance Testing
// package (com.unity.test-framework.performance, pulled in by Collections) hangs a saver on every test
// run that writes TestResults.xml to Application.persistentDataPath, which in the Editor is the built
// game's own folder, beside a player's saved mods, play logs and chat records. Nothing here uses its
// measurements, so at the start of each run this takes that saver off and writes the run's results to
// Library/TestResults.xml instead.
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

[InitializeOnLoad]
static class TestResultsHome
{
    const string SaverType = "Unity.PerformanceTesting.Editor.PerformanceTestRunSaver";

    public static string File => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Library", "TestResults.xml");

    static TestResultsHome() => TestRunnerApi.RegisterTestCallback(new Results(), int.MaxValue);

    public static int RemoveSaver()
    {
        int removed = 0;
        foreach (var callback in Registered())
        {
            if (callback != null && callback.GetType().FullName == SaverType)
            {
                TestRunnerApi.UnregisterTestCallback(callback);
                removed++;
            }
        }
        return removed;
    }

    public static ICallbacks[] Registered()
    {
        var holder = typeof(TestRunnerApi).Assembly.GetType("UnityEditor.TestTools.TestRunner.Api.CallbacksHolder");
        object instance = holder?.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
        return instance == null ? Array.Empty<ICallbacks>() : holder.GetMethod("GetAll")?.Invoke(instance, null) as ICallbacks[] ?? Array.Empty<ICallbacks>();
    }

    sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) => RemoveSaver();

        public void RunFinished(ITestResultAdaptor result)
        {
            try
            {
                TestRunnerApi.SaveResultToFile(result, File);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"Test results not saved to {File}: {e.Message}");
            }
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
        }
    }
}
