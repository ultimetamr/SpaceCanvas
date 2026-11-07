using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Text = TMPro.TextMeshProUGUI;
using InputField = TMPro.TMP_InputField;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// Runtime UI bridge for saving/importing .spacecanvas projects.
    /// Disable createRuntimeToolbar when the host project already has a bottom toolbar and bind
    /// SaveProject, ImportProjectFile and ShowAboutOpenSource from the existing buttons.
    /// </summary>
    public sealed partial class SpaceCanvasProjectIOUI : MonoBehaviour
    {
        public bool createRuntimeToolbar = true;
        public bool drawFallbackToolbar = false;
        public bool spatialMode;
        public bool fullSpaceLayout;
        public Camera viewCamera;
        [Tooltip("仅创建 Unity 内容槽位；不创建 Swan 原生多窗口。")]
        public bool createSwanWindowHost = false;
        public AudioClip successClip;
        public AudioClip failureClip;

        private AudioSource audioSource;
        private SpaceCanvasSwanWindowHost swanWindowHost;
        [SerializeField] private Canvas canvas;
        public Transform ViewRoot => canvas == null ? null : canvas.transform;
        public GameObject importPanel;
        public GameObject aboutPanel;
        public GameObject confirmPanel;
        public GameObject layoutPanel;
        public GameObject recoveryPanel;
        public Text toastText;
        public InputField projectNameInput;
        public Transform localListRoot;
        public Transform officialListRoot;
        public Text confirmationText;
        private Text aboutCapabilityText;
        private string pendingImportPath = string.Empty;
        private string pendingOfficialName = string.Empty;
        private bool pendingOfficial;
        public event System.Action InterfaceChanged;
        public event System.Action<string> ProjectSaved;
        public event System.Action OfficialExamplesViewed;
        public bool ModalOpen => (importPanel != null && importPanel.activeSelf)
            || (aboutPanel != null && aboutPanel.activeSelf) || (confirmPanel != null && confirmPanel.activeSelf)
            || (layoutPanel != null && layoutPanel.activeSelf) || (recoveryPanel != null && recoveryPanel.activeSelf);

        private static readonly Color PanelColor = SpaceCanvasPresentationStyle.Surface;
        private static readonly Color ButtonColor = SpaceCanvasPresentationStyle.Card;

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = spatialMode ? 1f : 0f;
            audioSource.minDistance = .8f; audioSource.maxDistance = 5f; audioSource.volume = .22f;
            if (successClip == null) successClip = Resources.Load<AudioClip>("SpaceCanvasAudio/Success");
            if (failureClip == null) failureClip = Resources.Load<AudioClip>("SpaceCanvasAudio/Failure");
            if (createSwanWindowHost)
            {
                swanWindowHost = GetComponent<SpaceCanvasSwanWindowHost>();
                if (swanWindowHost == null) swanWindowHost = gameObject.AddComponent<SpaceCanvasSwanWindowHost>();
            }
            // Serialized MR canvases are part of the native exported scene. Keep them alive;
            // runtime-only delegates must be rebound because Unity does not serialize AddListener.
            if (canvas != null) BindExistingToolbar();
            else if (createRuntimeToolbar) BuildRuntimeToolbar();
            else { RefreshImportLists(); SetImportTab(true); }
        }

        private void BindExistingToolbar()
        {
            EnsureEventSystem();
            foreach (var button in canvas.GetComponentsInChildren<Button>(true))
            {
                UnityEngine.Events.UnityAction action = null;
                switch (button.name)
                {
                    case "保存工程": action = SaveProject; break;
                    case "导入工程": action = OpenImportPanel; break;
                    case "关于开源": action = ShowAboutOpenSource; break;
                    case "撤销": action = UndoLastImport; break;
                    case "新手教学": action = ReplayTutorial; break;
                    case "本地工程": action = ShowLocalProjects; break;
                    case "官方示例": action = ShowOfficialExamples; break;
                    case "确认导入": action = ConfirmImport; break;
                    case "保存并导入": action = SaveAndImport; break;
                    case "恢复作品": action = RestoreRecovery; break;
                    case "稍后恢复": action = CloseRecovery; break;
                    case "丢弃副本": action = DiscardRecovery; break;
                    case "取消": action = CancelImport; break;
                    case "关闭":
                        action = aboutPanel != null && button.transform.IsChildOf(aboutPanel.transform)
                            ? (UnityEngine.Events.UnityAction)CloseAboutPanel : CloseImportPanel;
                        break;
                }
                if (action == null) continue;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(action);
            }
            RefreshImportLists();
            SetImportTab(true);
            AttachSystemKeyboard(projectNameInput);
            BindProjectName();
            EnsureAboutCapabilityText();
            EnsureVersionConfirmationLayout();
        }

        public void OpenImportPanel() { ToggleImportPanel(true); }
        public void CloseImportPanel() { ToggleImportPanel(false); }
        public void CloseAboutPanel() { if (aboutPanel != null) aboutPanel.SetActive(false); NotifyInterfaceChanged(); }

        private void NotifyInterfaceChanged()
        {
            if(fullSpaceLayout && canvas!=null)
            {
                var toolbar=canvas.transform.Find("SpaceCanvasToolbar");
                if(toolbar!=null)
                {
                    var group=toolbar.GetComponent<CanvasGroup>();
                    if(group==null) group=toolbar.gameObject.AddComponent<CanvasGroup>();
                    group.alpha=ModalOpen ? .18f : 1;
                    group.interactable=!ModalOpen; group.blocksRaycasts=!ModalOpen;
                }
            }
            InterfaceChanged?.Invoke();
        }
        public void ShowLocalProjects() { SetImportTab(true); }
        public void ShowOfficialExamples()
        {
            SetImportTab(false);
            if (importPanel != null && importPanel.activeSelf) OfficialExamplesViewed?.Invoke();
        }
        public void ReplayTutorial()
        {
            var tutorial = GetComponent<SpaceCanvasOnboarding>();
            if (tutorial != null) tutorial.Restart();
        }

        public void SaveProject()
        {
            SaveProject(projectNameInput == null ? "UntitledProject" : projectNameInput.text);
        }

        public void SaveProject(string saveName)
        { TrySaveProject(saveName); }

        private bool TrySaveProject(string saveName)
        {
            FinishGesture();
            saveName = string.IsNullOrWhiteSpace(saveName) ? "UntitledProject" : saveName.Trim();
            try
            {
                if (SceneExporter.ExportCurrentScene(saveName, out var path, out var error))
                {
                    ShowMessage("工程已保存：" + Path.GetFileName(path), true);
                    ProjectSaved?.Invoke(path);
                    return true;
                }
                else
                    ShowMessage(string.IsNullOrWhiteSpace(error) ? "保存工程失败。" : error, false);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                ShowMessage("保存工程失败：" + ex.Message, false);
            }
            return false;
        }

        public void ImportProjectFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                ShowMessage("未选择工程文件。", false);
                return;
            }
            if (!SpaceCanvasProjectCatalog.IsInsideLocalDirectory(filePath))
            { ShowMessage("请选择应用 Projects 目录中的工程。", false); return; }
            var entry = SpaceCanvasProjectEntry.Read(filePath);
            if (!entry.IsValid) { ShowMessage(entry.Error, false); return; }
            SelectLocalVersion(entry);
        }

        private void SelectLocalVersion(SpaceCanvasProjectEntry entry)
        {
            pendingImportPath = entry.FilePath;
            pendingProjectVersion = entry;
            pendingOfficial = false;
            pendingOfficialName = string.Empty;
            ShowConfirm("将替换当前布局，导入后可撤销。\n" + entry.TimeLabel + "  ·  " + entry.Project.objects.Count + " 个物体");
            ShowVersionConfirmation(entry.FileName, "版本 " + entry.VersionId, GetVersionSprite(entry));
        }

        public void ShowAboutOpenSource()
        {
            if (importPanel != null) importPanel.SetActive(false);
            if (confirmPanel != null) confirmPanel.SetActive(false);
            if (aboutPanel != null) aboutPanel.SetActive(true);
            RefreshCapabilityText();
            NotifyInterfaceChanged();
        }

        public void UndoLastImport()
        {
            FinishGesture();
            if (SpaceCanvasUndoService.UndoLast(out var error)) { RefreshProjectName(); ShowMessage("已撤销最近一次操作。", true); }
            else ShowMessage(string.IsNullOrWhiteSpace(error) ? "没有可撤销的工程操作。" : error, false);
        }

        public void BuildRuntimeToolbar()
        {
            if (canvas != null)
            {
                canvas.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(canvas.gameObject); else DestroyImmediate(canvas.gameObject);
            }
            EnsureEventSystem();
            canvas = CreateCanvas();
            if (spatialMode)
            {
                SpaceCanvasWorldCanvas.Configure(canvas, viewCamera);
                canvas.transform.position += Vector3.back * .006f;
            }
            var root = canvas.transform;
            var toolbar = CreatePanel(root, "SpaceCanvasToolbar", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 72f), Vector2.zero, PanelColor);
            var layout = toolbar.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 12, 12);
            layout.spacing = 12f;
            layout.childControlWidth = false;
            layout.childControlHeight = true;

            projectNameInput = CreateInput(toolbar.transform, "工程名称", SpaceCanvasRuntime.CurrentProjectRoot == null ? "我的空间" : SpaceCanvasRuntime.CurrentProjectRoot.projectName, 270f);
            AttachSystemKeyboard(projectNameInput);
            BindProjectName();
            CreateButton(toolbar.transform, "保存工程", SaveProject, 150f);
            CreateButton(toolbar.transform, "导入工程", () => ToggleImportPanel(true), 150f);
            CreateButton(toolbar.transform, "关于开源", ShowAboutOpenSource, 150f);
            CreateButton(toolbar.transform, "撤销", UndoLastImport, 110f);
            var help = CreateButton(toolbar.transform, "新手教学", ReplayTutorial, 48f);
            help.GetComponentInChildren<Text>().text = "?";
            var hint = SpaceCanvasPresentationStyle.Label(help.transform, "Tooltip", "重看新手教学", new Vector2(-60, 48), new Vector2(170, 34), 19);
            help.gameObject.AddComponent<SpaceCanvasHelpTooltip>().label = hint.gameObject;
            hint.gameObject.SetActive(false);

            importPanel = CreatePanel(root, "SpaceCanvasImportPanel", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.55f));
            BuildImportPanel(importPanel.transform);
            importPanel.SetActive(false);

            aboutPanel = CreatePanel(root, "SpaceCanvasAboutPanel", new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.88f), Vector2.zero, Vector2.zero, PanelColor);
            BuildAboutPanel(aboutPanel.transform);
            aboutPanel.SetActive(false);

            confirmPanel = CreatePanel(root, "SpaceCanvasConfirmPanel", new Vector2(0.22f, 0.36f), new Vector2(0.78f, 0.64f), Vector2.zero, Vector2.zero, PanelColor);
            BuildConfirmPanel(confirmPanel.transform);
            confirmPanel.SetActive(false);

            var toast = CreatePanel(root, "SpaceCanvasToast", new Vector2(0.18f, 0.78f), new Vector2(0.82f, 0.92f), Vector2.zero, Vector2.zero, new Color(0.02f, 0.03f, 0.05f, 0.9f));
            toastText = CreateText(toast.transform, string.Empty, 24, TextAnchor.MiddleCenter);
            SetStretch(toastText.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-12f, 0f));
            toast.SetActive(false);
        }

        private void BuildImportPanel(Transform parent)
        {
            if(fullSpaceLayout) { BuildRefinedImportPanel(parent); return; }
            var frame = CreatePanel(parent, "Frame", new Vector2(0.14f, 0.10f), new Vector2(0.86f, 0.90f), Vector2.zero, Vector2.zero, PanelColor);
            var title = CreateText(frame.transform, "导入工程", 30, TextAnchor.MiddleLeft);
            SetStretch(title.rectTransform, new Vector2(0f, 0.88f), Vector2.one, new Vector2(24f, 0f), new Vector2(-24f, -4f));

            var tabs = new GameObject("Tabs", typeof(RectTransform));
            tabs.transform.SetParent(frame.transform, false);
            SetStretch(tabs.GetComponent<RectTransform>(), new Vector2(0f, 0.76f), new Vector2(1f, 0.88f), new Vector2(20f, 0f), Vector2.zero);
            var tabLayout = tabs.AddComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = 8f;
            tabLayout.childControlWidth = false;
            CreateButton(tabs.transform, "本地工程", () => SetImportTab(true), 160f);
            CreateButton(tabs.transform, "官方示例", () => SetImportTab(false), 160f);

            localListRoot = CreateScrollList(frame.transform, "LocalList", new Vector2(0f, 0.14f), new Vector2(1f, 0.74f));
            officialListRoot = CreateScrollList(frame.transform, "OfficialList", new Vector2(0f, 0.14f), new Vector2(1f, 0.74f));
            var footer = CreateButtonRow(frame.transform, new Vector2(0f, 0f), new Vector2(1f, 0.12f));
            CreateButton(footer, "关闭", () => ToggleImportPanel(false), 140f);
            RefreshImportLists();
        }

        private void BuildAboutPanel(Transform parent)
        {
            if(fullSpaceLayout) { BuildRefinedAboutPanel(parent); return; }
            var text = CreateText(parent, "SpaceCanvas 开源工程格式\n\n.spacecanvas 是 UTF-8 JSON 纯文本，可直接编辑。\n\nUnity 接入：使用 导入PICO工程 > 导入工程文件。\n替换素材：按 assetId 放入 Resources/SpaceCanvasAssets。\n扩展字段：新增字段保持默认值和向后兼容。\n\n开源协议：MIT License，允许自由二次开发、修改和集成。", 24, TextAnchor.UpperLeft);
            SetStretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(28f, 76f), new Vector2(-28f, -28f));
            aboutCapabilityText = CreateText(parent, string.Empty, 16, TextAnchor.LowerLeft);
            SetStretch(aboutCapabilityText.rectTransform, new Vector2(0f, 0.12f), new Vector2(1f, 0.38f), new Vector2(28f, 0f), new Vector2(-28f, 0f));
            aboutCapabilityText.color = new Color(.64f, .86f, .92f, 1f);
            RefreshCapabilityText();
            var footer = CreateButtonRow(parent, new Vector2(0f, 0f), new Vector2(1f, 0.12f));
            CreateButton(footer, "关闭", CloseAboutPanel, 140f);
        }

        private void BuildRefinedImportPanel(Transform parent)
        {
            var frame=CreatePanel(parent,"Frame",Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero,PanelColor);
            SpaceCanvasPresentationStyle.Label(frame.transform,"Import eyebrow","PROJECTS  /  OPEN A SPACE",new Vector2(0,287),new Vector2(754,24),14,true);
            var title=SpaceCanvasPresentationStyle.Label(frame.transform,"Import title","打开你的下一个灵感",new Vector2(0,243),new Vector2(754,50),32);
            title.fontStyle=FontStyles.Bold;
            SpaceCanvasPresentationStyle.Label(frame.transform,"Import hint","继续本地工程，或从官方空间模板开始。",new Vector2(0,201),new Vector2(754,28),18,true);
            SpaceCanvasPresentationStyle.Button(frame.transform,"本地工程","本地工程",new Vector2(-284,143),new Vector2(186,44),ShowLocalProjects);
            SpaceCanvasPresentationStyle.Button(frame.transform,"官方示例","官方示例",new Vector2(-86,143),new Vector2(186,44),ShowOfficialExamples);
            SpaceCanvasPresentationStyle.Rule(frame.transform,new Vector2(0,109),754);
            localListRoot=CreateScrollList(frame.transform,"LocalList",new Vector2(.04f,.15f),new Vector2(.96f,.64f));
            officialListRoot=CreateScrollList(frame.transform,"OfficialList",new Vector2(.04f,.15f),new Vector2(.96f,.64f));
            localListRoot.parent.GetComponent<Image>().color=Color.clear;
            officialListRoot.parent.GetComponent<Image>().color=Color.clear;
            SpaceCanvasPresentationStyle.Label(frame.transform,"Import footer","导入后可撤销，找回刚才的布局。",new Vector2(-92,-289),new Vector2(570,32),16,true);
            SpaceCanvasPresentationStyle.Button(frame.transform,"关闭","返回工作台",new Vector2(277,-282),new Vector2(200,48),CloseImportPanel);
            RefreshImportLists(); SetImportTab(true);
        }

        private void BuildRefinedAboutPanel(Transform parent)
        {
            SpaceCanvasPresentationStyle.Label(parent,"Open eyebrow","BUILT TO BE OPEN",new Vector2(0,277),new Vector2(704,24),14,true);
            var title=SpaceCanvasPresentationStyle.Label(parent,"Open title","灵感，自由流动。",new Vector2(0,225),new Vector2(704,62),38);
            title.fontStyle=FontStyles.Bold;
            SpaceCanvasPresentationStyle.Label(parent,"Open introduction","你的空间可以分享，也可以被继续创造。",new Vector2(0,171),new Vector2(704,32),20,true);
            var badges=new[]{"JSON 1.0","MIT LICENSE","CC0 ASSETS"};
            for(var i=0;i<badges.Length;i++)
            {
                var card=SpaceCanvasPresentationStyle.SurfacePanel(parent,"Open badge",new Vector2(-236+i*236,114),new Vector2(224,44),SpaceCanvasPresentationStyle.Selected,12);
                var text=SpaceCanvasPresentationStyle.Label(card.transform,"Badge",badges[i],Vector2.zero,new Vector2(210,32),15);
                text.color=SpaceCanvasPresentationStyle.Accent; text.alignment=TextAlignmentOptions.Center;
            }
            SpaceCanvasPresentationStyle.Rule(parent,new Vector2(0,70),704);
            var headings=new[]{"01   开放格式","02   开发者接入","03   自由二次创作"};
            var details=new[]{".spacecanvas 是可编辑的 UTF-8 JSON 纯文本。","Unity 菜单：导入PICO工程 > 导入工程文件。","代码按 MIT 开源，已接入模型按 CC0 授权；可修改与分发。"};
            for(var i=0;i<3;i++)
            {
                var y=35-i*82;
                SpaceCanvasPresentationStyle.Label(parent,"Open section",headings[i],new Vector2(0,y),new Vector2(704,30),21);
                var detail=SpaceCanvasPresentationStyle.Label(parent,"Open detail",details[i],new Vector2(0,y-33),new Vector2(704,38),17,true);
                SpaceCanvasUnityCompatibility.SetTextWrapping(detail, true);
            }
            aboutCapabilityText=SpaceCanvasPresentationStyle.Label(parent,"CapabilityMatrix","Full Space MR · 工程可导出",new Vector2(-70,-233),new Vector2(564,36),14,true);
            SpaceCanvasPresentationStyle.Button(parent,"关闭","返回工作台",new Vector2(244,-273),new Vector2(216,48),CloseAboutPanel);
            RefreshCapabilityText();
        }

        private void RefreshCapabilityText()
        {
            EnsureAboutCapabilityText();
            if (aboutCapabilityText == null) return;
            try { aboutCapabilityText.text = fullSpaceLayout ? "FULL SPACE MR   /   格式说明与素材来源见随附文档" : PicoSpatialCapabilities.Current.ToHumanText(); }
            catch (System.Exception ex) { aboutCapabilityText.text = "能力检测失败：" + ex.Message; }
        }

        private void EnsureAboutCapabilityText()
        {
            if (aboutCapabilityText != null || aboutPanel == null) return;
            aboutCapabilityText = aboutPanel.transform.Find("CapabilityMatrix")?.GetComponent<Text>();
            if(fullSpaceLayout && aboutCapabilityText!=null) return;
            if (aboutCapabilityText == null)
            {
                aboutCapabilityText = CreateText(aboutPanel.transform, string.Empty, 16, TextAnchor.LowerLeft);
                aboutCapabilityText.gameObject.name = "CapabilityMatrix";
                SetStretch(aboutCapabilityText.rectTransform, new Vector2(0f, 0.12f), new Vector2(1f, 0.38f), new Vector2(28f, 0f), new Vector2(-28f, 0f));
            }
            aboutCapabilityText.color = new Color(.64f, .86f, .92f, 1f);
            foreach (var label in aboutPanel.GetComponentsInChildren<Text>(true))
            {
                if (label == aboutCapabilityText) continue;
                var rect = label.rectTransform;
                if (rect.anchorMin.y < .05f && rect.anchorMax.y > .9f)
                    rect.anchorMax = new Vector2(rect.anchorMax.x, .60f);
            }
        }

        private static void AttachSystemKeyboard(InputField input)
        {
            if (input == null) return;
            var bridge = input.GetComponent<SpaceCanvasSystemKeyboardBridge>();
            if (bridge == null) bridge = input.gameObject.AddComponent<SpaceCanvasSystemKeyboardBridge>();
            bridge.inputField = input;
        }

        private void BuildConfirmPanel(Transform parent)
        {
            if(fullSpaceLayout)
            {
                SpaceCanvasPresentationStyle.Label(parent,"Confirm eyebrow","REPLACE WORKSPACE",new Vector2(0,113),new Vector2(584,24),13,true);
                SpaceCanvasPresentationStyle.Label(parent,"Confirm heading","打开新的空间",new Vector2(0,74),new Vector2(584,44),29);
                confirmationText=SpaceCanvasPresentationStyle.Label(parent,"Confirmation","当前布局将被替换，导入后可通过撤销恢复。",new Vector2(0,18),new Vector2(584,66),21,true);
                SpaceCanvasUnityCompatibility.SetTextWrapping(confirmationText, true);
                var accept=SpaceCanvasPresentationStyle.Button(parent,"确认导入","备份并导入",new Vector2(198,-89),new Vector2(188,52),ConfirmImport);
                SpaceCanvasPresentationStyle.Tone(accept,SpaceCanvasPresentationStyle.Accent,true);
                SpaceCanvasPresentationStyle.Button(parent,"取消","取消",new Vector2(-198,-89),new Vector2(188,52),CancelImport);
                SpaceCanvasPresentationStyle.Button(parent,"保存并导入","保存并导入",new Vector2(0,-89),new Vector2(188,52),SaveAndImport);
                return;
            }
            var text = CreateText(parent, "导入工程将清空当前可编辑物体，是否继续？", 26, TextAnchor.MiddleCenter);
            confirmationText = text;
            SetStretch(text.rectTransform, new Vector2(0f, 0.32f), Vector2.one, new Vector2(20f, 0f), new Vector2(-20f, -20f));
            var footer = CreateButtonRow(parent, new Vector2(0f, 0f), new Vector2(1f, 0.28f));
            CreateButton(footer, "确认导入", ConfirmImport, 160f);
            CreateButton(footer, "取消", CancelImport, 160f);
        }

        public void ConfirmImport()
        {
            if (!pendingOfficial && (pendingProjectVersion == null || !pendingProjectVersion.IsValid))
            { ShowMessage("请先选择并确认一个有效的工程版本。", false); return; }
            FinishGesture();
            var projectSession = GetComponent<SpaceCanvasProjectSession>();
            if (projectSession != null && !projectSession.ProtectBeforeImport(out var backupError))
            { ShowMessage(backupError, false); return; }
            if (confirmPanel != null) confirmPanel.SetActive(false);
            NotifyInterfaceChanged();
            if (pendingOfficial)
            {
                StartCoroutine(LoadOfficialExampleConfirmed(pendingOfficialName));
                return;
            }
            try
            {
                // 使用确认卡展示的同一份快照，文件在此期间变化也不会偷偷换成另一个版本。
                var ok = SceneImporter.ImportProject(pendingProjectVersion.Project, out var error, true);
                if (ok) { pendingProjectVersion = null; RefreshProjectName(); ToggleImportPanel(false); }
                ShowMessage(ok ? (string.IsNullOrEmpty(error) ? "工程导入完成。" : error) : (string.IsNullOrWhiteSpace(error) ? "工程导入失败。" : error), ok);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                ShowMessage("工程导入失败：" + ex.Message, false);
            }
        }

        private IEnumerator LoadOfficialExampleConfirmed(string fileName)
        {
            yield return ExampleProjectLoader.LoadOfficialExample(fileName, (ok, error) =>
            {
                if (ok) { RefreshProjectName(); ToggleImportPanel(false); }
                ShowMessage(ok ? (string.IsNullOrEmpty(error) ? "官方示例导入完成。" : error) : (string.IsNullOrWhiteSpace(error) ? "示例工程导入失败。" : error), ok);
            });
        }

        private void SetImportTab(bool local)
        {
            if (localListRoot != null) localListRoot.parent.gameObject.SetActive(local);
            if (officialListRoot != null) officialListRoot.parent.gameObject.SetActive(!local);
            if(fullSpaceLayout && importPanel!=null)
                foreach(var button in importPanel.GetComponentsInChildren<Button>(true))
                    if(button.name=="本地工程" || button.name=="官方示例")
                        SpaceCanvasPresentationStyle.Tone(button,(button.name=="本地工程")==local ? SpaceCanvasPresentationStyle.Selected : SpaceCanvasPresentationStyle.Card);
        }

        private void ToggleImportPanel(bool visible)
        {
            if (importPanel == null) return;
            if (aboutPanel != null) aboutPanel.SetActive(false);
            if (confirmPanel != null) confirmPanel.SetActive(false);
            importPanel.SetActive(visible);
            if (visible)
            {
                RefreshImportLists();
                SetImportTab(true);
            }
            NotifyInterfaceChanged();
        }

        private void RefreshImportLists()
        {
            SpaceCanvasProjectEntry[] localFiles;
            try { localFiles = SpaceCanvasProjectCatalog.GetLocalProjects(); }
            catch (System.Exception ex) { ShowMessage("本地工程列表读取失败：" + ex.Message, false); localFiles = new SpaceCanvasProjectEntry[0]; }
            BeginListRefresh(localListRoot);
            BeginListRefresh(officialListRoot);
            ReleaseVersionSprites();
            var localIndex = 0;
            var officialIndex = 0;
            if (localFiles.Length == 0) BindListRow(localListRoot, localIndex++, "还没有保存的空间", null, null, "在工具栏为工程命名，然后点击保存工程");
            foreach (var entry in localFiles)
            {
                var version = entry;
                var title = (entry.IsLatest ? "最新  ·  " : "") + entry.FileName;
                BindListRow(localListRoot, localIndex++, title, () =>
                {
                    if (!version.IsValid) { ShowMessage(version.Error, false); return; }
                    SelectLocalVersion(version);
                }, null, entry.IsValid ? entry.Detail : "文件损坏  ·  点击查看原因", GetVersionSprite(entry));
            }
            foreach (var file in ExampleProjectLoader.GetOfficialExampleNames())
            {
                var safeName = file;
                var displayName=file.StartsWith("livingroom") ? "静谧客厅" : file.StartsWith("booth") ? "品牌展位" : "灵感工位";
                var description=file.StartsWith("livingroom") ? "LIVING ROOM  /  9 个物体" : file.StartsWith("booth") ? "EXHIBITION  /  9 个物体" : "WORKSPACE  /  9 个物体";
                BindListRow(officialListRoot, officialIndex++, displayName, () =>
                {
                    pendingOfficial = true;
                    pendingProjectVersion = null;
                    pendingOfficialName = safeName;
                    ShowConfirm("导入官方示例将清空当前可编辑物体，是否继续？");
                    ShowVersionConfirmation(displayName, "官方示例  ·  " + safeName, Resources.Load<Sprite>("SpaceCanvasPreviews/" + Path.GetFileNameWithoutExtension(safeName)));
                },Path.GetFileNameWithoutExtension(file),description);
            }
        }

        // SpatialAdapter also registers children of inactive panels. Unity does not call
        // OnDestroy on components that have never been active, so destroying those rows
        // leaves native tracker entries behind. Reuse rows and replace their delegates.
        // This keeps native entity IDs stable and bounds allocations by the largest list.
        private static void BeginListRefresh(Transform root)
        {
            if (root == null) return;
            foreach (Transform child in root) child.gameObject.SetActive(false);
        }

        private void BindListRow(Transform root, int index, string title, UnityEngine.Events.UnityAction action,string previewId=null,string description=null,Sprite versionSprite=null)
        {
            if (root == null) return;
            Button row = null;
            var buttonIndex = 0;
            foreach (Transform child in root)
            {
                var candidate = child.GetComponent<Button>();
                if (candidate == null) continue;
                if (buttonIndex++ == index) { row = candidate; break; }
            }
            if (row == null) row = CreateButton(root, title, null, 520f);
            row.name = title;
            row.onClick.RemoveAllListeners();
            if (action != null) row.onClick.AddListener(action);
            row.interactable = action != null;
            var label = row.GetComponentInChildren<Text>(true);
            if (label != null) { label.text = title; label.overflowMode = TextOverflowModes.Ellipsis; }
            if(fullSpaceLayout || description != null)
            {
                row.GetComponent<LayoutElement>().preferredHeight=100;
                row.GetComponent<LayoutElement>().minHeight=100;
                SetStretch(label.rectTransform,new Vector2(0,.43f),new Vector2(1,.91f),new Vector2(160,0),new Vector2(-45,0));
                label.alignment=TextAlignmentOptions.MidlineLeft; label.fontSize=23;
                var detail=row.transform.Find("Detail")?.GetComponent<Text>();
                if(detail==null) { detail=CreateText(row.transform,"",15,TextAnchor.MiddleLeft); detail.name="Detail"; }
                detail.text=description; detail.color=SpaceCanvasPresentationStyle.Muted;
                SetStretch(detail.rectTransform,new Vector2(0,.14f),new Vector2(1,.43f),new Vector2(160,0),new Vector2(-20,0));
                var image=row.transform.Find("Preview")?.GetComponent<Image>();
                if(image==null)
                {
                    var go=new GameObject("Preview",typeof(RectTransform),typeof(Image)); go.transform.SetParent(row.transform,false);
                    image=go.GetComponent<Image>(); image.raycastTarget=false; image.preserveAspect=true;
                    SetStretch((RectTransform)go.transform,new Vector2(0,0),new Vector2(0,1),new Vector2(10,8),new Vector2(142,-8));
                }
                var sprite=versionSprite != null ? versionSprite : string.IsNullOrEmpty(previewId) ? null : Resources.Load<Sprite>("SpaceCanvasPreviews/"+previewId);
                image.sprite=sprite; image.color=sprite!=null ? Color.white : new Color(.84f,.72f,.50f,.16f);
                var empty = row.transform.Find("Preview status")?.GetComponent<Text>();
                if (empty == null) { empty = CreateText(row.transform, "暂无预览", 14, TextAnchor.MiddleCenter); empty.name = "Preview status"; SetStretch(empty.rectTransform, new Vector2(0,0), new Vector2(0,1), new Vector2(10,8), new Vector2(142,-8)); }
                empty.gameObject.SetActive(sprite == null); empty.raycastTarget = false;
            }
            row.gameObject.SetActive(true);
        }

        private void ShowConfirm(string text)
        {
            if (confirmPanel == null) return;
            EnsureVersionConfirmationLayout();
            var label = confirmationText == null ? confirmPanel.GetComponentInChildren<Text>() : confirmationText;
            if (label != null) label.text = text + (fullSpaceLayout ? "\n未保存的作品会先生成本地恢复备份。" : "");
            if (fullSpaceLayout && importPanel != null) importPanel.SetActive(false);
            confirmPanel.SetActive(true);
            NotifyInterfaceChanged();
        }

        public void CancelImport()
        {
            if (fullSpaceLayout && importPanel != null) { importPanel.SetActive(true); SetImportTab(!pendingOfficial); }
            pendingImportPath = string.Empty;
            pendingProjectVersion = null;
            pendingOfficial = false;
            pendingOfficialName = string.Empty;
            if (confirmPanel != null) confirmPanel.SetActive(false);
            NotifyInterfaceChanged();
        }

        /// <summary>Separate compact toolbar and modal cards; no full-view black overlay.</summary>
        public void ConfigureFullSpaceLayout(Transform anchor, Camera camera)
        {
            fullSpaceLayout = true;
            canvas.transform.SetParent(anchor,false);
            canvas.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            canvas.transform.localScale = Vector3.one * .001f;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(940,720);
            canvas.worldCamera = camera;
            canvas.GetComponent<GraphicRaycaster>().enabled = false;
            var toolbar = canvas.transform.Find("SpaceCanvasToolbar").gameObject;
            SpaceCanvasPresentationStyle.Dock(toolbar,new Vector3(0,-481,-220),new Vector2(914,82),0,3000);
            var layout = toolbar.GetComponent<HorizontalLayoutGroup>(); layout.childAlignment=TextAnchor.MiddleCenter;
            layout.childControlWidth=true; layout.childForceExpandWidth=false; layout.spacing=10; layout.padding=new RectOffset(18,18,14,14);
            projectNameInput.GetComponent<LayoutElement>().preferredWidth=250;
            foreach(var button in toolbar.GetComponentsInChildren<Button>())
            {
                button.GetComponent<LayoutElement>().preferredWidth=button.name=="新手教学" ? 48 : button.name=="撤销" ? 88 : 138;
                button.GetComponent<LayoutElement>().preferredHeight=48;
                if(button.name=="保存工程") SpaceCanvasPresentationStyle.Tone(button,SpaceCanvasPresentationStyle.Accent,true);
            }
            SpaceCanvasPresentationStyle.Dock(importPanel,new Vector3(0,10,-300),new Vector2(850,670),0,3100);
            importPanel.GetComponent<Image>().color=Color.clear; importPanel.GetComponent<Image>().raycastTarget=false;
            SpaceCanvasPresentationStyle.Dock(aboutPanel,new Vector3(0,10,-300),new Vector2(800,650),0,3100);
            SpaceCanvasPresentationStyle.Dock(confirmPanel,new Vector3(0,10,-330),new Vector2(660,310),0,3200);
            EnsureVersionConfirmationLayout();
            // Opaque modal cards preserve text contrast against bright MR scene content.
            aboutPanel.GetComponent<Image>().color = new Color(PanelColor.r,PanelColor.g,PanelColor.b,1);
            confirmPanel.GetComponent<Image>().color = new Color(PanelColor.r,PanelColor.g,PanelColor.b,1);
            var frame = importPanel.transform.Find("Frame");
            if (frame != null) frame.GetComponent<Image>().color = new Color(PanelColor.r,PanelColor.g,PanelColor.b,1);
            var toast = toastText.transform.parent.gameObject;
            SpaceCanvasPresentationStyle.Dock(toast,new Vector3(0,-282,-310),new Vector2(680,66),0,3300);
            foreach (var child in canvas.GetComponentsInChildren<Canvas>(true)) child.worldCamera=camera;
        }

        public void ShowFeedback(string text, bool success) { ShowMessage(text, success); }

        private void FinishGesture()
        { SpaceCanvasScalarSlider.FinishAll(); GetComponent<SpaceCanvasPlacement>()?.Cancel(); var session = GetComponent<SpaceCanvasEditSession>(); if (session != null) session.EndGesture(false); }

        private void ShowMessage(string text, bool success)
        {
            var message = string.IsNullOrWhiteSpace(text) ? (success ? "操作完成。" : "操作失败。") : text;
            if (toastText != null)
            {
                toastText.text = message;
                toastText.transform.parent.gameObject.SetActive(true);
                CancelInvoke(nameof(HideToast));
                Invoke(nameof(HideToast), 5f);
            }
            if (audioSource != null)
            {
                var clip = success ? successClip : failureClip;
                if (clip != null) audioSource.PlayOneShot(clip);
            }
        }

        private void HideToast()
        {
            if (toastText != null) toastText.transform.parent.gameObject.SetActive(false);
        }

        private void OnGUI()
        {
            if (!drawFallbackToolbar) return;
            GUI.Box(new Rect(16f, Screen.height - 64f, Screen.width - 32f, 48f), "SpaceCanvas 工程工具栏");
        }

        private static Canvas CreateCanvas()
        {
            var go = new GameObject("SpaceCanvasProjectIOCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3000;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            // PICO/XR projects normally provide the new Input System. Use it when
            // available, while retaining the legacy module for a blank Unity project.
            var inputSystemModule = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemModule != null) go.AddComponent(inputSystemModule);
            else go.AddComponent<StandaloneInputModule>();
            if (Application.isPlaying) DontDestroyOnLoad(go);
        }

        private GameObject CreatePanel(Transform parent, string name, Vector2 min, Vector2 max, Vector2 size, Vector2 position, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(SpaceCanvasRoundedPanel));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = position;
            rect.offsetMax = size;
            go.GetComponent<Image>().color = color;
            return go;
        }

        private InputField CreateInput(Transform parent, string placeholder, string value, float width)
        {
            var go = new GameObject("ProjectNameInput", typeof(RectTransform), typeof(SpaceCanvasRoundedPanel), typeof(InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, 44f);
            var size = go.AddComponent<LayoutElement>();
            size.preferredWidth = width;
            size.preferredHeight = 48f;
            size.minHeight = 48f;
            go.GetComponent<Image>().color = SpaceCanvasPresentationStyle.Card;
            var text = CreateText(go.transform, value, 19, TextAnchor.MiddleLeft);
            text.color = SpaceCanvasPresentationStyle.Ink;
            SetStretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-12f, 0f));
            var input = go.GetComponent<InputField>();
            input.textViewport = go.GetComponent<RectTransform>();
            input.textComponent = text;
            input.characterLimit = 80;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.text = value;
            input.placeholder = CreateText(go.transform, placeholder, 19, TextAnchor.MiddleLeft);
            SetStretch(input.placeholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-12f, 0f));
            input.placeholder.color = SpaceCanvasPresentationStyle.Muted;
            return input;
        }

        private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action, float width)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(SpaceCanvasRoundedPanel), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, 44f);
            var size = go.AddComponent<LayoutElement>();
            size.preferredWidth = width;
            size.preferredHeight = 48f;
            size.minHeight = 48f;
            var image = go.GetComponent<Image>();
            image.color = ButtonColor;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            SpaceCanvasPresentationStyle.ButtonStyle(button);
            if (action != null) button.onClick.AddListener(action);
            var text = CreateText(go.transform, label, 19, TextAnchor.MiddleCenter);
            SetStretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return button;
        }

        private static Transform CreateButtonRow(Transform parent, Vector2 min, Vector2 max)
        {
            var row = new GameObject("ButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            var rect = row.GetComponent<RectTransform>();
            SetStretch(rect, min, max, new Vector2(20f, 8f), new Vector2(-20f, -8f));
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            return row.transform;
        }

        private Transform CreateScrollList(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var scroll = CreatePanel(parent, name, min, max, new Vector2(-20f, 0f), new Vector2(20f, 0f), new Color(0f, 0f, 0f, 0.18f));
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(scroll.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(.5f, 1);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scrollRect = scroll.AddComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = scroll.GetComponent<RectTransform>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scroll.AddComponent<RectMask2D>();
            return content.transform;
        }

        private static Text CreateText(Transform parent, string value, int size, TextAnchor alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = SpaceCanvasTypography.Font;
            text.fontSize = size;
            text.alignment = SpaceCanvasTypography.Alignment(alignment);
            text.text = value;
            text.color = SpaceCanvasPresentationStyle.Ink;
            text.raycastTarget = false;
            return text;
        }

        private static void SetStretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

    }

    public static class SpaceCanvasProjectIOBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateFallbackToolbar()
        {
            if (Object.FindObjectOfType<SpaceCanvasProjectIOUI>() != null) return;
            var host = new GameObject("SpaceCanvasProjectIOUI");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<SpaceCanvasProjectIOUI>();
        }
    }
}
