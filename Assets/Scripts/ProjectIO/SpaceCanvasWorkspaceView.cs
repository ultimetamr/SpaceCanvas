using UnityEngine;
using UnityEngine.UI;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// 工作空间查看层：只旋转 Editable Content，不旋转 XR 相机、工具面板或工程根节点。
    /// 物体始终保存父节点下的局部坐标，因此查看方向不会写入 JSON、撤销或恢复记录。
    /// 绕工程原点旋转，保持放置平面、模型与编辑坐标轴使用同一坐标系。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasWorkspaceView : MonoBehaviour
    {
        public enum Preset { Front, Side, Back, Top }
        private Transform content;
        private SpaceCanvasWorkspaceUI workspace;
        private Vector3 originalPosition;
        private Vector3 originalScale, fromPosition, toPosition, fromScale, toScale;
        private Quaternion originalRotation;
        private Quaternion fromRotation, toRotation;
        private float elapsed;
        private float yaw, pitch;
        private const float Duration = .3f;
        public bool IsTransitioning { get; private set; }
        public float Yaw => yaw;
        public float Pitch => pitch;

        public static SpaceCanvasWorkspaceView Current => SpaceCanvasRuntime.ContentRoot == null
            ? null : SpaceCanvasRuntime.ContentRoot.GetComponentInParent<SpaceCanvasWorkspaceView>();
        public static bool InputBlocked => Current != null && Current.IsTransitioning;

        /// <summary>Idempotent startup/migration for existing scenes; never reparents user objects.</summary>
        public static SpaceCanvasWorkspaceView Ensure(SpaceCanvasWorkspace host)
        {
            if (host == null || host.contentRoot == null) return null;
            var view = host.GetComponent<SpaceCanvasWorkspaceView>();
            if (view == null) view = host.gameObject.AddComponent<SpaceCanvasWorkspaceView>();
            view.Initialize(host.contentRoot, host.GetComponent<SpaceCanvasWorkspaceUI>());
            return view;
        }

        public void Initialize(Transform root, SpaceCanvasWorkspaceUI ui = null)
        {
            workspace = ui;
            if (root == content) return;
            RestoreOriginal();
            content = root;
            if (content == null) return;
            originalPosition = content.localPosition;
            originalScale = content.localScale;
            originalRotation = content.localRotation;
            yaw = pitch = 0;
        }

        public void Turn(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return;
            // A turn always returns from top view to the upright orbit.
            SetAngles(Mathf.Repeat(yaw + Mathf.Clamp(degrees, -180, 180) + 180, 360) - 180, 0, true);
        }

        public void Show(Preset preset, bool animate = true)
        {
            switch (preset)
            {
                case Preset.Side: SetAngles(90, 0, animate); break;
                case Preset.Back: SetAngles(180, 0, animate); break;
                case Preset.Top: SetAngles(0, -90, animate); break;
                default: SetAngles(0, 0, animate); break;
            }
        }

        /// <summary>View reset is independent of scene edits and does not consume an undo step.</summary>
        public void ResetView() { Show(Preset.Front); }

        private void SetAngles(float newYaw, float newPitch, bool animate)
        {
            if (content == null) return;
            workspace?.FinishPropertyEdit();
            workspace?.placement?.Cancel();
            if (workspace != null && workspace.editSession != null)
            {
                workspace.editSession.Handles?.CancelDrag();
                workspace.editSession.EndGesture(false);
            }
            yaw = newYaw; pitch = newPitch;
            fromRotation = content.localRotation;
            fromPosition = content.localPosition; fromScale = content.localScale;
            // Pitch is in the baseline viewing frame, yaw in project space.
            toRotation = originalRotation * Quaternion.Euler(pitch, 0, 0) * Quaternion.Euler(0, yaw, 0);
            // The top view projects the full floor vertically. Fit the default workbench's
            // central gap so its lower edge does not hide behind the fixed toolbars.
            toPosition = originalPosition + (pitch < -45 ? Vector3.up * .12f : Vector3.zero);
            toScale = originalScale * (pitch < -45 ? .72f : 1f);
            elapsed = 0;
            IsTransitioning = animate && Application.isPlaying && (Quaternion.Angle(fromRotation, toRotation) > .01f
                || Vector3.Distance(fromPosition, toPosition) > .0001f || Vector3.Distance(fromScale, toScale) > .0001f);
            if (!IsTransitioning) Apply(toRotation, toPosition, toScale);
        }

        private void Update()
        {
            if (!IsTransitioning || content == null) return;
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(elapsed / Duration);
            var blend = t * t * (3 - 2 * t);
            Apply(Quaternion.Slerp(fromRotation, toRotation, blend), Vector3.Lerp(fromPosition, toPosition, blend), Vector3.Lerp(fromScale, toScale, blend));
            if (t >= 1) { Apply(toRotation, toPosition, toScale); IsTransitioning = false; }
        }

        private void Apply(Quaternion rotation, Vector3 position, Vector3 scale)
        {
            content.localPosition = position;
            content.localRotation = rotation;
            content.localScale = scale;
        }

        private void RestoreOriginal()
        {
            IsTransitioning = false;
            if (content != null) Apply(originalRotation, originalPosition, originalScale);
        }

        private void OnDisable() { RestoreOriginal(); yaw = pitch = 0; }

        /// <summary>
        /// Reuses the saved dock and restores delegates after scene loading. The same existing
        /// raycaster and rounded-panel material are used to retain verified PICO input/clarity.
        /// </summary>
        public void BuildOrBindControls(Transform uiRoot, Camera camera)
        {
            if (uiRoot == null) return;
            var existing = uiRoot.Find("工作空间视角");
            Transform dock;
            if (existing != null) dock = existing;
            else
            {
                var panel = SpaceCanvasPresentationStyle.SurfacePanel(uiRoot, "工作空间视角", Vector2.zero,
                    new Vector2(760, 60), SpaceCanvasPresentationStyle.Surface, 16);
                SpaceCanvasPresentationStyle.Dock(panel.gameObject, new Vector3(0, -302, -220), new Vector2(760, 60), 0, 200);
                dock = panel.transform;
                SpaceCanvasPresentationStyle.Label(dock, "View heading", "视角", new Vector2(-337, 0), new Vector2(56, 40), 18, true);
                var names = new[] { "左转", "右转", "正面", "侧面", "背面", "俯视", "复位" };
                for (var i = 0; i < names.Length; i++)
                {
                    var button = SpaceCanvasPresentationStyle.Button(dock, names[i], names[i],
                        new Vector2(-260 + i * 96, 0), new Vector2(88, 44), null);
                    button.GetComponentInChildren<TMPro.TextMeshProUGUI>().fontSize = 18;
                }
            }
            var canvas = dock.GetComponent<Canvas>();
            if (canvas != null) canvas.worldCamera = camera;
            foreach (var button in dock.GetComponentsInChildren<Button>(true))
            {
                button.onClick.RemoveAllListeners();
                switch (button.name)
                {
                    case "左转": button.onClick.AddListener(() => Turn(-45)); break;
                    case "右转": button.onClick.AddListener(() => Turn(45)); break;
                    case "正面": button.onClick.AddListener(() => Show(Preset.Front)); break;
                    case "侧面": button.onClick.AddListener(() => Show(Preset.Side)); break;
                    case "背面": button.onClick.AddListener(() => Show(Preset.Back)); break;
                    case "俯视": button.onClick.AddListener(() => Show(Preset.Top)); break;
                    case "复位": button.onClick.AddListener(ResetView); break;
                }
            }
        }
    }
}
