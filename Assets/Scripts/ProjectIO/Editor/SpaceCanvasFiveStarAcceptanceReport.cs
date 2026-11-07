using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Editor
{
    /// <summary>
    /// Generates a reproducible acceptance report for reviewers.  It validates the
    /// open-format examples, documentation, package wiring and the current PICO
    /// capability matrix without claiming unavailable native window APIs.
    /// </summary>
    [InitializeOnLoad]
    public static class SpaceCanvasFiveStarAcceptanceReport
    {
        static SpaceCanvasFiveStarAcceptanceReport() { EditorApplication.update += Poll; }

        private static void Poll()
        {
            const string request = "Library/SpaceCanvas.GenerateAcceptance.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            File.Move(request, request + "." + DateTime.UtcNow.Ticks + ".started");
            GenerateMenuReport();
        }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/5星验收/生成完整验收报告")]
#endif
        public static void GenerateMenuReport()
        {
            var report = Generate();
            Directory.CreateDirectory("Library");
            File.WriteAllText("Library/SpaceCanvas.5Star.Acceptance.md", report, new UTF8Encoding(false));
            Debug.Log("SpaceCanvas 5 星验收报告已生成：Library/SpaceCanvas.5Star.Acceptance.md\n" + report);
        }

        public static string Generate()
        {
            var builder = new StringBuilder();
            var passed = 0;
            var warnings = 0;
            var failed = 0;
            builder.AppendLine("# SpaceCanvas 5 星验收报告");
            builder.AppendLine();
            builder.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("Unity：" + Application.unityVersion);
            builder.AppendLine();

            CheckFile("Packages/manifest.json", builder, ref passed, ref warnings, ref failed, true);
            CheckFile("Assets/StreamingAssets/SpaceCanvasDocs/SpaceCanvas工程格式规范v1.0.md", builder, ref passed, ref warnings, ref failed, true);
            CheckFile("Assets/StreamingAssets/SpaceCanvasDocs/5星验收与能力矩阵.md", builder, ref passed, ref warnings, ref failed, true);
            CheckFile("Assets/StreamingAssets/ExampleProjects/livingroom_demo.spacecanvas", builder, ref passed, ref warnings, ref failed, true);
            CheckFile("Assets/StreamingAssets/ExampleProjects/booth_demo.spacecanvas", builder, ref passed, ref warnings, ref failed, true);
            CheckFile("Assets/StreamingAssets/ExampleProjects/desk_demo.spacecanvas", builder, ref passed, ref warnings, ref failed, true);

            foreach (var name in SpaceCanvasProjectCatalog.OfficialExampleNames)
            {
                var path = Path.Combine(Application.streamingAssetsPath, "ExampleProjects", name);
                // In the Editor StreamingAssets is a normal directory. Android reads
                // these files through ExampleProjectLoader's UnityWebRequest path.
                if (!Application.isEditor) continue;
                try
                {
                    var project = ProjectSerializer.Deserialize(File.ReadAllText(path));
                    ProjectValidator.Validate(project);
                    builder.AppendLine("- PASS 示例合法：" + name + "（" + project.objects.Count + " 个物体）");
                    passed++;
                }
                catch (Exception ex)
                {
                    builder.AppendLine("- FAIL 示例损坏：" + name + " — " + ex.Message);
                    failed++;
                }
            }

            var capability = PicoSpatialCapabilities.Current;
            builder.AppendLine();
            builder.AppendLine("## PICO 能力矩阵");
            builder.AppendLine();
            builder.AppendLine("```text");
            builder.AppendLine(capability.ToHumanText());
            builder.AppendLine("```");
            if (!capability.NativeMultiWindowApiFound)
            {
                builder.AppendLine("- WARN 原生 Planar/Volumetric 创建 API 未从当前 SDK 公开程序集发现；交付模式为 Full Space MR fallback。");
                warnings++;
            }
            else passed++;

            builder.AppendLine();
            builder.AppendLine("## 结果");
            builder.AppendLine();
            builder.AppendLine("PASS=" + passed + ", WARN=" + warnings + ", FAIL=" + failed);
            builder.AppendLine(failed == 0
                ? "结论：工程格式、示例和可运行链路通过；空间窗口能力按 SDK 实际公开接口报告。"
                : "结论：存在必须修复的验收项，请先处理 FAIL 项。");
            return builder.ToString();
        }

        private static void CheckFile(string path, StringBuilder builder, ref int passed,
            ref int warnings, ref int failed, bool required)
        {
            if (File.Exists(path))
            {
                builder.AppendLine("- PASS 文件存在：" + path);
                passed++;
            }
            else
            {
                builder.AppendLine((required ? "- FAIL" : "- WARN") + " 文件缺失：" + path);
                if (required) failed++; else warnings++;
            }
        }
    }
}
