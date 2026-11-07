using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>标识可替换的工程根节点；独立文件确保 Unity 能正确保存组件引用。</summary>
    public sealed class SpaceCanvasProjectRoot : MonoBehaviour
    {
        public string projectName;
        public long createTime;
    }
}
