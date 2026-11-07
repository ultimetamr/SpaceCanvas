// Copyright 2018 Jeremy Cowles. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using UnityEngine;
using USD.NET;

namespace Unity.Formats.USD
{
    public class StandardShaderExporter : ShaderExporterBase
    {
        public static void ExportStandardSpecular(Scene scene,
            string usdShaderPath,
            Material material,
            UnityPreviewSurfaceSample surface,
            string destTexturePath)
        {
            Color c;

            ExportStandardCommon(scene, usdShaderPath, material, surface, destTexturePath);
            surface.useSpecularWorkflow.defaultValue = 1;
            surface.metallic.defaultValue = 0;

            if (material.HasProperty("_SpecGlossMap") && material.GetTexture("_SpecGlossMap") != null)
            {
                var scale = Vector4.one;
                if (material.HasProperty("_SpecColor"))
                {
                    scale = material.GetColor("_SpecColor");
                }

                var newTex = SetupTexture(scene, usdShaderPath, material, surface, scale, destTexturePath,
                    "_SpecGlossMap", "rgb");
                surface.specularColor.SetConnectedPath(newTex);
            }
            else if (material.HasProperty("_SpecColor"))
            {
                // If there is a spec color, then this is not metallic workflow.
                c = material.GetColor("_SpecColor").linear;
                surface.specularColor.defaultValue = new Vector3(c.r, c.g, c.b);
            }
            else
            {
                c = new Color(.5f, .5f, .5f);
                surface.specularColor.defaultValue = new Vector3(c.r, c.g, c.b);
            }

            // TODO: Specular and roughness are combined and the shader configuration dictates
            // where the glossiness comes from (albedo or spec alpha).

            if (material.HasProperty("_Glossiness"))
            {
                surface.roughness.defaultValue = 1 - material.GetFloat("_Glossiness");
            }
            else
            {
                surface.roughness.defaultValue = 0.5f;
            }
        }

        public static void ExportStandardRoughness(Scene scene,
            string usdShaderPath,
            Material material,
            UnityPreviewSurfaceSample surface,
            string destTexturePath)
        {
            ExportStandardCommon(scene, usdShaderPath, material, surface, destTexturePath);
            surface.useSpecularWorkflow.defaultValue = 0;

            if (material.HasProperty("_MetallicGlossMap") && material.GetTexture("_MetallicGlossMap") != null)
            {
                var scale = Vector4.one;
                if (material.HasProperty("_Metallic"))
                {
                    scale.x = material.GetFloat("_Metallic");
                }

                var newTex = SetupTexture(scene, usdShaderPath, material, surface, scale, destTexturePath,
                    "_MetallicGlossMap", "r");
                surface.metallic.SetConnectedPath(newTex);
                scale = Vector4.one;
                scale.x = 1 - material.GetFloat("_Glossiness");
                var roughnessTex = SetupTexture(scene, usdShaderPath, material, surface, scale, destTexturePath,
                    "_MetallicGlossMap", "a");
                surface.roughness.SetConnectedPath(roughnessTex);
            }
            else if (material.HasProperty("_Metallic"))
            {
                surface.metallic.defaultValue = material.GetFloat("_Metallic");
            }
            else
            {
                surface.metallic.defaultValue = .5f;
            }

            if (material.HasProperty("_SpecGlossMap") && material.GetTexture("_SpecGlossMap") != null)
            {
                var scale = Vector4.one;
                if (material.HasProperty("_Glossiness"))
                {
                    scale.x = 1 - material.GetFloat("_Glossiness");
                }

                var newTex = SetupTexture(scene, usdShaderPath, material, surface, scale, destTexturePath,
                    "_SpecGlossMap", "r");
                surface.roughness.SetConnectedPath(newTex);
            }
            else if (material.HasProperty("_Glossiness"))
            {
                surface.roughness.defaultValue = 1 - material.GetFloat("_Glossiness");
            }
            else
            {
                surface.roughness.defaultValue = 0.5f;
            }
        }

        public static void ExportStandard(Scene scene,
            string usdShaderPath,
            Material material,
            UnityPreviewSurfaceSample surface,
            string destTexturePath)
        {
            ExportStandardCommon(scene, usdShaderPath, material, surface, destTexturePath);
            surface.useSpecularWorkflow.defaultValue = 0;

            // commented out the original implementation as it seems to have the following issues
            // 1.unity doesn't support metallic value and metallic map at the same time, when exporting metallic map, scale should always be 1.0. Exporting metallic value as scale here will result mismatched metallic value on exported material
            // 2.when using smoothness map, the scale property is _GlossMapScale rather than _Glossiness
            // 3.exported files are not rendered correctly in RealityKit
            //
            //if (material.HasProperty("_MetallicGlossMap") && material.GetTexture("_MetallicGlossMap") != null)
            //{
            //    var scale = Vector4.one;
            //    if (material.HasProperty("_Metallic"))
            //    {
            //        scale.x = material.GetFloat("_Metallic");
            //    }

            //    var newTex = SetupTexture(scene, usdShaderPath, material, surface, scale, destTexturePath,
            //        "_MetallicGlossMap", "b", ConversionType.SwapRASmoothnessToBGRoughness);
            //    surface.metallic.SetConnectedPath(newTex);
            //    scale = Vector4.one;
            //    scale.x = 1 - material.GetFloat("_Glossiness");
            //    var roughnessTex = SetupTexture(scene, usdShaderPath, material, surface, scale, destTexturePath,
            //        "_MetallicGlossMap", "g", ConversionType.SwapRASmoothnessToBGRoughness);
            //    surface.roughness.SetConnectedPath(roughnessTex);
            //}
            //else if (material.HasProperty("_Metallic"))
            //{
            //    surface.metallic.defaultValue = material.GetFloat("_Metallic");
            //}
            //else
            //{
            //    surface.metallic.defaultValue = .5f;
            //}

            //if (material.HasProperty("_Glossiness"))
            //{
            //    surface.roughness.defaultValue = 1 - material.GetFloat("_Glossiness");
            //}
            //else
            //{
            //    surface.roughness.defaultValue = 0.5f;
            //}

            if (material.HasProperty("_MetallicGlossMap") && material.GetTexture("_MetallicGlossMap") != null)
            {
                // when there is metallic map we shouldn't apply metallic value as scale since Unity doesn't support metallic value over metallic map
                var newTex = SetupTexture(scene, usdShaderPath, material, surface, Vector4.one, destTexturePath,
                    "_MetallicGlossMap", "r");
                surface.metallic.SetConnectedPath(newTex);
            }

            var smoothnessChannel = material.GetFloat("_SmoothnessTextureChannel");
            string smoothnessMapPropertyName = "_MetallicGlossMap";
            if (smoothnessChannel == 1.0f) {
                smoothnessMapPropertyName = "_MainTex";
            }

            if (material.HasProperty(smoothnessMapPropertyName) && material.GetTexture(smoothnessMapPropertyName) != null) {
                //TODO(xutongh.zhou): extract the alpha channel as smoothness map as RealityKit have troubles rendering packed texture
                var scale = Vector4.one;
                // when there is smoothness map _GlossMapScale should be used over _Glossiness
                scale.w = material.GetFloat("_GlossMapScale");
                var roughnessTex = SetupTexture(scene, usdShaderPath, material, surface, scale, destTexturePath,
                    smoothnessMapPropertyName, "a", ConversionType.SmoothnessToRoughness);
                surface.roughness.SetConnectedPath(roughnessTex);
            }


            if (material.HasProperty("_Metallic"))
            {
                surface.metallic.defaultValue = material.GetFloat("_Metallic");
            }
            else
            {
                surface.metallic.defaultValue = .5f;
            }

            if (material.HasProperty("_Glossiness"))
            {
                surface.roughness.defaultValue = 1 - material.GetFloat("_Glossiness");
            }
            else
            {
                surface.roughness.defaultValue = 0.5f;
            }
        }

        public static void ExportGeneric(Scene scene,
            string usdShaderPath,
            Material material,
            UnityPreviewSurfaceSample surface,
            string destTexturePath)
        {
            Color c;
            ExportStandardCommon(scene, usdShaderPath, material, surface, destTexturePath);

            if (material.HasProperty("_SpecColor"))
            {
                // If there is a spec color, then this is not metallic workflow.
                c = material.GetColor("_SpecColor").linear;
            }
            else
            {
                c = new Color(.4f, .4f, .4f);
            }

            surface.specularColor.defaultValue = new Vector3(c.r, c.g, c.b);

            if (material.HasProperty("_Metallic"))
            {
                surface.metallic.defaultValue = material.GetFloat("_Metallic");
            }
            else
            {
                surface.metallic.defaultValue = .5f;
            }

            if (ShouldUseSpecularWorkflow(material))
            {
                if (material.HasProperty("_SpecGlossMap") && material.GetTexture("_SpecGlossMap") != null)
                {
                    var scale = Vector4.one;
                    if (material.HasProperty("_SpecularColor"))
                    {
                        scale = material.GetColor("_SpecularColor");
                    }

                    var newTex = SetupTexture(scene, usdShaderPath, material, surface, scale, destTexturePath,
                        "_SpecGlossMap", "rgb");
                    surface.specularColor.SetConnectedPath(newTex);
                }
                else if (material.HasProperty("_SpecColor"))
                {
                    // If there is a spec color, then this is not metallic workflow.
                    c = material.GetColor("_SpecColor").linear;
                    surface.specularColor.defaultValue = new Vector3(c.r, c.g, c.b);
                }
                else
                {
                    c = new Color(.5f, .5f, .5f);
                    surface.specularColor.defaultValue = new Vector3(c.r, c.g, c.b);
                }

                if (material.HasProperty("_Glossiness"))
                {
                    surface.roughness.defaultValue = 1 - material.GetFloat("_Glossiness");
                }
                else
                {
                    surface.roughness.defaultValue = 0.5f;
                }

                surface.useSpecularWorkflow.defaultValue = 1;
            }
            else
            {
                surface.useSpecularWorkflow.defaultValue = 0;
                if (material.HasProperty("_MetallicGlossMap") && material.GetTexture("_MetallicGlossMap") != null)
                {
                    var scale = Vector4.one;
                    if (material.HasProperty("_Metallic"))
                    {
                        scale.x = material.GetFloat("_Metallic");
                    }

                    var newTex = SetupTexture(scene, usdShaderPath, material, surface, scale, destTexturePath,
                        "_MetallicGlossMap", "r");
                    surface.metallic.SetConnectedPath(newTex);
                }
                else if (material.HasProperty("_Metallic"))
                {
                    surface.metallic.defaultValue = material.GetFloat("_Metallic");
                }
                else
                {
                    surface.metallic.defaultValue = .5f;
                }

                if (material.HasProperty("_Glossiness"))
                {
                    surface.roughness.defaultValue = 1 - material.GetFloat("_Glossiness");
                }
                else
                {
                    surface.roughness.defaultValue = 0.5f;
                }
            }

            if (material.HasProperty("_Smoothness"))
            {
                surface.roughness.defaultValue = 1 - material.GetFloat("_Smoothness");
            }
            else if (material.HasProperty("_Glossiness"))
            {
                surface.roughness.defaultValue = 1 - material.GetFloat("_Glossiness");
            }
            else if (material.HasProperty("_Roughness"))
            {
                surface.roughness.defaultValue = material.GetFloat("_Roughness");
            }
            else
            {
                surface.roughness.defaultValue = 0.5f;
            }
        }

        public static void ExportStandardCommon(Scene scene,
            string usdShaderPath,
            Material mat,
            UnityPreviewSurfaceSample surface,
            string destTexturePath)
        {
            Color c;

            // Export all generic parameter.
            // These are not useful to UsdPreviewSurface, but enable perfect round-tripping.
            surface.unity.shaderName = mat.shader.name;
            surface.unity.shaderKeywords = mat.shaderKeywords;

            // Unfortunately, parameter names can only be discovered generically in-editor.
#if UNITY_EDITOR
            for (int i = 0; i < UnityEditor.ShaderUtil.GetPropertyCount(mat.shader); i++)
            {
                string name = UnityEditor.ShaderUtil.GetPropertyName(mat.shader, i);
                if (!mat.HasProperty(name))
                {
                    continue;
                }

                // Note that for whatever reason, shader properties may be listed multiple times.
                // So dictionary assignment is used here, instead of Add().

                switch (UnityEditor.ShaderUtil.GetPropertyType(mat.shader, i))
                {
                    case UnityEditor.ShaderUtil.ShaderPropertyType.Color:
                        surface.unity.colorArgs[name] = mat.GetColor(name).linear;
                        break;
                    case UnityEditor.ShaderUtil.ShaderPropertyType.Float:
                    case UnityEditor.ShaderUtil.ShaderPropertyType.Range:
                        surface.unity.floatArgs[name] = mat.GetFloat(name);
                        break;
                    case UnityEditor.ShaderUtil.ShaderPropertyType.Vector:
                        surface.unity.vectorArgs[name] = mat.GetVector(name);
                        break;
                }
            }
#endif

            if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null)
            {
                var scale = new Vector4(1, 1, 1, 1);
                if (mat.HasProperty("_Color"))
                    scale = mat.GetColor("_Color").linear;
                var newTex = SetupTexture(scene, usdShaderPath, mat, surface, scale, destTexturePath, "_MainTex",
                    "rgb");
                surface.diffuseColor.SetConnectedPath(newTex);
            }
            else if (mat.HasProperty("_Color"))
            {
                // Standard.
                c = mat.GetColor("_Color").linear;
                surface.diffuseColor.defaultValue = c;
            }
            else
            {
                c = Color.white;
                surface.diffuseColor.defaultValue = c;
            }

            // Standard Shader has 4 modes (magic shader values internally defined in StandardShaderGUI.cs):
            // Opaque - no opacity/transparency
            // Cutout - opacityThreshold should be used to cut off based on alpha values (in _MainTex.a)
            // Fade - opacity should be used, but no opacityThreshold
            // Transparent - opacity should be used, but no opacityThreshold.
            // Note: not quite sure if and how the difference between Fade and Transparent should be handled in USD.
            StandardShaderBlendMode shaderMode = StandardShaderBlendMode.Opaque;
            if (mat.HasProperty("_Mode"))
            {
                shaderMode = (StandardShaderBlendMode)mat.GetFloat("_Mode");
            }

            //  TODO: This is broken - _Surface appears in zero BiRP shaders
            //      and this is causing a lot of shaders with transparency not to export properly
            //      come up with a better solution later
            //
            if (mat.HasProperty("_Surface"))
            {
                if (mat.HasProperty("_AlphaClip") && mat.GetFloat("_AlphaClip") == 1.0f)
                {
                    shaderMode = StandardShaderBlendMode.Cutout;
                }
                else
                {
                    shaderMode = mat.GetFloat("_Surface") == 0.0f
                        ? StandardShaderBlendMode.Opaque
                        : StandardShaderBlendMode.Transparent;
                }
            }

            //  This is a very bad kludge, change later or so help me god
            if (mat.shader.name.Equals("Unlit/Transparent"))
            {
                shaderMode = StandardShaderBlendMode.Transparent;
            }
            if (mat.shader.name.Equals("Unlit/Transparent Cutout"))
            {
                shaderMode = StandardShaderBlendMode.Cutout;
            }

            if (shaderMode != StandardShaderBlendMode.Opaque)
            {
                if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null)
                {
                    var scale = Vector4.one;
                    if (mat.HasProperty("_BaseColor"))
                        scale.w = mat.GetColor("_BaseColor").linear.a;
                    var newTex = SetupTexture(scene, usdShaderPath, mat, surface, scale, destTexturePath, "_MainTex",
                        "a");
                    surface.opacity.SetConnectedPath(newTex);
                }
                else if (mat.HasProperty("_Color"))
                {
                    c = mat.GetColor("_Color").linear;
                    surface.opacity.defaultValue = c.a;
                }
                else
                {
                    c = Color.white;
                    surface.opacity.defaultValue = 1.0f;
                }
            }
            else
            {
                surface.opacity.defaultValue = 1.0f;
            }

            if (shaderMode == StandardShaderBlendMode.Cutout && mat.HasProperty("_Cutoff"))
            {
                surface.opacityThreshold.defaultValue = mat.GetFloat("_Cutoff");
            }

            surface.useSpecularWorkflow.defaultValue = 1;

            if (mat.HasProperty("_BumpMap") && mat.GetTexture("_BumpMap") != null)
            {
                // do not support bumpscale for now
                var newTex = SetupTexture(scene, usdShaderPath, mat, surface, Vector4.one, destTexturePath, "_BumpMap",
                    "rgb", ConversionType.UnpackNormal);
                surface.normal.SetConnectedPath(newTex);
            }

            if (mat.HasProperty("_ParallaxMap") && mat.GetTexture("_ParallaxMap") != null)
            {
                var scale = Vector4.one * mat.GetFloat("_Parallax");
                var newTex = SetupTexture(scene, usdShaderPath, mat, surface, scale, destTexturePath,
                    "_ParallaxMap", "rgb");
                surface.displacement.SetConnectedPath(newTex);
            }

            if (mat.HasProperty("_OcclusionMap") && mat.GetTexture("_OcclusionMap") != null)
            {
                var aoTex = mat.GetTexture("_OcclusionMap");
                // Occlusion maps in built-in standard shaders have the same data in every channel.
                // In URP, the occlusion is packed into the G channel of a mask map.
                // https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@16.0/manual/lit-shader.html#channel-packing
                var scale = Vector4.one * mat.GetFloat("_OcclusionStrength");
                var newTex = SetupTexture(scene, usdShaderPath, mat, surface, scale, destTexturePath,
                    "_OcclusionMap", "r");
                surface.occlusion.SetConnectedPath(newTex);
            }

            /*
            if (mat.HasProperty("_Metallic")) {
              surface.metallic.defaultValue = mat.GetFloat("_Metallic");
            } else {
              surface.metallic.defaultValue = .5f;
            }
            */

            if (mat.IsKeywordEnabled("_EMISSION"))
            {
                if (mat.HasProperty("_EmissionMap") && mat.GetTexture("_EmissionMap") != null)
                {
                    var scale = Vector4.one;
                    if (mat.HasProperty("_EmissionColor"))
                        scale = mat.GetColor("_EmissionColor").linear;
                    var newTex = SetupTexture(scene, usdShaderPath, mat, surface, scale, destTexturePath,
                        "_EmissionMap", "rgb");
                    surface.emissiveColor.SetConnectedPath(newTex);
                }

                if (mat.HasProperty("_EmissionColor"))
                {
                    c = mat.GetColor("_EmissionColor").linear;
                    surface.emissiveColor.defaultValue = c;
                }
            }
        }

        // local version of the internal BlendMode enum in https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/Inspector/StandardShaderGUI.cs
        enum StandardShaderBlendMode
        {
            Opaque = 0,
            Cutout = 1,
            Fade = 2, // Old school alpha-blending mode, fresnel does not affect amount of transparency
            Transparent = 3 // Physically plausible transparency mode, implemented as alpha pre-multiply
        }

        // return true if the material should use specular workflow
        private static bool ShouldUseSpecularWorkflow(Material material)
        {
            // Workflow mode of 0 is Specular and 1 is Metallic
            if (material.HasProperty("_WorkflowMode"))
                return (material.GetFloat("_WorkflowMode") == 0.0f);

            // Gross heuristics to detect specular workflow if workflow mode not set
            return material.IsKeywordEnabled("_SPECGLOSSMAP")
                || material.HasProperty("_SpecColor")
                || material.HasProperty("_SpecularColor")
                || material.shader.name.ToLower().Contains("specular");
        }
    }
}
