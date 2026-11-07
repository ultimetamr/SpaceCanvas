using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SpaceCanvas.ProjectIO.Editor.Tests
{
    public sealed class RoundedPanelRenderingTests
    {
        private static Mesh BuildMesh(SpaceCanvasRoundedPanel panel)
        {
            var mesh = new Mesh();
            using (var helper = new VertexHelper())
            {
                typeof(SpaceCanvasRoundedPanel).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Invoke(panel, new object[] { helper });
                helper.FillMesh(mesh);
            }
            return mesh;
        }

        [Test] public void SharedMaterialKeepsPerPanelShapeAndOriginalHitBounds()
        {
            SpaceCanvasPanelMaterialBuilder.Ensure();
            var host = new GameObject("Panel canvas", typeof(RectTransform), typeof(Canvas));
            var a = new GameObject("Panel A", typeof(RectTransform), typeof(SpaceCanvasRoundedPanel));
            var b = new GameObject("Panel B", typeof(RectTransform), typeof(SpaceCanvasRoundedPanel));
            Mesh meshA = null, meshB = null;
            try
            {
                a.transform.SetParent(host.transform, false); b.transform.SetParent(host.transform, false);
                var rect = a.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(96, 64);
                var panelA = a.GetComponent<SpaceCanvasRoundedPanel>(); panelA.radius = 16; panelA.borderColor = Color.red;
                var panelB = b.GetComponent<SpaceCanvasRoundedPanel>(); panelB.radius = 8; panelB.borderColor = Color.green;
                meshA = BuildMesh(panelA); meshB = BuildMesh(panelB);
                Assert.AreSame(panelA.material, panelB.material);
                Assert.AreEqual("SpaceCanvas/UI/RoundedPanel", panelA.material.shader.name);
                var shapeA = new List<Vector4>(); var shapeB = new List<Vector4>();
                meshA.GetUVs(1, shapeA); meshB.GetUVs(1, shapeB);
                Assert.AreEqual(16, shapeA[0].x); Assert.AreEqual(8, shapeB[0].x);
                var colourA = new List<Vector4>(); var colourB = new List<Vector4>();
                meshA.GetUVs(2, colourA); meshB.GetUVs(2, colourB);
                Assert.AreEqual(new Vector4(1, 0, 0, 1), colourA[0]);
                Assert.AreEqual(new Vector4(0, 1, 0, 1), colourB[0]);
                Assert.AreEqual(new Vector2(96, 64), rect.sizeDelta);
                Assert.IsTrue(panelA.raycastTarget);
                Assert.IsTrue((host.GetComponent<Canvas>().additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord2) != 0);
            }
            finally
            {
                if (meshA != null) Object.DestroyImmediate(meshA);
                if (meshB != null) Object.DestroyImmediate(meshB);
                Object.DestroyImmediate(host);
            }
        }

        [Test] public void RenderedSilhouetteHasSmoothPixelCoverageAndTransparentCorners()
        {
            SpaceCanvasPanelMaterialBuilder.Ensure();
            var host = new GameObject("Panel AA render", typeof(RectTransform), typeof(SpaceCanvasRoundedPanel));
            var panel = host.GetComponent<SpaceCanvasRoundedPanel>();
            ((RectTransform)host.transform).sizeDelta = new Vector2(96, 64);
            panel.color = Color.white; panel.radius = 16; panel.topLight = 0; panel.borderWidth = 0;
            var mesh = BuildMesh(panel);
            var target = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var image = new Texture2D(256, 256, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            var command = new CommandBuffer { name = "SpaceCanvas UI pixel coverage validation" };
            try
            {
                target.Create();
                command.SetRenderTarget(target); command.ClearRenderTarget(false, true, Color.clear);
                command.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(-64, 64, -64, 64, .1f, 10));
                command.DrawMesh(mesh, Matrix4x4.Translate(Vector3.back), panel.material, 0, 0);
                Graphics.ExecuteCommandBuffer(command);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); image.Apply();
                var pixels = image.GetPixels32(); var partial = 0;
                foreach (var pixel in pixels) if (pixel.a > 5 && pixel.a < 250) partial++;
                Assert.Greater(image.GetPixel(128, 128).a, .95f, "Panel centre disappeared");
                Assert.Less(image.GetPixel(35, 67).a, .05f, "Round corner was filled as a square");
                Assert.Greater(partial, 100, "Silhouette has hard binary coverage; head movement would shimmer");
                Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/Panel-AA-Validation.png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; command.Dispose(); target.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(image);
                Object.DestroyImmediate(mesh); Object.DestroyImmediate(host);
            }
        }
    }
}
