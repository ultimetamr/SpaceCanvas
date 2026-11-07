using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// 在 Update 阶段为内容发生变化的 TMP 文本预热，避免首次补字发生在布局循环内部。
    /// 只在文本内容/字号发生变化时请求字形，不在每帧分配完整字库。
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class SpaceCanvasFontWarmup : MonoBehaviour
    {
        private readonly Dictionary<TMP_Text,string> previous = new Dictionary<TMP_Text,string>();
        private float nextScan;
        private void Update()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + .2f;
            var texts = FindObjectsOfType<TMP_Text>();
            var changed = false;
            foreach (var text in texts)
            {
                if (text.font == null) continue;
                var key = text.fontSize + ":" + (int)text.fontStyle + ":" + text.text;
                if (previous.TryGetValue(text, out var last) && last == key) continue;
                previous[text] = key;
                text.ForceMeshUpdate();
                changed = true;
            }
            if (changed) Canvas.ForceUpdateCanvases();
            // 清除已销毁项，避免长时间导入/切换选择累积引用。
            if (previous.Count > texts.Length * 2 + 32) previous.Clear();
        }
    }
}
