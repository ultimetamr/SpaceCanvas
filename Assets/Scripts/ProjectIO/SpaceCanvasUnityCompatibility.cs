using TMPro;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>集中处理 Unity 2022 与 Unity 6 的 API 差异；这些 ID 仅用于本次运行的交互。</summary>
    public static class SpaceCanvasUnityCompatibility
    {
#if UNITY_6000_6_OR_NEWER
        private sealed class InteractionId { public int value; }
        private static readonly ConditionalWeakTable<Object, InteractionId> InteractionIds = new ConditionalWeakTable<Object, InteractionId>();
        private static int nextId;
#endif
        public static int ObjectId(Object value)
        {
            if (value == null) return 0;
#if UNITY_6000_6_OR_NEWER
            // 新 EntityId 为 64 位，不能截断为 int。为旧交互接口分配无碰撞的会话令牌。
            return InteractionIds.GetValue(value, _ => new InteractionId { value = System.Threading.Interlocked.Increment(ref nextId) }).value;
#else
            return value.GetInstanceID();
#endif
        }

        public static void SetTextWrapping(TMP_Text text, bool enabled)
        {
#if UNITY_6000_0_OR_NEWER
            text.textWrappingMode = enabled ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
#else
            text.enableWordWrapping = enabled;
#endif
        }

        public static bool IsTextWrappingEnabled(TMP_Text text)
        {
#if UNITY_6000_0_OR_NEWER
            return text.textWrappingMode != TextWrappingModes.NoWrap;
#else
            return text.enableWordWrapping;
#endif
        }
    }
}
