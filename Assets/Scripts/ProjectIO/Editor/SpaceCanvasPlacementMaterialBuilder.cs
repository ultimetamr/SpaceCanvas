using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceCanvas.ProjectIO.Editor
{
    /// <summary>
    /// Persist a transparent URP variant before Android shader stripping runs.
    /// Preview transparency must not depend on enabling an opaque-only shader variant at runtime.
    /// </summary>
    public static class SpaceCanvasPlacementMaterialBuilder
    {
        private const string AssetPath = "Assets/Resources/SpaceCanvasMaterials/PlacementPreview.mat";

        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            EditorApplication.delayCall += () =>
            {
                if (!AssetDatabase.LoadAssetAtPath<Material>(AssetPath)) Ensure();
            };
        }

        public static void Ensure()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("缺少 URP Unlit 预览着色器。");
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetPath);
            if (material == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                if (!AssetDatabase.IsValidFolder("Assets/Resources/SpaceCanvasMaterials")) AssetDatabase.CreateFolder("Assets/Resources", "SpaceCanvasMaterials");
                material = new Material(shader) { name = "SpaceCanvas Placement Preview" };
                AssetDatabase.CreateAsset(material, AssetPath);
            }
            material.shader = shader;
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_AlphaClip", 0);
            material.SetColor("_BaseColor", new Color(1, 1, 1, .38f));
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("DepthNormalsOnly", false);
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }
    }
}
