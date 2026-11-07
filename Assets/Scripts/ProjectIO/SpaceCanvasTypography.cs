using TMPro;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>所有运行时 UI 共用可导出的 TMP SDF 字库，避免旧版动态字体不受 Spatial 导出器支持。</summary>
    public static class SpaceCanvasTypography
    {
        public static TMP_FontAsset Font { get { return Resources.Load<TMP_FontAsset>("SpaceCanvasFonts/SpaceCanvasChineseSDF"); } }
        public static TextAlignmentOptions Alignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }
    }
}
