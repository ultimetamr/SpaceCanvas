using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Text = TMPro.TextMeshProUGUI;
using InputField = TMPro.TMP_InputField;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// 可运行的桌面预览界面。所有修改作用于 ContentRoot，导入、保存与撤销使用同一数据模型。
    /// 该 Canvas 是 Unity 界面，不代表多个 Swan 原生窗口；原生窗口需要另行接入 SDK 容器。
    /// 编辑器可调用 BuildInterface 保存可见布局，运行时重建监听，避免临时委托在保存场景后丢失。
    /// </summary>
    public sealed partial class SpaceCanvasWorkspaceUI : MonoBehaviour
    {
        [SerializeField] private Canvas workspaceCanvas;
        public Transform ViewRoot => workspaceCanvas == null ? null : workspaceCanvas.transform;
        public bool spatialMode;
        public bool fullSpaceLayout;
        public Camera viewCamera;
        public SpaceCanvasEditSession editSession;
        public SpaceCanvasPlacement placement;
        public SpaceCanvasEditableObject Selected { get { return selected; } }
        [SerializeField] private Transform objectList;
        [SerializeField] private Transform propertyBody;
        [SerializeField] private Text status;
        private SpaceCanvasEditableObject selected;
        private string objectSignature;
        private float nextRefresh;
        [SerializeField] private SpaceCanvasAssetPalette palette;
        private bool panelsVisible = true;
        public event Action RecenterRequested;
        public event Action<SpaceCanvasEditableObject> AssetAdded;
        public event Action<SpaceCanvasEditableObject> ObjectEdited;
        private bool tutorialVisible;
        private bool modalVisible;
        private readonly List<InputField> transformFields = new List<InputField>();
        private static readonly Color Background = SpaceCanvasPresentationStyle.Surface;
        private static TMP_FontAsset Font { get { return SpaceCanvasTypography.Font; } }

        private void Start()
        {
            if (workspaceCanvas == null || objectList == null || propertyBody == null) BuildInterface();
            else
            {
                // Preserve the exported native hierarchy; rebind only the runtime delegates.
                if (fullSpaceLayout && palette != null) palette.Bind(AddAsset);
                var assetButtons = fullSpaceLayout ? null : workspaceCanvas.transform.Find("素材库");
                if (assetButtons != null)
                    foreach (var button in assetButtons.GetComponentsInChildren<Button>(true))
                        for (var i = 0; i < SpaceCanvasBuiltinAssets.Names.Length; i++)
                        {
                            if (button.name != SpaceCanvasBuiltinAssets.Names[i]) continue;
                            var assetId = SpaceCanvasBuiltinAssets.Ids[i];
                            button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => AddAsset(assetId));
                            break;
                        }
                var tools = workspaceCanvas.transform.Find("空间编辑工具");
                if (tools != null)
                    foreach (var button in tools.GetComponentsInChildren<Button>(true))
                    {
                        var toolName = button.name;
                        button.onClick.RemoveAllListeners();
                        button.onClick.AddListener(() =>
                        {
                            if (toolName == "删除") DeleteSelected();
                            else if (toolName == "专注编辑") TogglePanels();
                            else if (toolName == "回正") RecenterRequested?.Invoke();
                            else if (toolName == "布局") GetComponent<SpaceCanvasWorkspaceSettingsUI>()?.Open();
                            else if (editSession != null) editSession.SetTool(toolName == "移动" ? SpaceCanvasEditTool.Move : toolName == "旋转" ? SpaceCanvasEditTool.Rotate : SpaceCanvasEditTool.Scale);
                        });
                    }
                RefreshObjects();
            }
            Canvas.ForceUpdateCanvases();
            RefreshToolFeedback();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .2f;
            if (fullSpaceLayout) RefreshToolFeedback();
            var signature = string.Join("|", SpaceCanvasRuntime.GetEditableObjects().ConvertAll(x => x == null ? "" : x.ObjectId + ":" + SpaceCanvasUnityCompatibility.ObjectId(x)));
            if (signature != objectSignature) RefreshObjects();
        }

        public void BuildInterface()
        {
            if (workspaceCanvas != null) Dispose(workspaceCanvas.gameObject);
            var canvasGo = new GameObject("SpaceCanvasWorkspaceCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            workspaceCanvas = canvasGo.GetComponent<Canvas>();
            workspaceCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            workspaceCanvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 1000);
            scaler.matchWidthOrHeight = .5f;
            if (spatialMode) SpaceCanvasWorldCanvas.Configure(workspaceCanvas, viewCamera);
            var left = Panel(canvasGo.transform, "素材库", new Vector2(0, .09f), new Vector2(.19f, .93f));
            Label(left, "素材库 · 36 个可替换模型", 21, new Vector2(0, .93f), Vector2.one);
            var assets = Scroll(left, new Vector2(.04f, .03f), new Vector2(.96f, .92f));
            for (var i = 0; i < SpaceCanvasBuiltinAssets.Ids.Length; i++)
            {
                var id = SpaceCanvasBuiltinAssets.Ids[i];
                ListButton(assets, SpaceCanvasBuiltinAssets.Names[i], () => AddAsset(id));
            }
            var right = Panel(canvasGo.transform, "属性面板", new Vector2(.79f, .09f), new Vector2(1, .93f));
            Label(right, "场景物体 / 属性", 21, new Vector2(0, .94f), Vector2.one);
            objectList = Scroll(right, new Vector2(.04f, .60f), new Vector2(.96f, .93f));
            propertyBody = Panel(right, "属性内容", new Vector2(.03f, .02f), new Vector2(.97f, .59f));
            var top = Panel(canvasGo.transform, "标题", new Vector2(0, .94f), Vector2.one);
            Label(top, "SPACE CANVAS   空间原型坊", 25, new Vector2(.02f, 0), new Vector2(.60f, 1));
            status = Label(canvasGo.transform, "", 19, new Vector2(.22f, .85f), new Vector2(.76f, .93f));
            status.alignment = TextAlignmentOptions.Center;
            status.color = new Color(.8f, .94f, 1);
            RefreshObjects();
            ShowProperties();
            if (spatialMode) BuildEditTools(canvasGo.transform);
        }

        public void SelectObject(SpaceCanvasEditableObject item) { FinishPropertyEdit(); selected = item; ShowProperties(); RefreshObjectHighlights(); }
        public void RefreshSelection() { RefreshObjects(); }

        private void BuildEditTools(Transform parent)
        {
            var bar = Panel(parent, "空间编辑工具", new Vector2(.21f, .1f), new Vector2(.77f, .17f));
            var labels = fullSpaceLayout ? new[] { "移动", "旋转", "缩放", "删除", "专注编辑", "布局", "回正" }
                : new[] { "移动", "旋转", "缩放", "删除" };
            for (var i = 0; i < labels.Length; i++)
            {
                var index = i;
                var button = ListButton(bar, labels[i], () =>
                {
                    if (index == 3) DeleteSelected();
                    else if (index == 4) TogglePanels();
                    else if (labels[index] == "布局") GetComponent<SpaceCanvasWorkspaceSettingsUI>()?.Open();
                    else if (labels[index] == "回正") RecenterRequested?.Invoke();
                    else if (editSession != null) editSession.SetTool((SpaceCanvasEditTool)index);
                });
                var step = 1f / labels.Length;
                Place(button.GetComponent<RectTransform>(), new Vector2(i * step + .006f, .08f), new Vector2((i + 1) * step - .006f, .92f));
            }
        }

        private void RefreshToolFeedback()
        {
            if (!fullSpaceLayout || workspaceCanvas == null || editSession == null) return;
            var tools = workspaceCanvas.transform.Find("空间编辑工具");
            if (tools == null) return;
            var selectedTool = editSession.tool == SpaceCanvasEditTool.Move ? "移动" : editSession.tool == SpaceCanvasEditTool.Rotate ? "旋转" : "缩放";
            foreach (var button in tools.GetComponentsInChildren<Button>(true))
            {
                var feedback = button.GetComponent<SpaceCanvasButtonFeedback>();
                if (feedback != null) feedback.SetIdleColor(button.name == selectedTool
                    ? SpaceCanvasPresentationStyle.Selected : SpaceCanvasPresentationStyle.Card);
            }
        }

        public void TogglePanels()
        {
            if (workspaceCanvas == null) return;
            panelsVisible = !panelsVisible;
            foreach (var panelName in new[] {"素材库", "属性面板"})
            {
                var panel = workspaceCanvas.transform.Find(panelName);
                if (panel != null) panel.gameObject.SetActive(panelsVisible);
            }
            var focus = workspaceCanvas.transform.Find("空间编辑工具/专注编辑");
            if (focus != null) focus.GetComponentInChildren<Text>().text = panelsVisible ? "专注编辑" : "展开面板";
        }

        /// <summary>Independent world canvases at the edges; the central editing volume has no UI backdrop.</summary>
        public void ConfigureFullSpaceLayout(Transform anchor, Camera camera)
        {
            fullSpaceLayout = true;
            workspaceCanvas.transform.SetParent(anchor,false);
            workspaceCanvas.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
            workspaceCanvas.transform.localScale = Vector3.one * .001f;
            workspaceCanvas.worldCamera = camera;
            workspaceCanvas.GetComponent<GraphicRaycaster>().enabled = false;
            var left = workspaceCanvas.transform.Find("素材库").gameObject;
            var right = workspaceCanvas.transform.Find("属性面板").gameObject;
            SpaceCanvasPresentationStyle.Dock(left,new Vector3(-700,42,90),new Vector2(420,748),-18);
            SpaceCanvasPresentationStyle.Dock(right,new Vector3(700,42,90),new Vector2(398,748),18);
            palette = left.GetComponent<SpaceCanvasAssetPalette>();
            if (palette == null) palette = left.AddComponent<SpaceCanvasAssetPalette>();
            palette.Bind(AddAsset);
            var title = workspaceCanvas.transform.Find("标题").gameObject;
            SpaceCanvasPresentationStyle.Dock(title,new Vector3(0,365,100),new Vector2(600,74));
            title.GetComponent<Image>().color = SpaceCanvasPresentationStyle.Surface;
            var heading = title.GetComponentInChildren<Text>(); heading.fontSize=25;
            heading.characterSpacing = 5; heading.text = "SPACE CANVAS";
            SpaceCanvasPresentationStyle.Place(heading.rectTransform,new Vector2(0,3),new Vector2(530,48));
            heading.alignment = TextAlignmentOptions.Center;
            SpaceCanvasPresentationStyle.Place(status.rectTransform,new Vector2(0,289),new Vector2(650,58));
            status.fontSize = 18; status.color = SpaceCanvasPresentationStyle.Muted;
            for(var i=right.transform.childCount-1;i>=0;i--)
            {
                var direct = right.transform.GetChild(i).GetComponent<Text>();
                if(direct!=null) Dispose(direct.gameObject);
            }
            SpaceCanvasPresentationStyle.Label(right.transform,"Inspector eyebrow","02  /  INSPECTOR",new Vector2(0,333),new Vector2(342,24),14,true);
            var inspectorTitle=SpaceCanvasPresentationStyle.Label(right.transform,"Inspector title","场景与属性",new Vector2(0,297),new Vector2(342,42),28);
            inspectorTitle.fontStyle=FontStyles.Bold;
            SpaceCanvasPresentationStyle.Rule(right.transform,new Vector2(0,261),342);
            Place((RectTransform)objectList.parent,new Vector2(.07f,.702f),new Vector2(.93f,.831f));
            objectList.parent.GetComponent<Image>().color = Color.clear;
            Place((RectTransform)propertyBody,new Vector2(.07f,.025f),new Vector2(.93f,.69f));
            propertyBody.GetComponent<Image>().color = Color.clear;
            var tools=workspaceCanvas.transform.Find("空间编辑工具").gameObject;
            SpaceCanvasPresentationStyle.Dock(tools,new Vector3(0,-384,-220),new Vector2(760,68),0,200);
            foreach(var button in tools.GetComponentsInChildren<Button>())
            {
                button.GetComponentInChildren<Text>().fontSize=19;
                if(button.name=="删除") button.GetComponentInChildren<Text>().color=new Color(.91f,.57f,.49f);
            }
            ShowProperties();
            RefreshToolFeedback();
            foreach(var canvas in workspaceCanvas.GetComponentsInChildren<Canvas>(true)) canvas.worldCamera=camera;
        }

        public void SetModalVisibility(bool modal)
        {
            modalVisible = modal;
            if (workspaceCanvas == null) return;
            foreach(var name in new[]{"素材库","属性面板","空间编辑工具","标题"})
            {
                var panel = workspaceCanvas.transform.Find(name); if(panel==null) continue;
                var group = panel.GetComponent<CanvasGroup>();
                if (group == null) group = panel.gameObject.AddComponent<CanvasGroup>();
                group.alpha = modal ? .08f : 1f; group.interactable = !modal; group.blocksRaycasts = !modal;
            }
            status.enabled = !modal && !tutorialVisible && (placement == null || !placement.IsPlacing);
        }

        public void SetTutorialVisibility(bool visible)
        {
            tutorialVisible = visible;
            if (workspaceCanvas == null) return;
            var title = workspaceCanvas.transform.Find("标题");
            var placing = placement != null && placement.IsPlacing;
            if (title != null) title.gameObject.SetActive(!visible && !placing);
            if (status != null) status.enabled = !visible && !modalVisible && !placing;
        }
        public void RefreshChrome() { SetTutorialVisibility(tutorialVisible); }

        public void DeleteSelected()
        {
            if (placement != null && placement.IsPlacing) { placement.Cancel(); return; }
            FinishPropertyEdit();
            if (selected == null) return;
            try
            {
                if (editSession != null) editSession.EndGesture(false);
                Snapshot();
                var removedIds = new HashSet<string>();
                foreach (var item in selected.GetComponentsInChildren<SpaceCanvasEditableObject>(true)) removedIds.Add(item.ObjectId);
                foreach (var group in SpaceCanvasRuntime.GetGroups()) group.childIds.RemoveAll(removedIds.Contains);
                Dispose(selected.gameObject); selected = null;
                RefreshObjects();
            }
            catch (Exception ex) { if (status != null) status.text = "删除失败：" + ex.Message; }
        }

        private void RefreshObjects()
        {
            if (objectList == null) return;
            Clear(objectList);
            var items = SpaceCanvasRuntime.GetEditableObjects();
            objectSignature = string.Join("|", items.ConvertAll(x => x == null ? "" : x.ObjectId + ":" + SpaceCanvasUnityCompatibility.ObjectId(x)));
            foreach (var item in items)
            {
                if (item == null) continue;
                var captured = item;
                ListButton(objectList, SpaceCanvasBuiltinAssets.DisplayName(item.AssetId), () => { if (editSession != null) editSession.Select(captured); else SelectObject(captured); });
            }
            // Import/undo detaches old objects immediately, but Unity destroys them at frame end.
            // A detached selection can still compare non-null during this refresh.
            if (selected == null || !items.Contains(selected)) selected = null;
            if (editSession != null && !editSession.IsManipulating && editSession.Selected != selected) editSession.Select(selected);
            if (status != null) status.text = fullSpaceLayout ? "空间原型坊   /   " + items.Count + " 个物体   /   MR 工作台\n选择素材 · 调整布局 · 保存灵感" : (spatialMode ? "MR 空间工作台" : "桌面编辑预览") + "   ·   " + items.Count + " 个物体";
            ShowProperties();
            RefreshObjectHighlights();
        }

        private void RefreshObjectHighlights()
        {
            if(!fullSpaceLayout || objectList==null) return;
            var items=SpaceCanvasRuntime.GetEditableObjects(); var index=0;
            foreach(var button in objectList.GetComponentsInChildren<Button>())
            {
                SpaceCanvasPresentationStyle.Tone(button,index<items.Count && items[index]==selected ? SpaceCanvasPresentationStyle.Selected : SpaceCanvasPresentationStyle.Card);
                index++;
            }
        }

        private void Snapshot()
        {
            if (SceneExporter.TryCaptureCurrentScene(null, out var data, out var error)) SpaceCanvasUndoService.PushSnapshot(data);
            else throw new InvalidOperationException(error);
        }

        private void AddAsset(string id)
        {
            if (spatialMode && placement != null) placement.Begin(id);
            else CommitAsset(id, Vector3.up * .5f);
        }

        public bool CommitAsset(string id, Vector3 localPosition)
        {
            try
            {
                FinishPropertyEdit();
                if (editSession != null) editSession.EndGesture(false);
                Snapshot();
                var root = SpaceCanvasRuntime.CurrentProjectRoot;
                if (root == null)
                {
                    var go = new GameObject("新工程");
                    go.transform.SetParent(SpaceCanvasRuntime.ContentRoot, false);
                    root = go.AddComponent<SpaceCanvasProjectRoot>();
                    root.projectName = "新工程";
                    root.createTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    go.AddComponent<SpaceCanvasSceneSettings>();
                    SpaceCanvasRuntime.SetCurrentProjectRoot(root);
                }
                var instance = SpaceCanvasAssetLibrary.CreateInstance(id, root.transform, out _);
                selected = instance.GetComponent<SpaceCanvasEditableObject>();
                selected.Apply(new SceneObject { id = Guid.NewGuid().ToString("N"), assetId = id, localPosition = new[] { localPosition.x, localPosition.y, localPosition.z } });
                RefreshObjects();
                AssetAdded?.Invoke(selected);
                return true;
            }
            catch (Exception ex) { if (status != null) status.text = "添加失败：" + ex.Message; return false; }
        }

        private void ShowProperties()
        {
            FinishPropertyEdit();
            if (propertyBody == null) return;
            Clear(propertyBody);
            transformFields.Clear();
            if (selected == null)
            {
                if (fullSpaceLayout)
                {
                    SpaceCanvasPresentationStyle.Label(propertyBody,"Empty title","选择物体，开始编辑",new Vector2(0,160),new Vector2(342,40),24);
                    var hint = SpaceCanvasPresentationStyle.Label(propertyBody,"Empty hint","用射线选择工作台上的物体\n或从左侧素材库添加一个模型。\n\n拖动彩色手柄调整布局，\n也可在这里精确输入参数。",new Vector2(0,34),new Vector2(342,170),19,true);
                    SpaceCanvasUnityCompatibility.SetTextWrapping(hint, true);
                }
                else Label(propertyBody, "选择一个场景物体", 19, Vector2.zero, Vector2.one);
                return;
            }
            if(fullSpaceLayout) { ShowRefinedProperties(); return; }
            Label(propertyBody, SpaceCanvasBuiltinAssets.DisplayName(selected.AssetId), 22, new Vector2(0, .9f), Vector2.one);
            var vectors = new[] { selected.transform.localPosition, selected.transform.localEulerAngles, selected.transform.localScale };
            var titles = new[] { "位置 (m)", "旋转 (°)", "缩放" };
            for (var row = 0; row < 3; row++)
            {
                var y = .76f - row * .14f;
                Label(propertyBody, titles[row], 16, new Vector2(.02f, y + .07f), new Vector2(.98f, y + .13f));
                for (var axis = 0; axis < 3; axis++)
                {
                    var field = Field(propertyBody, vectors[row][axis].ToString("0.###", CultureInfo.InvariantCulture), new Vector2(.02f + axis * .33f, y), new Vector2(.32f + axis * .33f, y + .07f));
                    transformFields.Add(field);
                    field.onEndEdit.AddListener(_ => ApplyTransform());
                }
            }
            Label(propertyBody, "颜色 #RRGGBB", 16, new Vector2(.02f, .37f), new Vector2(.98f, .43f));
            var color = Field(propertyBody, selected.ColorHex, new Vector2(.02f, .30f), new Vector2(.98f, .37f));
            color.onEndEdit.AddListener(value =>
            {
                if (selected == null) return;
                if (!ColorUtility.TryParseHtmlString(value, out _)) { status.text = "请输入合法颜色，例如 #38B8CC"; return; }
                Snapshot(); var data = selected.ToSceneObject(); data.colorHex = value; selected.Apply(data);
            });
            Label(propertyBody, "透明度 / 金属度 (0–1)", 16, new Vector2(.02f, .23f), new Vector2(.98f, .29f));
            var alpha = Field(propertyBody, selected.Alpha.ToString(CultureInfo.InvariantCulture), new Vector2(.02f, .16f), new Vector2(.48f, .23f));
            var metal = Field(propertyBody, selected.Metallic.ToString(CultureInfo.InvariantCulture), new Vector2(.52f, .16f), new Vector2(.98f, .23f));
            alpha.onEndEdit.AddListener(value => ApplyScalar(value, true));
            metal.onEndEdit.AddListener(value => ApplyScalar(value, false));
            var collider = ListButton(propertyBody, "碰撞体：" + (selected.ColliderEnabled ? "开" : "关"), () =>
            { if (selected != null) { Snapshot(); selected.ColliderEnabled = !selected.ColliderEnabled; ShowProperties(); } });
            Place(collider.GetComponent<RectTransform>(), new Vector2(.02f, .02f), new Vector2(.98f, .12f));
        }

        private void ApplyTransform()
        {
            if (selected == null || transformFields.Count != 9) return;
            var values = new float[9];
            for (var i = 0; i < values.Length; i++)
                if (!float.TryParse(transformFields[i].text, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || float.IsNaN(values[i]) || float.IsInfinity(values[i]) || Mathf.Abs(values[i]) > 1000000f)
                { status.text = "参数必须是有效数字。"; return; }
            var oldPosition = selected.transform.localPosition;
            var oldRotation = selected.transform.localRotation;
            var oldScale = selected.transform.localScale;
            Snapshot();
            selected.transform.localPosition = new Vector3(values[0], values[1], values[2]);
            selected.transform.localEulerAngles = new Vector3(values[3], values[4], values[5]);
            selected.transform.localScale = new Vector3(values[6], values[7], values[8]);
            if (oldPosition != selected.transform.localPosition || oldRotation != selected.transform.localRotation || oldScale != selected.transform.localScale)
                ObjectEdited?.Invoke(selected);
        }

        /// <summary>Spacious inspector with consistent 42px ray targets and explicit XYZ axes.</summary>
        private void ShowRefinedProperties()
        {
            var title=SpaceCanvasPresentationStyle.Label(propertyBody,"Selection",SpaceCanvasBuiltinAssets.DisplayName(selected.AssetId),new Vector2(0,220),new Vector2(342,36),25);
            title.fontStyle=FontStyles.Bold;
            SpaceCanvasPresentationStyle.Label(propertyBody,"Selection hint","拖动手柄调整 · 每次操作均可撤销",new Vector2(0,190),new Vector2(342,30),18,true);
            SpaceCanvasPresentationStyle.Rule(propertyBody,new Vector2(0,168),342);
            BuildInspectorTabs();
            if (!appearanceVisible)
            {
                BuildPrecisionFields(-54);
                BuildObjectActions(-173);
                var hint = SpaceCanvasPresentationStyle.Label(propertyBody,"Transform hint","箭头移动 · 圆环旋转 · 方柄等比缩放\n一段拖动只记录一次撤销",new Vector2(0,-220),new Vector2(342,60),18,true);
                SpaceCanvasUnityCompatibility.SetTextWrapping(hint, true);
                return;
            }
            var color=Field(propertyBody,selected.ColorHex,Vector2.zero,Vector2.one);
            SpaceCanvasPresentationStyle.Place((RectTransform)color.transform,new Vector2(30,87),new Vector2(282,40));
            ColorUtility.TryParseHtmlString(selected.ColorHex,out var swatch);
            SpaceCanvasPresentationStyle.SurfacePanel(propertyBody,"Color swatch",new Vector2(-151,87),new Vector2(40,40),swatch,10);
            color.onEndEdit.AddListener(value=>
            {
                if(selected==null) return;
                if(!ColorUtility.TryParseHtmlString(value,out _)) { status.text="请输入合法颜色，例如 #D6C4A8"; return; }
                ApplyAppearance(value, selected.Alpha, selected.Metallic);
            });
            BuildAppearancePresets();
            BuildScalarSlider(true, -88, 77);
            BuildScalarSlider(false, 88, 77);
            var collider=SpaceCanvasPresentationStyle.Button(propertyBody,"Collider toggle","物理碰撞     "+(selected.ColliderEnabled ? "已开启" : "已关闭"),new Vector2(0,-164),new Vector2(342,40),()=>
            { if(selected!=null) { Snapshot(); selected.ColliderEnabled=!selected.ColliderEnabled; ShowProperties(); } });
            collider.GetComponentInChildren<Text>().fontSize=18;
            BuildObjectActions(-221);
        }

        private void ApplyScalar(string text, bool isAlpha)
        {
            if (selected == null) return;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || float.IsNaN(value) || value < 0 || value > 1)
            { status.text = "透明度和金属度需在 0–1 之间。"; return; }
            Snapshot(); var data = selected.ToSceneObject();
            if (isAlpha) data.alpha = value; else data.metallic = value;
            selected.Apply(data);
        }

        private static Transform Panel(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(SpaceCanvasRoundedPanel)); go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Background; Place(go.GetComponent<RectTransform>(), min, max); return go.transform;
        }

        private static Text Label(Transform parent, string value, int size, Vector2 min, Vector2 max)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>(); text.font = Font; text.text = value; text.fontSize = size; text.color = SpaceCanvasPresentationStyle.Ink;
            text.alignment = TextAlignmentOptions.Left; text.raycastTarget = false; Place(text.rectTransform, min, max); return text;
        }

        private static InputField Field(Transform parent, string value, Vector2 min, Vector2 max)
        {
            var go = new GameObject("Value", typeof(RectTransform), typeof(SpaceCanvasRoundedPanel), typeof(InputField)); go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), min, max); go.GetComponent<Image>().color = SpaceCanvasPresentationStyle.Card;
            go.GetComponent<SpaceCanvasRoundedPanel>().radius=10;
            var text = Label(go.transform, value, 18, new Vector2(.04f, 0), new Vector2(.96f, 1));
            text.alignment=TextAlignmentOptions.MidlineLeft;
            var input = go.GetComponent<InputField>(); input.textViewport = go.GetComponent<RectTransform>(); input.textComponent = text; input.text = value; return input;
        }

        private static Button ListButton(Transform parent, string title, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(title, typeof(RectTransform), typeof(SpaceCanvasRoundedPanel), typeof(Button), typeof(LayoutElement)); go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 40; go.GetComponent<Image>().color = SpaceCanvasPresentationStyle.Card;
            var button = go.GetComponent<Button>(); button.targetGraphic = go.GetComponent<Image>(); button.onClick.AddListener(action);
            var text = Label(go.transform, title, 18, new Vector2(.05f, 0), new Vector2(.95f, 1)); text.alignment = TextAlignmentOptions.Center;
            SpaceCanvasPresentationStyle.ButtonStyle(button); return button;
        }

        private static Transform Scroll(Transform parent, Vector2 min, Vector2 max)
        {
            var viewport = Panel(parent, "Scroll", min, max); viewport.gameObject.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)); content.transform.SetParent(viewport, false);
            var rect = content.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1); rect.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>(); layout.spacing = 6; layout.childControlHeight = true; layout.childForceExpandHeight = false; layout.childControlWidth = true; layout.childForceExpandWidth = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.content = rect; scroll.viewport = viewport.GetComponent<RectTransform>(); scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; return content.transform;
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max) { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }
        private static void Clear(Transform parent) { for (var i = parent.childCount - 1; i >= 0; i--) Dispose(parent.GetChild(i).gameObject); }
        private static void Dispose(GameObject go) { go.SetActive(false); go.transform.SetParent(null); if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
    }
}
