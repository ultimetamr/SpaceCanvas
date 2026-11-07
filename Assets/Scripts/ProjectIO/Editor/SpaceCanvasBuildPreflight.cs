using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Editor
{
    /// <summary>
    /// 5 星交付前的轻量构建检查。检查失败会给出明确中文信息，只有真正缺少场景
    /// 或 Android 标识时阻断构建；SDK 能力和 16 KB 对齐等环境项以 warning 记录。
    /// </summary>
    [InitializeOnLoad]
    public sealed class SpaceCanvasBuildPreflight : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        static SpaceCanvasBuildPreflight() { EditorApplication.update += Poll; }

        private static void Poll()
        {
            const string request = "Library/SpaceCanvas.BuildPreflight.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            File.Move(request, request + "." + DateTime.UtcNow.Ticks + ".started");
            RunFromMenu();
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            var result = RunChecks(report.summary.platform == BuildTarget.Android);
            File.WriteAllText("Library/SpaceCanvas.BuildPreflight.txt", result.text);
            if (!result.ok) throw new BuildFailedException(result.text);
            Debug.Log("[SpaceCanvas Preflight]\n" + result.text);
        }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/5星验收/运行构建前检查")]
#endif
        public static void RunFromMenu()
        {
            var result = RunChecks(EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android);
            Directory.CreateDirectory("Library");
            File.WriteAllText("Library/SpaceCanvas.BuildPreflight.txt", result.text);
            if (result.ok) Debug.Log("[SpaceCanvas Preflight]\n" + result.text);
            else Debug.LogError("[SpaceCanvas Preflight]\n" + result.text);
        }

        private static (bool ok, string text) RunChecks(bool android)
        {
            var ok = true;
            var warnings = 0;
            var errors = 0;
            var text = new StringBuilder();
            text.AppendLine("SpaceCanvas 5星构建前检查");
            text.AppendLine("UTC=" + DateTime.UtcNow.ToString("O"));
            text.AppendLine("Target=" + EditorUserBuildSettings.activeBuildTarget);

            var scenes = EditorBuildSettings.scenes;
            if (scenes == null || scenes.Length == 0)
            {
                errors++; ok = false; text.AppendLine("ERROR: 未配置任何 Build Scene。");
            }
            else text.AppendLine("PASS: Build Scene 数量=" + scenes.Length);

            if (android)
            {
                var appId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
                if (string.IsNullOrWhiteSpace(appId) || appId == "com.DefaultCompany.New Unity Project")
                {
                    warnings++; text.AppendLine("WARN: Android appID 仍为默认值，请在发布前填写真实 PICO appID。");
                }
                else text.AppendLine("PASS: Android appID=" + appId);

                if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0)
                {
                    warnings++; text.AppendLine("WARN: 未启用 ARM64；PICO OS 6 真机建议使用 ARM64。");
                }
                else text.AppendLine("PASS: ARM64 已启用（16 KB 对齐仍需检查最终 APK native 库）。");

                var loaderCount = GetAndroidXrLoaderCount();
                if (loaderCount <= 0)
                {
                    warnings++; text.AppendLine("WARN: Android XR Management 没有已分配的 Loader，请确认 OpenXR Loader 已启用。");
                }
                else text.AppendLine("PASS: Android XR Management Loader 已分配（" + loaderCount + " 个）。");
            }

            CheckFile("Assets/StreamingAssets/SpaceCanvasDocs/SpaceCanvas工程格式规范v1.0.md", text, ref warnings);
            CheckFile("Assets/StreamingAssets/SpaceCanvasDocs/SpaceCanvas工程格式.schema.json", text, ref warnings);
            CheckFile("Assets/StreamingAssets/SpaceCanvasDocs/PICO-SDK-6.1.1能力清单.md", text, ref warnings);
            CheckFile("Assets/StreamingAssets/ExampleProjects/livingroom_demo.spacecanvas", text, ref warnings);
            CheckFile("Assets/StreamingAssets/ExampleProjects/booth_demo.spacecanvas", text, ref warnings);
            CheckFile("Assets/StreamingAssets/ExampleProjects/desk_demo.spacecanvas", text, ref warnings);

            var report = PicoSpatialCapabilities.Current;
            text.AppendLine(report.ToHumanText());
            if (!report.NativeMultiWindowApiFound)
            {
                warnings++;
                text.AppendLine("WARN: 当前 SDK 未暴露多原生窗口创建 API；交付使用 Full Space MR fallback，未伪称为原生多窗口。");
            }
            text.AppendLine("RESULT=" + (ok ? "PASS" : "FAIL") + "; errors=" + errors + "; warnings=" + warnings);
            return (ok, text.ToString());
        }

        private static void CheckFile(string path, StringBuilder text, ref int warnings)
        {
            if (File.Exists(path)) text.AppendLine("PASS: " + path);
            else { warnings++; text.AppendLine("WARN: 缺少 " + path); }
        }

        private static int GetAndroidXrLoaderCount()
        {
            // Reflection keeps this preflight assembly usable in a blank Unity
            // project even when XR Management is not installed.
            try
            {
                var type = Type.GetType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget, Unity.XR.Management.Editor");
                var method = type == null ? null : type.GetMethod("XRGeneralSettingsForBuildTarget");
                var settings = method == null ? null : method.Invoke(null, new object[] { BuildTargetGroup.Android });
                var assigned = settings == null ? null : settings.GetType().GetProperty("AssignedSettings")?.GetValue(settings, null);
                var loaders = assigned == null ? null : assigned.GetType().GetProperty("activeLoaders")?.GetValue(assigned, null) as System.Collections.ICollection;
                return loaders == null ? 0 : loaders.Count;
            }
            catch { return 0; }
        }
    }
}
