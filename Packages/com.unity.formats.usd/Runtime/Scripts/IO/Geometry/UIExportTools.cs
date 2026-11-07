using System.Collections.Generic;
using UnityEngine;
using USD.NET.Unity;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro;

//  A utility class for exporting Unity UI elements into USD files
//  Generates corresponding meshes for UI elements and forwards them to the existing mesh export API
//  Handles nodes with CanvasRenderer + an Image component, or with only an Image component.

namespace Unity.Formats.USD
{
    public static class UIExportTools
    {

        //  We map Unity's UI/Default shader to one of two supported options
        //
        public static string UIReplacementShader =>
            GraphicsSettings.currentRenderPipeline == null ?
                "Unlit/Transparent" :
                "Universal Render Pipeline/Unlit";

        private static string AlphaChannelToRedShader => "Hidden/USD/Texture Alpha To Red";

        //  Handle Unity Canvas UI elements as meshes
        //
        public static void ExportCanvasRenderer
            (ObjectContext objContext, ExportContext exportContext)
        {
            CanvasRenderer canvasRenderer = objContext.gameObject.GetComponent<CanvasRenderer>();
            Graphic uiGraphic = objContext.gameObject.GetComponent<Graphic>();

            if (canvasRenderer == null || uiGraphic == null)
            {
                return;
            }

            uiGraphic.Rebuild(CanvasUpdate.PreRender);

            var canvasMesh = canvasRenderer.GetMesh();
            if (canvasMesh == null || canvasMesh.vertexCount == 0)
            {
                return;
            }

            //  Ensure that double-sided rendering is set for this mesh
            //
            MeshSample sample = objContext.sample as MeshSample;
            if (sample != null)
            {
                sample.doubleSided = true;
            }

            Material uiMaterial = objContext.additionalData as Material;

            UnityEngine.Profiling.Profiler.BeginSample("USD: Canvas UI Element");

            MeshExporter.ExportMesh(objContext,
                exportContext,
                canvasMesh,
                uiMaterial,
                new Material[]{});

            UnityEngine.Profiling.Profiler.EndSample();
        }

        public static void ExportSpriteRenderer
            (ObjectContext objContext, ExportContext exportContext)
        {
            SpriteRenderer spriteRenderer = objContext.gameObject.GetComponent<SpriteRenderer>();
            Sprite uiSprite = spriteRenderer.sprite;
            Mesh uiMesh = uiSprite ? ExtractMeshFromSprite(uiSprite): null;

            //  Ensure that double-sided rendering is set for this mesh
            //
            MeshSample sample = objContext.sample as MeshSample;
            if (sample != null)
            {
                sample.doubleSided = true;
            }

            Material uiMaterial = objContext.additionalData as Material;

            UnityEngine.Profiling.Profiler.BeginSample("USD: Sprite UI Element");

            MeshExporter.ExportMesh(objContext,
                exportContext,
                uiMesh,
                uiMaterial,
                new Material[]{});

            UnityEngine.Profiling.Profiler.EndSample();
        }

public static void ExportTextMeshPro
            (ObjectContext objContext, ExportContext exportContext)
        {
            TMP_Text textMeshProComponent = objContext.gameObject.GetComponent<TMP_Text>();

            textMeshProComponent.ForceMeshUpdate();
            Mesh textMesh = textMeshProComponent.mesh;

            Material textMaterial = objContext.additionalData as Material;

            textMaterial.color *= textMeshProComponent.color;

            UnityEngine.Profiling.Profiler.BeginSample("USD: TextMeshPro UI Element");

            MeshExporter.ExportMesh(objContext,
                exportContext,
                textMesh,
                textMaterial,
                new Material[]{});

            UnityEngine.Profiling.Profiler.EndSample();
        }

        //  Helper function for generating a mesh from a UI sprite
        //
        private static Mesh ExtractMeshFromSprite(Sprite sprite)
        {
            var vertices = sprite.vertices;
            var mesh = new Mesh();
            var newVerts = new List<Vector3>();

            for (var i = 0; i < vertices.Length; i++)
            {
                newVerts.Add(vertices[i]);
            }

            mesh.SetVertices(newVerts);
            mesh.SetTriangles(sprite.triangles, 0);
            mesh.SetUVs(0, sprite.uv);
            mesh.RecalculateBounds();

            return mesh;
        }

        public static Texture2D ConvertTextureRedToAlpha(Texture2D texture)
        {
            Material flipMaterial = new Material(Shader.Find(AlphaChannelToRedShader));
            Texture2D temp = texture;

            flipMaterial.SetTexture("_TextureToFlip", temp);
            return ProcessTextureMaterial(temp, flipMaterial, true);
        }

        private static Texture2D ProcessTextureMaterial(Texture2D texture, Material blitMaterial, bool isRGB=false)
        {
            bool isMonoColor = texture.format == TextureFormat.Alpha8 || texture.format == TextureFormat.R8;
            TextureFormat textureFormat = TextureFormat.ARGB32;
            if (isMonoColor) textureFormat = TextureFormat.R8;
            if (isRGB) textureFormat = TextureFormat.RGB24;

            var exportTexture = new Texture2D(texture.width, texture.height, textureFormat, false);
            exportTexture.name = texture.name;

            RenderTextureFormat rtFormat = isMonoColor ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
            var renderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 32, rtFormat);
            Graphics.Blit(exportTexture, renderTexture, blitMaterial);
            RenderTexture.active = renderTexture;

            exportTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            exportTexture.Apply();

            RenderTexture.ReleaseTemporary(renderTexture);

            return exportTexture;
        }
    }
}
