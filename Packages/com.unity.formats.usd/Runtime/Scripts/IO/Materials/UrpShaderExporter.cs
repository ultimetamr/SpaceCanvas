using UnityEngine;
using USD.NET;

namespace Unity.Formats.USD
{
    public class UrpShaderExporter : ShaderExporterBase
    {
        private const string BASE_MAP_PROPERTY_NAME = "_BaseMap";
        private const string BASE_COLOR_PROPERTY_NAME = "_BaseColor";

        private const string SPECULAR_MAP_PROPERTY_NAME = "_SpecGlossMap";
        private const string SPECULAR_COLOR_PROPERTY_NAME = "_SpecColor";

        private const string NORMAL_MAP_PROPERTY_NAME = "_BumpMap";

        private const string METALLIC_MAP_PROPERTY_NAME = "_MetallicGlossMap";
        private const string METALLIC_VALUE_PROEPRTY_NAME = "_Metallic";

        private const string SMOOTHNESS_VALUE_PROPERTY_NAME = "_Smoothness";

        private const string EMISSION_COLOR_PROPERTY_NAME = "_EmissionColor";
        private const string EMISSION_MAP_PROPERTY_NAME = "_EmissionMap";

        public static void ExportLit(Scene scene,
            string usdShaderPath,
            Material mat,
            UnityPreviewSurfaceSample surface,
            string destTexturePath)
        {
            Vector4 c;

            c = mat.GetColor(BASE_COLOR_PROPERTY_NAME).linear;
            surface.diffuseColor.defaultValue = c;
            // main color export
            if (mat.HasProperty(BASE_MAP_PROPERTY_NAME) && mat.GetTexture(BASE_MAP_PROPERTY_NAME) != null)
            {
                var colorTex = SetupTexture(scene, usdShaderPath, mat, surface, c, destTexturePath, BASE_MAP_PROPERTY_NAME,
                    "rgb");
                surface.diffuseColor.SetConnectedPath(colorTex);
            }

            // opacity export
            if (mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
            {
                c = mat.GetColor(BASE_COLOR_PROPERTY_NAME).linear;
                surface.opacity.defaultValue = c.w; // alpha channel
                if (mat.HasProperty(BASE_MAP_PROPERTY_NAME) &&
                    mat.GetTexture(BASE_MAP_PROPERTY_NAME) != null)
                {
                    var scale = Vector4.one;
                    scale.w = c.w; // alpha channel

                    var alphaTex = SetupTexture(scene, usdShaderPath, mat, surface, scale, destTexturePath,
                        BASE_MAP_PROPERTY_NAME, "a");
                    surface.opacity.SetConnectedPath(alphaTex);
                }

                // alpha test export
                if (mat.IsKeywordEnabled("_ALPHATEST_ON"))
                {
                    surface.opacityThreshold.defaultValue = mat.GetFloat("_Cutoff");
                }
            }

            // skip specular/metallic export if specular hightlights is turned off
            if (!mat.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"))
            {
                if (mat.IsKeywordEnabled("_SPECULAR_SETUP"))
                {

                    // specular material type
                    if (mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP"))
                    {
                        var specTex = SetupTexture(scene, usdShaderPath, mat, surface, c, destTexturePath,
                                       SPECULAR_MAP_PROPERTY_NAME, "rgb");
                        surface.specularColor.SetConnectedPath(specTex);

                    }
                    else
                    {
                        c = mat.GetColor(SPECULAR_COLOR_PROPERTY_NAME).linear;
                        surface.specularColor.defaultValue = c;
                    }
                    surface.useSpecularWorkflow.defaultValue = 1;
                }
                else
                {
                    // metallic material type
                    var metallic = mat.GetFloat(METALLIC_VALUE_PROEPRTY_NAME);
                    surface.metallic.defaultValue = metallic;
                    if (mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP"))
                    {
                        // since unity can't use metallic value and metallic map at the same time, set the scale to 1.0
                        var metallicTex = SetupTexture(scene, usdShaderPath, mat, surface, Vector4.one,
                            destTexturePath, METALLIC_MAP_PROPERTY_NAME, "r");
                        surface.metallic.SetConnectedPath(metallicTex);
                    }
                    surface.useSpecularWorkflow.defaultValue = 0;
                }

                // smoothness
                surface.roughness.defaultValue = 1.0f - mat.GetFloat(SMOOTHNESS_VALUE_PROPERTY_NAME);

                // which texture has smoothness map as alpha channel
                string smoothnessMapPropertyName = mat.GetFloat("_SmoothnessTextureChannel") == 0.0f ?
                   (mat.IsKeywordEnabled("_SPECULAR_SETUP") ? SPECULAR_MAP_PROPERTY_NAME : METALLIC_MAP_PROPERTY_NAME) : BASE_MAP_PROPERTY_NAME;
                if (mat.HasProperty(smoothnessMapPropertyName) && mat.GetTexture(smoothnessMapPropertyName) != null)
                {
                   var scale = Vector4.one;
                   if (mat.HasProperty(SMOOTHNESS_VALUE_PROPERTY_NAME))
                   {
                       scale.w = mat.GetFloat(SMOOTHNESS_VALUE_PROPERTY_NAME);
                   }

                   // TODO(xutong.zhou): extract the alpha channel as RealityKit doesn't support multi-channel texture
                   var smoothnessTex = SetupTexture(scene, usdShaderPath, mat, surface, scale, destTexturePath, smoothnessMapPropertyName,
                       "a", ConversionType.SmoothnessToRoughness);
                   surface.roughness.SetConnectedPath(smoothnessTex);
                }
            } else
            {
                surface.roughness.defaultValue = 0.5f;
            }

            if (mat.IsKeywordEnabled("_NORMALMAP") && mat.HasProperty(NORMAL_MAP_PROPERTY_NAME) && mat.GetTexture(NORMAL_MAP_PROPERTY_NAME) != null)
            {
                var scale = Vector4.one * mat.GetFloat("_BumpScale");
                var normalTex = SetupTexture(scene, usdShaderPath, mat, surface, scale, destTexturePath,
                    NORMAL_MAP_PROPERTY_NAME, "rgb", ConversionType.UnpackNormal);
                surface.normal.defaultValue = scale;
                surface.normal.SetConnectedPath(normalTex);
            }

            if (mat.IsKeywordEnabled("_PARALLAXMAP"))
            {
                var scale = mat.GetFloat("_Parallax");
                var heightTex = SetupTexture(scene, usdShaderPath, mat, surface, scale * Vector4.one, destTexturePath,
                    "_ParallaxMap", "r");
                surface.displacement.defaultValue = scale;
                surface.displacement.SetConnectedPath(heightTex);
            }

            if (mat.IsKeywordEnabled("_OCCLUSIONMAP"))
            {
                var scale = mat.GetFloat("_OcclusionStrength");
                // In URP, the occlusion can be packed into the G channel of a mask map.
                // https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@16.0/manual/lit-shader.html#channel-packing
                // TODO(xutong.zhou): support channel packing later, use r channel for now as there are assets using single channel AO texture
                var occlusionTex = SetupTexture(scene, usdShaderPath, mat, surface, scale * Vector4.one, destTexturePath,
                    "_OcclusionMap", "r");
                surface.occlusion.defaultValue = scale;
                surface.occlusion.SetConnectedPath(occlusionTex);
            }

            if (mat.IsKeywordEnabled("_EMISSION"))
            {
                // importer needs to have HDR support
                Color emissionColor = mat.GetColor(EMISSION_COLOR_PROPERTY_NAME).linear;
                surface.emissiveColor.defaultValue = emissionColor;
                if (mat.HasProperty(EMISSION_MAP_PROPERTY_NAME) && mat.GetTexture(EMISSION_MAP_PROPERTY_NAME) != null)
                {
                    var emissionTex = SetupTexture(scene, usdShaderPath, mat, surface, emissionColor, destTexturePath,
                        EMISSION_MAP_PROPERTY_NAME, "rgb");
                    surface.emissiveColor.SetConnectedPath(emissionTex);
                }
            }
        }

        public static void ExportUnlit(Scene scene,
            string usdShaderPath,
            Material mat,
            UnityPreviewSurfaceSample surface,
            string destTexturePath)
        {
            Vector4 c;

            c = mat.GetColor(BASE_COLOR_PROPERTY_NAME).linear;
            surface.diffuseColor.defaultValue = c;
            // main color export
            if (mat.HasProperty(BASE_MAP_PROPERTY_NAME) && mat.GetTexture(BASE_MAP_PROPERTY_NAME) != null)
            {
                var colorTex = SetupTexture(scene, usdShaderPath, mat, surface, c, destTexturePath,
                    BASE_MAP_PROPERTY_NAME,
                    "rgb");
                surface.diffuseColor.SetConnectedPath(colorTex);
            }

            // opacity export
            if (mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
            {
                c = mat.GetColor(BASE_COLOR_PROPERTY_NAME).linear;
                surface.opacity.defaultValue = c.w; // alpha channel
                if (mat.HasProperty(BASE_MAP_PROPERTY_NAME) &&
                    mat.GetTexture(BASE_MAP_PROPERTY_NAME) != null)
                {
                    var scale = Vector4.one;
                    scale.w = c.w; // alpha channel

                    var alphaTex = SetupTexture(scene, usdShaderPath, mat, surface, scale, destTexturePath,
                        BASE_MAP_PROPERTY_NAME, "a");
                    surface.opacity.SetConnectedPath(alphaTex);
                }

                // alpha test export
                if (mat.IsKeywordEnabled("_ALPHATEST_ON"))
                {
                    surface.opacityThreshold.defaultValue = mat.GetFloat("_Cutoff");
                }
            }
        }
    }
}
