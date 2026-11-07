using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Editor
{
    /// <summary>
    /// TMP's installed C# mesh writer and imported Essential Resources shaders must agree.
    /// Unity 6 UGUI writes signed SDF scale into UV0.w; older shaders read UV1.y, which is
    /// now an ordinary layout UV. MSAA and adding canvas channels cannot repair that mismatch.
    /// Keep the shader GUIDs and font/material assets when updating official shader sources.
    /// </summary>
    public sealed class SpaceCanvasTmpShaderCompatibility : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1100;
        public void OnPreprocessBuild(BuildReport report) => ValidateInstalledShaders();

        public static void ValidateInstalledShaders()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            var writerPath = package == null ? "" : Path.Combine(package.resolvedPath, "Runtime/TMP/TextMeshProUGUI.cs");
            if (!File.Exists(writerPath))
            {
                Debug.LogWarning("[SpaceCanvas TMP] 无法读取当前 TMP 网格源码，跳过顶点格式检查。");
                return;
            }
            var writer = File.ReadAllText(writerPath);
            var packedScale = writer.Contains("vertex_BL.uv.w = xScale");
            foreach (var name in new[] { "TextMeshPro/Mobile/Distance Field", "TextMeshPro/Distance Field",
                "TextMeshPro/Mobile/Distance Field Overlay" })
            {
                var shader = Shader.Find(name);
                var path = shader == null ? "" : AssetDatabase.GetAssetPath(shader);
                if (!File.Exists(path)) throw new BuildFailedException("缺少 TMP 官方着色器：" + name);
                ValidateSource(File.ReadAllText(path), packedScale, path);
                if (ShaderUtil.ShaderHasError(shader)) throw new BuildFailedException("TMP 着色器编译失败：" + path);
            }
            Debug.Log("[SpaceCanvas TMP] Mesh/shader vertex format verified: " + (packedScale ? "UV0.w" : "UV1.y"));
        }

        /// <summary>Separate contract check so a legacy shader regression fails before making an APK.</summary>
        public static void ValidateSource(string source, bool packedScale, string path = "TMP shader")
        {
            var expected = packedScale ? "abs(input.texcoord0.w)" : "abs(input.texcoord1.y)";
            if (source == null || !source.Contains(expected))
                throw new BuildFailedException(path + " 与当前 TMP 顶点格式不匹配（应读取 "
                    + (packedScale ? "UV0.w" : "UV1.y")
                    + "）。请使用当前 UGUI/TMP 包的 Essential Resources 更新官方着色器，保留现有字体和材质 GUID。");
        }
    }
}
