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

using System;
using System.Collections.Generic;
using UnityEngine;
using USD.NET;
using USD.NET.Unity;

namespace Unity.Formats.USD
{
    public static class MaterialExporter
    {
        public delegate void ExportHandler(Scene scene,
            string usdShaderPath,
            Material material,
            UnityPreviewSurfaceSample surface,
            string destTexturePath);

        public static readonly string kStandardFallbackHandler = "USD.NET/Fallback/Standard";
        public static readonly string kHdrpFallbackHandler = "USD.NET/Fallback/HDRP";
        public static readonly string kLwrpFallbackHandler = "USD.NET/Fallback/LWRP";

        /// <summary>
        /// The ExportRegistry is a mapping from Unity shader name (e.g. "Standard") to ExportHandler.
        /// When exporting a Material, the shader name is used to lookup the export hanlder function
        /// in this registry.
        /// </summary>
        static public Dictionary<string, ExportHandler> ExportRegistry
            = new Dictionary<string, ExportHandler>();

        static MaterialExporter()
        {
            ExportRegistry.Add("Standard",
                StandardShaderExporter.ExportStandard);
            ExportRegistry.Add("Standard (Roughness setup)",
                StandardShaderExporter.ExportStandardRoughness);
            ExportRegistry.Add("Standard (Specular setup)",
                StandardShaderExporter.ExportStandardSpecular);

            ExportRegistry.Add("Unlit/Texture", StandardShaderExporter.ExportStandardCommon);
            ExportRegistry.Add("Unlit/Color", StandardShaderExporter.ExportStandardCommon);
            ExportRegistry.Add("Unlit/Transparent", StandardShaderExporter.ExportStandardCommon);
            ExportRegistry.Add("Unlit/Transparent Cutout", StandardShaderExporter.ExportStandardCommon);

            ExportRegistry.Add("HDRenderPipeline/Lit",
                HdrpShaderExporter.ExportLit);
            ExportRegistry.Add("HDRenderPipeline/LitTessellation",
                HdrpShaderExporter.ExportLit);
            ExportRegistry.Add("HDRenderPipeline/LayeredLit",
                HdrpShaderExporter.ExportLit);
            ExportRegistry.Add("HDRenderPipeline/LayeredLitTessellation",
                HdrpShaderExporter.ExportLit);

            ExportRegistry.Add("HDRP/Lit",
                HdrpShaderExporter.ExportLit);
            ExportRegistry.Add("HDRP/LitTessellation",
                HdrpShaderExporter.ExportLit);
            ExportRegistry.Add("HDRP/LayeredLit",
                HdrpShaderExporter.ExportLit);
            ExportRegistry.Add("HDRP/LayeredLitTessellation",
                HdrpShaderExporter.ExportLit);

            ExportRegistry.Add("Universal Render Pipeline/Lit",
                UrpShaderExporter.ExportLit);
            ExportRegistry.Add("Universal Render Pipeline/Unlit",
                UrpShaderExporter.ExportUnlit);
            ExportRegistry.Add("Universal Render Pipeline/Particles Unlit",
                UrpShaderExporter.ExportUnlit);

            ExportRegistry.Add(kStandardFallbackHandler,
                StandardShaderExporter.ExportGeneric);
        }

        private static MaterialSample.BlendingMode GetRenderingMode(Material mat)
        {
            if (mat.HasProperty("_Mode"))
            {
                int mode = mat.GetInt("_Mode");

                switch (mode)
                {
                    case 0:
                        return MaterialSample.BlendingMode.BLEND_OPAQUE;
                    case 1: // alpha cutoff
                        return MaterialSample.BlendingMode.BLEND_TRANSPARENT;
                    case 2:
                        return MaterialSample.BlendingMode.BLEND_FADE;
                    case 3: // alpha blend
                        return MaterialSample.BlendingMode.BLEND_TRANSPARENT;
                    default:
                        return MaterialSample.BlendingMode.BLEND_OPAQUE;
                }
            }
            else if (mat.HasProperty("_Surface"))
            {
                int mode = mat.GetInt("_Surface");

                switch (mode)
                {
                    case 0:
                        return MaterialSample.BlendingMode.BLEND_OPAQUE;
                    case 1:
                        return MaterialSample.BlendingMode.BLEND_TRANSPARENT;
                    default:
                        return MaterialSample.BlendingMode.BLEND_OPAQUE;
                }
            }
            else if (mat.shader.name.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MaterialSample.BlendingMode.BLEND_TRANSPARENT;
            }
            else if (mat.shader.name.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MaterialSample.BlendingMode.BLEND_ADD;
            }
            else if (mat.shader.name.IndexOf("Premultiply", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MaterialSample.BlendingMode.BLEND_PREMULTIPLY;
            }
            else if (mat.shader.name.IndexOf("Multiply", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MaterialSample.BlendingMode.BLEND_MULTIPLY;
            }
            else if (mat.shader.name.IndexOf("Mask", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MaterialSample.BlendingMode.BLEND_MASKED;
            }

            return MaterialSample.BlendingMode.BLEND_OPAQUE;
        }

        private static MaterialSample.MaterialType GetMaterialType(Material mat)
        {
            if (mat.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MaterialSample.MaterialType.UNLIT;
            }
            return MaterialSample.MaterialType.PHYSICALLY_BASED;
        }

        public static void ExportMaterial(Scene scene, Material mat, string usdMaterialPath)
        {
            string shaderPath = usdMaterialPath + "/PreviewSurface";

            var material = new MaterialSample();
            material.surface.SetConnectedPath(shaderPath, "outputs:surface");
            material.SetBlendingMode(GetRenderingMode(mat));
            material.SetMaterialType(GetMaterialType(mat));
            var origTime = scene.Time;

            try
            {
                scene.Time = null;
                scene.Write(usdMaterialPath, material);
            }
            finally
            {
                scene.Time = origTime;
            }

            var shader = new UnityPreviewSurfaceSample();
            var texPath = /*TODO: this should be explicit*/
                System.IO.Path.GetDirectoryName(scene.FilePath);

            ExportHandler handler = null;
            if (!ExportRegistry.TryGetValue(mat.shader.name, out handler))
            {
                handler = ExportRegistry[kStandardFallbackHandler];
            }

            if (handler == null)
            {
                Debug.LogException(new System.Exception("Could not find handler to export shader: " + mat.shader.name));
                return;
            }

            try
            {
                scene.Time = null;
                handler(scene, shaderPath, mat, shader, texPath);
                scene.Write(shaderPath, shader);
            }
            finally
            {
                scene.Time = origTime;
            }
        }
    }
}
