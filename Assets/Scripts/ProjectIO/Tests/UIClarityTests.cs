using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Tests
{
    /// <summary>Clarity must apply to inactive panels without changing spatial placement or hit testing.</summary>
    public sealed class UIClarityTests
    {
        [TestCase(14, 18)]
        [TestCase(18, 18)]
        [TestCase(30, 30)]
        public void TextConfigurationIsIdempotent(int original, int expected)
        {
            var host = new GameObject("Clarity text", typeof(RectTransform), typeof(TextMeshProUGUI));
            try
            {
                var text = host.GetComponent<TextMeshProUGUI>();
                text.fontSize = original;
                text.rectTransform.sizeDelta = new Vector2(320, 24);
                text.raycastTarget = false;
                SpaceCanvasUIClarity.ConfigureText(text);
                SpaceCanvasUIClarity.ConfigureText(text);
                Assert.AreEqual(expected, text.fontSize);
                Assert.IsTrue(text.extraPadding);
                Assert.IsFalse(text.raycastTarget);
                if (expected == 18) Assert.AreEqual(27, text.rectTransform.rect.height);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void InactiveNestedCanvasKeepsPlacementAndSorting()
        {
            var host = new GameObject("Clarity canvas", typeof(RectTransform), typeof(Canvas));
            var panel = new GameObject("Inactive panel", typeof(RectTransform), typeof(Canvas));
            try
            {
                panel.transform.SetParent(host.transform, false);
                panel.transform.localPosition = new Vector3(200, 10, -30);
                var canvas = panel.GetComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = 420;
                canvas.pixelPerfect = true;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord2;
                panel.SetActive(false);
                SpaceCanvasUIClarity.Configure(host.transform);
                SpaceCanvasUIClarity.Configure(host.transform);
                var required = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal
                    | AdditionalCanvasShaderChannels.Tangent | AdditionalCanvasShaderChannels.TexCoord2;
                Assert.AreEqual(required, canvas.additionalShaderChannels & required);
                Assert.IsFalse(canvas.pixelPerfect);
                Assert.IsTrue(canvas.overrideSorting);
                Assert.AreEqual(420, canvas.sortingOrder);
                Assert.AreEqual(new Vector3(200, 10, -30), panel.transform.localPosition);
                Assert.IsFalse(panel.activeSelf);
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
