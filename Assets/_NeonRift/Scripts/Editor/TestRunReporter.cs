using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace NeonRift.EditorTools
{
    /// <summary>Runs the EditMode suite and writes a plain-text report to Logs/TestResults-EditMode.txt (for tooling and CI).</summary>
    public static class TestRunReporter
    {
        public const string ReportPath = "Logs/TestResults-EditMode.txt";

        [MenuItem("Neon Rift/Tests/Run EditMode Tests (write report)")]
        public static void RunEditMode()
        {
            if (File.Exists(ReportPath)) File.Delete(ReportPath);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks(api));
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        private sealed class Callbacks : ICallbacks
        {
            private readonly TestRunnerApi api;
            public Callbacks(TestRunnerApi api) => this.api = api;

            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount} inconclusive={result.InconclusiveCount} duration={result.Duration:0.0}s");
                Append(result, sb);
                File.WriteAllText(ReportPath, sb.ToString());
                Debug.Log($"[Tests] EditMode: {result.PassCount} passed, {result.FailCount} failed. Report: {ReportPath}");
                api.UnregisterCallbacks(this);
            }

            private static void Append(ITestResultAdaptor r, StringBuilder sb)
            {
                if (!r.HasChildren)
                {
                    sb.AppendLine($"{r.TestStatus,-12} {r.Test.FullName} ({r.Duration:0.00}s)");
                    if (r.TestStatus == TestStatus.Failed) sb.AppendLine("    " + r.Message?.Replace("\n", "\n    ").TrimEnd());
                    return;
                }
                foreach (var c in r.Children) Append(c, sb);
            }
        }
    }
}
