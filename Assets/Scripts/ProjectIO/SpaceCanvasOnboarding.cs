using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// First-run MR coaching. Lives on the existing workbench, never follows the head or edits a project.
    /// The editor persists its nested Canvas so the XR build can install the usual tracked raycaster.
    /// Runtime listeners are rebound once; all automatic progress comes from committed business actions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasOnboarding : MonoBehaviour
    {
        public const string SeenPreference = "SpaceCanvas.Onboarding.Seen.v1";
        public SpaceCanvasWorkspaceUI workspace;
        public SpaceCanvasProjectIOUI projectUI;
        public SpaceCanvasEditSession editSession;
        [SerializeField] private GameObject panel;
        [SerializeField] private TextMeshProUGUI heading, description, progress;
        [SerializeField] private Button primary, dismiss;
        [SerializeField] private Image[] segments;
        private readonly SpaceCanvasTutorialFlow flow = new SpaceCanvasTutorialFlow();
        public SpaceCanvasTutorialFlow Flow => flow;
        public event Action ViewChanged;

        private static readonly string[] Titles = {
            "用一个小作品，认识空间编辑", "01  添加一件喜欢的素材", "02  调整物体的位置", "03  保存你的第一个工程", "04  认识工程与官方模板", "准备好了，开始自由创作" };
        private static readonly string[] Descriptions = {
            "将射线对准“开始体验”。手柄按扳机并松开；手势对准后捏合。\n约 2 分钟，随时可以跳过，不会替换你的作品。",
            "选择左侧素材缩略图，再把射线对准工作台或桌面。\n看到半透明预览后，再按一次扳机或捏合，确认放置。",
            "对准工作台上的物体，按住扳机或捏合，轻轻拖动后松开。\n也可以在右侧属性面板修改位置；下方可切换旋转与缩放。",
            "在底部输入工程名称，再点击“保存工程”。\n保存成功后会自动进入下一步，作品以开放 JSON 格式保存在本机。",
            "点击底部“导入工程”，再切换到“官方示例”查看模板。\n这里只需查看；替换场景前仍会询问确认，查看后关闭面板即可。",
            "下方“撤销”可退回最近一次编辑，最多保留 20 步。\n通过“关于开源”了解工程格式；点击工具栏“?”可随时重看教学。" };

        private void Start()
        {
            if (primary == null || projectUI == null || workspace == null) return;
            primary.onClick.RemoveAllListeners(); primary.onClick.AddListener(ContinueOrSkip);
            dismiss.onClick.RemoveAllListeners(); dismiss.onClick.AddListener(Dismiss);
            workspace.AssetAdded += OnAdded;
            workspace.ObjectEdited += OnEdited;
            projectUI.ProjectSaved += OnSaved;
            projectUI.OfficialExamplesViewed += OnExamplesViewed;
            projectUI.InterfaceChanged += Render;
            if (workspace.placement != null) workspace.placement.ViewChanged += OnPlacementChanged;
            if (editSession != null) editSession.EditCommitted += OnEdited;
            if (!PlayerPrefs.HasKey(SeenPreference)) Restart();
            else Render();
        }

        /// <summary>Explicit replay resets only tutorial progress; scene and undo history are preserved.</summary>
        public void Restart()
        {
            flow.Restart(); Render(); ViewChanged?.Invoke();
            Debug.Log("SpaceCanvas Onboarding: started");
        }

        public void Dismiss()
        {
            flow.Dismiss(); Remember(); Render(); ViewChanged?.Invoke();
            Debug.Log("SpaceCanvas Onboarding: dismissed");
        }

        private void ContinueOrSkip()
        {
            if (!flow.Continue()) return;
            if (!flow.Active) Remember();
            Render(); ViewChanged?.Invoke();
        }

        private void OnAdded(SpaceCanvasEditableObject item) { Record(SpaceCanvasTutorialAction.AssetAdded); }
        private void OnEdited(SpaceCanvasEditableObject item) { Record(SpaceCanvasTutorialAction.ObjectEdited); }
        private void OnSaved(string path) { Record(SpaceCanvasTutorialAction.ProjectSaved); }
        private void OnExamplesViewed() { Record(SpaceCanvasTutorialAction.ExamplesViewed); }
        private void Record(SpaceCanvasTutorialAction action)
        {
            if (!flow.Record(action)) return;
            Debug.Log("SpaceCanvas Onboarding: " + flow.Step);
            Render(); ViewChanged?.Invoke();
        }
        private static void Remember() { PlayerPrefs.SetInt(SeenPreference, 1); PlayerPrefs.Save(); }

        private void Render()
        {
            if (panel == null) return;
            var visible = flow.Active && (projectUI == null || !projectUI.ModalOpen)
                && (workspace == null || workspace.placement == null || !workspace.placement.IsPlacing);
            panel.SetActive(visible);
            if (workspace != null) workspace.SetTutorialVisibility(visible);
            if (!visible) return;
            var index = (int)flow.Step;
            heading.text = Titles[index]; description.text = Descriptions[index];
            progress.text = index == 0 ? "SPACE CANVAS  /  新手体验" : index == 5 ? "教学完成" : "动手试试  " + index + " / 4";
            primary.GetComponentInChildren<TextMeshProUGUI>().text = index == 0 ? "开始体验" : index == 5 ? "开始创作" : "跳过此步";
            dismiss.gameObject.SetActive(index != 5);
            for (var i = 0; i < segments.Length; i++) segments[i].color = i < index ? SpaceCanvasPresentationStyle.Accent : SpaceCanvasPresentationStyle.Card;
        }

        /// <summary>Refreshes an editor preview after changing Flow without persisting user preferences.</summary>
        public void RefreshView() { Render(); }
        private void OnPlacementChanged() { Render(); ViewChanged?.Invoke(); }

        /// <summary>Called by the existing presentation builder before installing XR raycasters and saving.</summary>
        public void BuildView(Transform parent, Camera camera)
        {
            if (panel != null) { if (Application.isPlaying) Destroy(panel); else DestroyImmediate(panel); }
            panel = new GameObject("新手教学", typeof(RectTransform), typeof(SpaceCanvasRoundedPanel));
            panel.transform.SetParent(parent, false);
            SpaceCanvasPresentationStyle.Dock(panel, new Vector3(0, 360, -160), new Vector2(760, 224), 0, 3050);
            panel.GetComponent<Canvas>().worldCamera = camera;
            panel.GetComponent<Image>().raycastTarget = false;
            progress = SpaceCanvasPresentationStyle.Label(panel.transform, "Progress", "", new Vector2(-30, 83), new Vector2(648, 26), 15, true);
            heading = SpaceCanvasPresentationStyle.Label(panel.transform, "Title", "", new Vector2(0, 44), new Vector2(708, 42), 27);
            heading.fontStyle = FontStyles.Bold;
            description = SpaceCanvasPresentationStyle.Label(panel.transform, "Description", "", new Vector2(0, -9), new Vector2(708, 64), 20);
            SpaceCanvasUnityCompatibility.SetTextWrapping(description, true); description.overflowMode = TextOverflowModes.Truncate;
            primary = SpaceCanvasPresentationStyle.Button(panel.transform, "教学继续", "开始体验", new Vector2(264, -76), new Vector2(180, 48), ContinueOrSkip);
            SpaceCanvasPresentationStyle.Tone(primary, SpaceCanvasPresentationStyle.Accent, true);
            dismiss = SpaceCanvasPresentationStyle.Button(panel.transform, "结束教学", "暂时跳过", new Vector2(76, -76), new Vector2(172, 48), Dismiss);
            segments = new Image[4];
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = new GameObject("Step " + (i + 1), typeof(RectTransform), typeof(Image));
                segment.transform.SetParent(panel.transform, false);
                SpaceCanvasPresentationStyle.Place((RectTransform)segment.transform, new Vector2(-321 + i * 64, -76), new Vector2(52, 4));
                segments[i] = segment.GetComponent<Image>(); segments[i].raycastTarget = false;
            }
            panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (workspace != null) { workspace.AssetAdded -= OnAdded; workspace.ObjectEdited -= OnEdited; workspace.SetTutorialVisibility(false); }
            if (workspace != null && workspace.placement != null) workspace.placement.ViewChanged -= OnPlacementChanged;
            if (projectUI != null) { projectUI.ProjectSaved -= OnSaved; projectUI.OfficialExamplesViewed -= OnExamplesViewed; projectUI.InterfaceChanged -= Render; }
            if (editSession != null) editSession.EditCommitted -= OnEdited;
        }
    }
}
