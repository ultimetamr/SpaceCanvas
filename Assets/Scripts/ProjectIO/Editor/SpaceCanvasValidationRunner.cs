using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Editor
{
    /// <summary>在真正的 Unity EditMode 中执行本模块测试，并保存 NUnit XML 报告。</summary>
    [InitializeOnLoad]
    public static class SpaceCanvasValidationRunner
    {
        private static TestRunnerApi runner;
        static SpaceCanvasValidationRunner() { EditorApplication.update += Poll; }
        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying || runner != null) return;
            if (File.Exists("Library/SpaceCanvas.RunEditorImportTests.request"))
            {
                File.Move("Library/SpaceCanvas.RunEditorImportTests.request", "Library/SpaceCanvas.RunEditorImportTests." + DateTime.UtcNow.Ticks + ".started");
                RunImportParity();
                return;
            }
            if (File.Exists("Library/SpaceCanvas.UpgradeMaterials.request"))
            {
                File.Move("Library/SpaceCanvas.UpgradeMaterials.request", "Library/SpaceCanvas.UpgradeMaterials." + DateTime.UtcNow.Ticks + ".started");
                SpaceCanvasSceneBootstrap.UpgradeCurrentSceneAssets();
            }
            if (File.Exists("Library/SpaceCanvas.RefreshPreview.request"))
            {
                File.Move("Library/SpaceCanvas.RefreshPreview.request", "Library/SpaceCanvas.RefreshPreview." + DateTime.UtcNow.Ticks + ".started");
                SpaceCanvasSceneBootstrap.RefreshPreview();
            }
            if (!File.Exists("Library/SpaceCanvas.RunTests.request")) return;
            File.Move("Library/SpaceCanvas.RunTests.request", "Library/SpaceCanvas.RunTests." + DateTime.UtcNow.Ticks + ".started");
            Run();
        }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/测试/运行完整 EditMode 验证")]
#endif
        public static void Run()
        {
            if (runner != null) return;
            var assemblies = new System.Collections.Generic.List<string> { "SpaceCanvas.ProjectIO.Tests" };
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "SpaceCanvas.ProjectIO.XR.Tests"
                    || assembly.GetName().Name == "SpaceCanvas.ProjectIO.Editor.Tests") assemblies.Add(assembly.GetName().Name);
            RunAssemblies(assemblies.ToArray());
        }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/测试/验证头显与编辑器模型一致性")]
#endif
        public static void RunImportParity() { RunAssemblies(new[] { "SpaceCanvas.ProjectIO.Editor.Tests" }); }

        private static void RunAssemblies(string[] assemblies)
        {
            if (runner != null) return;
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new Callbacks());
            runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = assemblies }));
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                new System.Xml.Linq.XDocument(ToXml(result)).Save("Library/SpaceCanvas.EditMode.Results.xml");
                File.WriteAllText("Library/SpaceCanvas.Validation.txt", "Passed=" + result.PassCount + " Failed=" + result.FailCount + " Skipped=" + result.SkipCount + "\n" + result.Message);
                Debug.Log("SpaceCanvas EditMode：通过 " + result.PassCount + "，失败 " + result.FailCount + "，跳过 " + result.SkipCount);
                UnityEngine.Object.DestroyImmediate(runner); runner = null;
            }

            private static System.Xml.Linq.XElement ToXml(ITestResultAdaptor result)
            {
                var node = new System.Xml.Linq.XElement("test-result",
                    new System.Xml.Linq.XAttribute("name", result.FullName),
                    new System.Xml.Linq.XAttribute("result", result.ResultState),
                    new System.Xml.Linq.XAttribute("passed", result.PassCount),
                    new System.Xml.Linq.XAttribute("failed", result.FailCount),
                    new System.Xml.Linq.XElement("message", result.Message ?? ""),
                    new System.Xml.Linq.XElement("stack-trace", result.StackTrace ?? ""));
                foreach (var child in result.Children) node.Add(ToXml(child));
                return node;
            }
        }
    }
}
