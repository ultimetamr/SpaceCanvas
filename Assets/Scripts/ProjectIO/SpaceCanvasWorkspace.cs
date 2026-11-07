using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// 场景的入口组件。持有编辑内容容器、相机和 UI，确保导入不会删除工具窗口。
    /// Bootstrap 在编辑模式下保存默认示例；进入运行模式后只注册已有内容，不重复生成。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasWorkspace : MonoBehaviour
    {
        public Transform contentRoot;
        public Camera viewCamera;
        public SpaceCanvasProjectIOUI projectUI;

        private void Awake()
        {
            SpaceCanvasRuntime.ContentRoot = contentRoot;
            if (contentRoot == null) return;
            var project = contentRoot.GetComponentInChildren<SpaceCanvasProjectRoot>(true);
            if (project != null) SpaceCanvasRuntime.SetCurrentProjectRoot(project);
            // Existing scenes may contain prefab instances created before the premium
            // procedural layer was added. Upgrade them in place without changing JSON data.
            SpaceCanvasRuntime.UpgradeBuiltinAssets();
        }

        private void OnDestroy()
        {
            if (SpaceCanvasRuntime.ContentRoot == contentRoot) SpaceCanvasRuntime.ContentRoot = null;
        }
    }
}
