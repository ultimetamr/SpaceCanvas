using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// 标记已审核的外部模型。真实贴图保留原色，JSON 颜色作为轻量染色叠加。
    /// 修改模型时递增 revision，再通过素材菜单迁移已保存场景。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasCuratedAsset : MonoBehaviour
    {
        public string sourceId;
        public string revision = "polyhaven-1";
        [Range(0, 1)] public float tintStrength = .25f;
    }
}
