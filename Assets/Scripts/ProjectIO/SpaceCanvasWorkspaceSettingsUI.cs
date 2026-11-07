using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>Comfort preferences affect the workbench anchor only, never project-local object coordinates.</summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasWorkspaceSettingsUI : MonoBehaviour
    {
        public const string Prefix = "SpaceCanvas.Comfort.v1.";
        public SpaceCanvasProjectIOUI projectUI;
        public SpaceCanvasWorkspaceUI workspace;
        [SerializeField] private GameObject panel;
        [SerializeField] private Slider distanceSlider, heightSlider, scaleSlider;
        [SerializeField] private TextMeshProUGUI distanceText, heightText, scaleText;
        public float Distance { get; private set; } = 1.5f;
        public float Height { get; private set; } = -.2f;
        public float Scale { get; private set; } = 1f;
        public event Action Applied;

        private void Awake() { Load(); }
        public static float Sanitize(float value, float fallback, float min, float max)
        { return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max); }
        public void Load()
        {
            Distance = Sanitize(PlayerPrefs.GetFloat(Prefix + "Distance", 1.5f), 1.5f, 1.2f, 2.2f);
            Height = Sanitize(PlayerPrefs.GetFloat(Prefix + "Height", -.2f), -.2f, -.5f, .3f);
            Scale = Sanitize(PlayerPrefs.GetFloat(Prefix + "Scale", 1f), 1f, .65f, 1.35f);
        }
        private void Start()
        {
            if (panel == null) return;
            foreach (var button in panel.GetComponentsInChildren<Button>(true))
            {
                button.onClick.RemoveAllListeners();
                switch (button.name)
                {
                    case "应用布局": button.onClick.AddListener(Apply); break;
                    case "默认布局": button.onClick.AddListener(ResetValues); break;
                    case "关闭布局": button.onClick.AddListener(Close); break;
                    case "查看恢复": button.onClick.AddListener(projectUI.ShowRecoveryPrompt); break;
                    case "导出诊断": button.onClick.AddListener(ExportDiagnostics); break;
                }
            }
            foreach (var slider in new[] { distanceSlider, heightSlider, scaleSlider })
            { slider.onValueChanged.RemoveAllListeners(); slider.onValueChanged.AddListener(_ => RefreshLabels()); }
            panel.SetActive(false);
        }
        public void Open()
        {
            if (panel == null) return;
            distanceSlider.SetValueWithoutNotify(Distance); heightSlider.SetValueWithoutNotify(Height); scaleSlider.SetValueWithoutNotify(Scale);
            RefreshLabels(); projectUI.OpenExternalPanel(panel);
        }
        private void Close() { projectUI.CloseExternalPanel(panel); }
        private void ResetValues()
        { distanceSlider.value = 1.5f; heightSlider.value = -.2f; scaleSlider.value = 1f; }
        private void Apply()
        {
            workspace.FinishPropertyEdit(); workspace.editSession?.EndGesture(false);
            Distance = Sanitize(distanceSlider.value, 1.5f, 1.2f, 2.2f);
            Height = Sanitize(heightSlider.value, -.2f, -.5f, .3f);
            Scale = Sanitize(scaleSlider.value, 1f, .65f, 1.35f);
            PlayerPrefs.SetFloat(Prefix + "Distance", Distance); PlayerPrefs.SetFloat(Prefix + "Height", Height); PlayerPrefs.SetFloat(Prefix + "Scale", Scale); PlayerPrefs.Save();
            Close(); Applied?.Invoke();
        }
        private void RefreshLabels()
        {
            distanceText.text = "距离  " + distanceSlider.value.ToString("0.00") + " m";
            heightText.text = "高度偏移  " + heightSlider.value.ToString("+0.00;-0.00;0.00") + " m";
            scaleText.text = "工作台大小  " + (scaleSlider.value * 100).ToString("0") + "%";
        }
        private void ExportDiagnostics()
        {
            var monitor = GetComponent<SpaceCanvasPerformanceMonitor>();
            if (monitor != null && monitor.Export(out var path, out var error)) projectUI.ShowFeedback("诊断已保存：" + System.IO.Path.GetFileName(path), true);
            else projectUI.ShowFeedback("诊断导出失败，请检查存储空间。", false);
        }
        private Slider BuildSlider(string name, float y, float min, float max, float value, out TextMeshProUGUI title)
        {
            title = SpaceCanvasPresentationStyle.Label(panel.transform, name + " title", name, new Vector2(-210, y), new Vector2(230, 44), 21);
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Slider)); go.transform.SetParent(panel.transform, false);
            SpaceCanvasPresentationStyle.Place((RectTransform)go.transform, new Vector2(130, y), new Vector2(370, 48)); go.GetComponent<Image>().color = Color.clear;
            SpaceCanvasPresentationStyle.SurfacePanel(go.transform, "Track", Vector2.zero, new Vector2(342, 6), SpaceCanvasPresentationStyle.Card, 3);
            var area = new GameObject("Area", typeof(RectTransform)); area.transform.SetParent(go.transform, false);
            var rect = (RectTransform)area.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(18, 0); rect.offsetMax = new Vector2(-18, 0);
            var handle = SpaceCanvasPresentationStyle.SurfacePanel(rect, "Handle", Vector2.zero, new Vector2(36, 40), SpaceCanvasPresentationStyle.Accent, 8); handle.raycastTarget = true;
            var slider = go.GetComponent<Slider>(); slider.minValue = min; slider.maxValue = max; slider.handleRect = (RectTransform)handle.transform; slider.targetGraphic = handle; slider.SetValueWithoutNotify(value);
            slider.handleRect.sizeDelta = new Vector2(28, -18);
            return slider;
        }
        public void BuildView(Transform parent, Camera camera)
        {
            panel = SpaceCanvasPresentationStyle.SurfacePanel(parent, "工作台布局", Vector2.zero, new Vector2(760, 570), SpaceCanvasPresentationStyle.Surface, 20).gameObject;
            projectUI.layoutPanel = panel;
            SpaceCanvasPresentationStyle.Dock(panel, new Vector3(0, 10, -330), new Vector2(760, 570), 0, 3200); panel.GetComponent<Canvas>().worldCamera = camera;
            panel.GetComponent<Image>().color = new Color(SpaceCanvasPresentationStyle.Surface.r, SpaceCanvasPresentationStyle.Surface.g, SpaceCanvasPresentationStyle.Surface.b, 1);
            SpaceCanvasPresentationStyle.Label(panel.transform, "Layout heading", "舒适的创作位置", new Vector2(0, 218), new Vector2(660, 48), 30);
            distanceSlider = BuildSlider("距离", 134, 1.2f, 2.2f, 1.5f, out distanceText);
            heightSlider = BuildSlider("高度", 57, -.5f, .3f, -.2f, out heightText);
            scaleSlider = BuildSlider("大小", -20, .65f, 1.35f, 1f, out scaleText);
            SpaceCanvasPresentationStyle.Rule(panel.transform, new Vector2(0, -77), 660);
            SpaceCanvasPresentationStyle.Button(panel.transform, "查看恢复", "恢复上次作品", new Vector2(-172, -122), new Vector2(316, 48), projectUI.ShowRecoveryPrompt);
            SpaceCanvasPresentationStyle.Button(panel.transform, "导出诊断", "导出运行诊断", new Vector2(172, -122), new Vector2(316, 48), ExportDiagnostics);
            SpaceCanvasPresentationStyle.Button(panel.transform, "关闭布局", "取消", new Vector2(-224, -220), new Vector2(212, 52), Close);
            SpaceCanvasPresentationStyle.Button(panel.transform, "默认布局", "恢复默认", new Vector2(0, -220), new Vector2(212, 52), ResetValues);
            var apply = SpaceCanvasPresentationStyle.Button(panel.transform, "应用布局", "应用并回正", new Vector2(224, -220), new Vector2(212, 52), Apply);
            SpaceCanvasPresentationStyle.Tone(apply, SpaceCanvasPresentationStyle.Accent, true); panel.SetActive(false);
        }
    }
}
