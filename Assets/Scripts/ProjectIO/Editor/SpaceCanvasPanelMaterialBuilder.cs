using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Editor
{
    /// <summary>Persist the UI shader before stripping; one shared material serves every panel.</summary>
    public sealed class SpaceCanvasPanelMaterialBuilder : IPreprocessBuildWithReport
    {
        public const string MaterialPath = "Assets/Resources/SpaceCanvasMaterials/RoundedPanel.mat";
        public int callbackOrder => -1050;
        public void OnPreprocessBuild(BuildReport report) => Ensure();
        [InitializeOnLoadMethod]
        private static void Schedule() { EditorApplication.delayCall += Ensure; }
        public static void Ensure()
        {
            var shader = Shader.Find("SpaceCanvas/UI/RoundedPanel");
            if (shader == null) throw new InvalidOperationException("缺少圆角面板抗锯齿着色器。");
            if (ShaderUtil.ShaderHasError(shader)) throw new BuildFailedException("圆角面板着色器编译失败。");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                if (!AssetDatabase.IsValidFolder("Assets/Resources/SpaceCanvasMaterials")) AssetDatabase.CreateFolder("Assets/Resources", "SpaceCanvasMaterials");
                material = new Material(shader) { name = "SpaceCanvas Rounded Panel" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            if (material.shader != shader) { material.shader = shader; EditorUtility.SetDirty(material); }
            AssetDatabase.SaveAssetIfDirty(material);
        }
    }
}
