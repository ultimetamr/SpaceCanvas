using TMPro;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// Shared settings for saved and runtime-created world-space panels. Nested sorting
    /// canvases need TMP's SDF UV channel too; pixel snapping is unsuitable for tracked 3D UI.
    /// Does not alter panel positions, object transforms, raycasts, or modal sorting.
    /// </summary>
    public static class SpaceCanvasUIClarity
    {
        public static void ConfigureCanvas(Canvas canvas)
        {
            if (canvas == null) return;
            canvas.pixelPerfect = false;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.TexCoord2
                | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
        }

        public static void Configure(Transform root)
        {
            if (root == null) return;
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) ConfigureCanvas(canvas);
            foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true)) ConfigureText(text);
        }

        public static void ConfigureText(TextMeshProUGUI text)
        {
            if (text == null) return;
            // 14/16 px captions were too fine at the reviewed 1.5m workbench distance.
            // A small, bounded increase fits existing label boxes without changing the layout.
            if (!text.enableAutoSizing && text.fontSize > 0 && text.fontSize < 18)
                text.fontSize = 18;
            // CJK line metrics need more height than the visible glyph. Retain the
            // label centre and its anchors while allowing the new caption size to fit.
            if (!text.enableAutoSizing && Mathf.Approximately(text.fontSize, 18)
                && text.rectTransform.rect.height > 0 && text.rectTransform.rect.height < 27)
            {
                var size = text.rectTransform.sizeDelta;
                size.y += 27 - text.rectTransform.rect.height;
                text.rectTransform.sizeDelta = size;
            }
            // Expand glyph geometry so SDF edge pixels are not clipped at oblique angles.
            text.extraPadding = true;
        }
    }
}
