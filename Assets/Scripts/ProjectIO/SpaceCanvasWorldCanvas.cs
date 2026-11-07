using UnityEngine;
using UnityEngine.UI;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>将现有 uGUI 置于固定的空间工作台平面。无头部父子绑定、无自造窗口 API。</summary>
    public static class SpaceCanvasWorldCanvas
    {
        public static void Configure(Canvas canvas, Camera camera)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            SpaceCanvasUIClarity.ConfigureCanvas(canvas);
            var rect = (RectTransform)canvas.transform;
            rect.sizeDelta = new Vector2(1600, 1000);
            rect.position = new Vector3(0, .1f, -.50f);
            rect.rotation = Quaternion.identity;
            rect.localScale = Vector3.one * .001125f;
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null) { scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.dynamicPixelsPerUnit = 1; }
        }
    }
}
