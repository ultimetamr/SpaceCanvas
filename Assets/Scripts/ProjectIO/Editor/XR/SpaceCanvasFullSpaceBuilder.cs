using System;
using System.IO;
using System.Linq;
using ByteDance.PICO.XR;
using ByteDance.PICO.XR.Editor;
using SpaceCanvas.ProjectIO.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.OpenXR;
#if ENABLE_PICO_OPENXR_SDK
using ByteDance.PICO.OpenXR;
using ByteDance.PICO.OpenXR.Interactions;
using UnityEngine.XR.OpenXR.Features.Interactions;
#endif

namespace SpaceCanvas.ProjectIO.Editor.XR
{
    /// <summary>
    /// Full Space MR authoring/build pipeline. Uses the installed PICO OpenXR features,
    /// not a transparent SpatialAdapter Activity. Original shared-space scene is retained.
    /// A request survives the SDK define/domain reload before the scene is generated.
    /// </summary>
    [InitializeOnLoad]
    public static class SpaceCanvasFullSpaceBuilder
    {
        public const string ScenePath = "Assets/Scenes/SpaceCanvas_FullSpaceMR.unity";
        private const string Request = "Library/SpaceCanvas.FullSpace.request";
        private const string BuildRequest = "Library/SpaceCanvas.BuildFullSpaceAndroid.request";
        private const string CleanBuildRequest = "Library/SpaceCanvas.CleanBuildFullSpaceAndroid.request";
        private const string PresentationRequest = "Library/SpaceCanvas.Presentation.request";
        private const string PreviewRequest = "Library/SpaceCanvas.MRPreview.request";
        private static bool switching;
        static SpaceCanvasFullSpaceBuilder() { EditorApplication.update += Poll; }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/打开 Full Space MR 工作台")]
#endif
        public static void RequestScene() { File.WriteAllText(Request, "FullSpace"); }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/构建 Full Space MR Android APK")]
#endif
        public static void RequestBuild() { File.WriteAllText(Request, "FullSpace"); File.WriteAllText(BuildRequest, "build"); }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/重建 MR 界面与射线")]
#endif
        public static void RequestPresentation() { File.WriteAllText(PresentationRequest, "rebuild"); }

        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || switching) return;
            if (!File.Exists(Request) && !File.Exists(BuildRequest) && !File.Exists(CleanBuildRequest) && !File.Exists(PresentationRequest) && !File.Exists(PreviewRequest)) return;
            try
            {
#if !ENABLE_PICO_OPENXR_SDK || PICO_MS_SDK
                switching = true;
                ConfigureOpenXR();
                return; // Pending request is consumed only after the new SDK mode compiles.
#else
                ConfigureOpenXR();
                if (File.Exists(Request)) { Consume(Request); BuildScene(); }
                if (File.Exists(PresentationRequest)) { Consume(PresentationRequest); ConfigurePresentation(); }
                if (File.Exists(PreviewRequest)) { Consume(PreviewRequest); RenderPresentationPreview(); }
                if (File.Exists(BuildRequest)) { Consume(BuildRequest); BuildAndroid(); }
                if (File.Exists(CleanBuildRequest)) { Consume(CleanBuildRequest); BuildAndroid(true); }
#endif
            }
            catch (Exception ex)
            {
                switching = false;
                if (File.Exists(Request)) Consume(Request);
                if (File.Exists(BuildRequest)) Consume(BuildRequest);
                if (File.Exists(CleanBuildRequest)) Consume(CleanBuildRequest);
                if (File.Exists(PresentationRequest)) Consume(PresentationRequest);
                if (File.Exists(PreviewRequest)) Consume(PreviewRequest);
                File.WriteAllText("Library/SpaceCanvas.FullSpace.txt", "FAIL\n" + ex);
                Debug.LogException(ex);
            }
        }
        private static void Consume(string path) { File.Move(path, path + "." + DateTime.UtcNow.Ticks + ".started"); }

        private static void ConfigureOpenXR()
        {
            // Preserve generated Spatial manifests/configuration before changing renderer mode.
            var backup = "Library/SpaceCanvas.BeforeFullSpace";
            Directory.CreateDirectory(backup);
            foreach (var path in new[] {"Assets/Plugins/Android/AndroidManifest.xml", "Assets/Plugins/Android/LauncherManifest.xml",
                "Assets/Resources/PXR_ProjectSetting.asset", "Assets/XR/Settings/PXR_Settings.asset", "ProjectSettings/ProjectSettings.asset"})
                if (File.Exists(path) && !File.Exists(Path.Combine(backup, Path.GetFileName(path)))) File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));
            var settings = PXR_Settings.GetSettings();
            if (settings == null) throw new InvalidOperationException("缺少 PICO 设置。");
            settings.appMode = PXR_Settings.AppMode.XR; EditorUtility.SetDirty(settings);
            var config = PXR_ProjectSetting.GetProjectConfig();
            config.isSpatialAdapter = false; config.videoSeeThrough = true; config.handTracking = true;
            PXR_ProjectSetting.SaveAssets();
            foreach (var path in new[] {"Assets/Plugins/Android/AndroidManifest.xml", "Assets/Plugins/Android/LauncherManifest.xml"})
                if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (general == null || general.Manager == null) throw new InvalidOperationException("缺少 Android XR Management 设置。");
            const string loaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
            if (general.Manager.activeLoaders.Count != 1 || general.Manager.activeLoaders[0].GetType().FullName != loaderType)
            {
                foreach (var loader in general.Manager.activeLoaders.ToArray()) XRPackageMetadataStore.RemoveLoader(general.Manager, loader.GetType().FullName, BuildTargetGroup.Android);
                if (!XRPackageMetadataStore.AssignLoader(general.Manager, loaderType, BuildTargetGroup.Android)) throw new InvalidOperationException("OpenXR Loader 注册失败。");
            }
            general.InitManagerOnStart = true; general.Manager.automaticLoading = true; general.Manager.automaticRunning = true;
            EditorUtility.SetDirty(general); EditorUtility.SetDirty(general.Manager);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.spacecanvas.prototype");
            PlayerSettings.bundleVersion = "0.3.2"; PlayerSettings.Android.bundleVersionCode = 5;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] {UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3});
            var adapter = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Settings/SpatialAdapterRuntimeSettings.asset");
            if (adapter != null) { var data = new SerializedObject(adapter); data.FindProperty("m_enableSpatialAdapter").boolValue = false; data.ApplyModifiedPropertiesWithoutUndo(); }
            AssetDatabase.SaveAssets();
            PXR_Utils.UpdateSDKSymbols();
#if ENABLE_PICO_OPENXR_SDK
            var openXRConfig = PXR_OpenXRProjectSetting.GetProjectConfig();
            openXRConfig.isHandTracking = true;
            openXRConfig.handTrackingSupportType = HandTrackingSupport.ControllersAndHands;
            EditorUtility.SetDirty(openXRConfig);
            PXR_Utils.EnableOpenXRFeature<PICOFeature>();
            PXR_Utils.EnableOpenXRFeature<OpenXRExtensions>();
            PXR_Utils.EnableOpenXRFeature<PassthroughFeature>();
            PXR_Utils.EnableOpenXRFeature<PICO4ControllerProfile>();
            PXR_Utils.EnableOpenXRFeature<PICO4UltraControllerProfile>();
            PXR_Utils.EnableOpenXRFeature<PICONeo3ControllerProfile>();
            PXR_Utils.EnableOpenXRFeature<PICOG3ControllerProfile>();
            PXR_Utils.EnableOpenXRFeature<HandInteractionProfile>();
            PXR_Utils.EnableHandTrackingFeature();
            AssetDatabase.SaveAssets();
#endif
        }

        private static void BuildScene()
        {
            var scene = EditorSceneManager.OpenScene(File.Exists(ScenePath) ? ScenePath : SpaceCanvasSceneBootstrap.ScenePath);
            var workspace = UnityEngine.Object.FindObjectOfType<SpaceCanvasWorkspace>();
            if (workspace == null || workspace.contentRoot == null) throw new InvalidOperationException("工作台场景内容缺失。");
            if (File.Exists(ScenePath)) { EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene(ScenePath, true)}; return; }
            var ui = workspace.GetComponent<SpaceCanvasWorkspaceUI>();
            var projectUI = workspace.projectUI;
            var session = workspace.GetComponent<SpaceCanvasEditSession>() ?? workspace.gameObject.AddComponent<SpaceCanvasEditSession>();
            session.workspaceUI = ui; session.projectUI = projectUI; ui.editSession = session;
            ui.spatialMode = true; projectUI.spatialMode = true; projectUI.createSwanWindowHost = false;
            projectUI.viewCamera = workspace.viewCamera; ui.viewCamera = workspace.viewCamera;
            projectUI.BuildRuntimeToolbar(); ui.BuildInterface();
            var anchor = new GameObject("Full Space Workbench Anchor").transform;
            anchor.position = new Vector3(0, 1.4f, 1.5f);
            workspace.transform.SetParent(anchor, false);
            workspace.contentRoot.localPosition = new Vector3(0, -.38f, .25f); workspace.contentRoot.localScale = Vector3.one * .14f;
            var rig = new GameObject("Full Space OpenXR Origin");
            var origin = rig.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset"); offset.transform.SetParent(rig.transform, false);
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device; origin.CameraYOffset = 1.6f;
            offset.transform.localPosition = Vector3.up * 1.6f;
            var camera = workspace.viewCamera;
            SpaceCanvasRuntime.ContentRoot = workspace.contentRoot;
            SpaceCanvasRuntime.SetCurrentProjectRoot(workspace.contentRoot.GetComponentInChildren<SpaceCanvasProjectRoot>(true));
            camera.transform.SetParent(offset.transform, false); camera.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.nearClipPlane = .05f; camera.farClipPlane = 100; camera.stereoTargetEye = StereoTargetEyeMask.Both; origin.Camera = camera;
            var driver = camera.gameObject.AddComponent<TrackedPoseDriver>();
            driver.positionInput = new InputActionProperty(new InputAction("Head Position", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            driver.rotationInput = new InputActionProperty(new InputAction("Head Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            driver.ignoreTrackingState = true;
            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>(true))
            {
                if (!canvas.name.StartsWith("SpaceCanvas")) continue;
                canvas.transform.SetParent(anchor, false); canvas.transform.localPosition = Vector3.zero; canvas.transform.localRotation = Quaternion.identity;
                canvas.transform.localScale = Vector3.one * .001125f; canvas.worldCamera = camera;
                if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null) canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            }
            var events = UnityEngine.Object.FindObjectOfType<EventSystem>() ?? new GameObject("EventSystem").AddComponent<EventSystem>();
            foreach (var module in events.GetComponents<BaseInputModule>()) UnityEngine.Object.DestroyImmediate(module);
            events.gameObject.AddComponent<XRUIInputModule>();
            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            foreach (var left in new[] {true, false})
            {
                var pointer = new GameObject(left ? "Left OpenXR Pointer" : "Right OpenXR Pointer");
                pointer.transform.SetParent(offset.transform, false);
                var ray = pointer.AddComponent<XRRayInteractor>(); ray.enableUIInteraction = true; ray.maxRaycastDistance = 10;
                pointer.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
                var input = pointer.AddComponent<SpaceCanvasOpenXRPointer>(); input.leftHand = left; input.session = session;
            }
            var runtime = rig.AddComponent<SpaceCanvasFullSpaceRuntime>(); runtime.head = camera; runtime.workbench = anchor;
            var gaze = rig.AddComponent<SpaceCanvasEyeGazeInteractor>(); gaze.head = camera;
            var gazeLine = rig.AddComponent<LineRenderer>();
            gazeLine.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/SpaceCanvasMaterials/OpenXRPointer.mat");
            gaze.line = gazeLine;
            var bounds = workspace.GetComponent<SpaceCanvasSelectionBounds>() ?? workspace.gameObject.AddComponent<SpaceCanvasSelectionBounds>(); bounds.session = session;
            SpaceCanvasSceneBootstrap.SaveSceneWithStableFonts(scene, ScenePath);
            EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene(ScenePath, true)};
            AssetDatabase.SaveAssets();
            File.WriteAllText("Library/SpaceCanvas.FullSpace.txt", "PASS\n" + ScenePath + "\nOpenXR / Full Space MR / no SpatialAdapter window\n");
            Debug.Log("[SpaceCanvas FullSpace] Saved " + ScenePath);
        }

        /// <summary>Batch-mode entry point that preserves the authored MR scene and its project content.</summary>
        public static void BuildExistingAndroid()
        {
            ConfigureOpenXR();
            BuildAndroid();
        }

        /// <summary>Render the authored workspace for inspection without saving preview state to its scene.</summary>
        public static void PreviewExistingSceneBatch()
        {
            SpaceCanvasTmpShaderCompatibility.ValidateInstalledShaders();
            SpaceCanvasPanelMaterialBuilder.Ensure();
            EditorSceneManager.OpenScene(ScenePath);
            var runtime = UnityEngine.Object.FindObjectOfType<SpaceCanvasFullSpaceRuntime>();
            if (runtime != null) SpaceCanvasUIClarity.Configure(runtime.workbench);
            RenderPresentationPreview();
        }

        private static void BuildAndroid(bool cleanCache = false)
        {
            if (!File.Exists(ScenePath)) BuildScene();
            SpaceCanvasEditorAssetBridge.SaveWorkspaceSceneForBuild(ScenePath);
            SpaceCanvasPlacementMaterialBuilder.Ensure();
            SpaceCanvasPanelMaterialBuilder.Ensure();
            SpaceCanvasAppIcon.Configure();
            ConfigurePointerVisuals();
            Directory.CreateDirectory("Builds");
            BuildReport report;
            // Unity 6's bundled JDK uses Windows Unix-domain sockets for Gradle IPC.
            // Inherited DOS-short TEMP paths can fail with "Invalid argument: connect".
            // Use a normal project-local path for this build and restore the editor environment.
            var previousTemp = Environment.GetEnvironmentVariable("TEMP");
            var previousTmp = Environment.GetEnvironmentVariable("TMP");
            try
            {
#if UNITY_6000_0_OR_NEWER && UNITY_EDITOR_WIN
                var javaTemp = Path.GetFullPath("Temp/SpaceCanvasJava");
                Directory.CreateDirectory(javaTemp);
                Environment.SetEnvironmentVariable("TEMP", javaTemp);
                Environment.SetEnvironmentVariable("TMP", javaTemp);
#endif
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes = new[] {ScenePath}, target = BuildTarget.Android,
                    locationPathName = "Builds/SpaceCanvas-FullSpaceMR.apk",
                    options = BuildOptions.Development | (cleanCache ? BuildOptions.CleanBuildCache : BuildOptions.None)});
            }
            finally
            {
                Environment.SetEnvironmentVariable("TEMP", previousTemp);
                Environment.SetEnvironmentVariable("TMP", previousTmp);
            }
            File.WriteAllText("Library/SpaceCanvas.FullSpaceBuild.txt", report.summary.result + "\nErrors=" + report.summary.totalErrors + "\nWarnings=" + report.summary.totalWarnings + "\n" + report.summary.outputPath);
            File.WriteAllLines("Library/SpaceCanvas.FullSpaceBuild.Diagnostics.txt", report.steps.SelectMany(s => s.messages)
                .Where(m => m.type == LogType.Warning || m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.type + ": " + m.content));
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors > 0)
                Debug.LogError("Full Space MR 构建未通过，请查看 Library/SpaceCanvas.FullSpaceBuild.txt。");
        }

        /// <summary>Explicit authoring migration. Normal builds preserve subsequent scene edits.</summary>
        private static void ConfigurePresentation()
        {
            if (!File.Exists(ScenePath)) BuildScene();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var workspace = UnityEngine.Object.FindObjectOfType<SpaceCanvasWorkspace>();
            var runtime = UnityEngine.Object.FindObjectOfType<SpaceCanvasFullSpaceRuntime>();
            if (workspace == null || runtime == null || runtime.workbench == null)
                throw new InvalidOperationException("Full Space 工作台未配置。");
            var ui = workspace.GetComponent<SpaceCanvasWorkspaceUI>();
            var projectUI = workspace.projectUI;
            var session = workspace.GetComponent<SpaceCanvasEditSession>();
            var handles = workspace.GetComponent<SpaceCanvasTransformHandles>();
            if (handles == null) handles = workspace.gameObject.AddComponent<SpaceCanvasTransformHandles>();
            handles.session = session;
            var camera = workspace.viewCamera;
            SpaceCanvasRuntime.ContentRoot = workspace.contentRoot;
            SpaceCanvasRuntime.SetCurrentProjectRoot(workspace.contentRoot.GetComponentInChildren<SpaceCanvasProjectRoot>(true));
            ui.fullSpaceLayout = projectUI.fullSpaceLayout = true;
            ui.spatialMode = projectUI.spatialMode = true;
            var placement = workspace.GetComponent<SpaceCanvasPlacement>();
            if (placement == null) placement = workspace.gameObject.AddComponent<SpaceCanvasPlacement>();
            placement.workspace = ui; placement.projectUI = projectUI; ui.placement = placement;
            var projectSession = workspace.GetComponent<SpaceCanvasProjectSession>();
            if (projectSession == null) projectSession = workspace.gameObject.AddComponent<SpaceCanvasProjectSession>();
            projectSession.projectUI = projectUI;
            var comfort = workspace.GetComponent<SpaceCanvasWorkspaceSettingsUI>();
            if (comfort == null) comfort = workspace.gameObject.AddComponent<SpaceCanvasWorkspaceSettingsUI>();
            comfort.projectUI = projectUI; comfort.workspace = ui; runtime.comfort = comfort;
            if (workspace.GetComponent<SpaceCanvasPerformanceMonitor>() == null) workspace.gameObject.AddComponent<SpaceCanvasPerformanceMonitor>();
            projectUI.BuildRuntimeToolbar(); ui.BuildInterface();
            ui.ConfigureFullSpaceLayout(runtime.workbench, camera);
            projectUI.ConfigureFullSpaceLayout(runtime.workbench, camera);
            placement.BuildView(projectUI.ViewRoot, camera);
            projectUI.BuildRecoveryView();
            comfort.BuildView(projectUI.ViewRoot, camera);
            projectSession.BuildStatus(projectUI.ViewRoot);
            var tutorial = workspace.GetComponent<SpaceCanvasOnboarding>();
            if (tutorial == null) tutorial = workspace.gameObject.AddComponent<SpaceCanvasOnboarding>();
            tutorial.workspace = ui; tutorial.projectUI = projectUI; tutorial.editSession = session;
            tutorial.BuildView(projectUI.ViewRoot, camera);
            workspace.contentRoot.localPosition = new Vector3(0, -.20f, .16f);
            workspace.contentRoot.localScale = Vector3.one * .16f;

            var presentation = runtime.GetComponent<SpaceCanvasMRPresentation>();
            if (presentation == null) presentation = runtime.gameObject.AddComponent<SpaceCanvasMRPresentation>();
            presentation.workspace = ui; presentation.projectUI = projectUI; presentation.runtime = runtime;
            presentation.onboarding = tutorial;
            var origin = runtime.GetComponent<XROrigin>();
            var offset = origin.CameraFloorOffsetObject.transform;
            foreach (var old in UnityEngine.Object.FindObjectsOfType<SpaceCanvasOpenXRPointer>(true))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            foreach (var left in new[] {true, false})
            {
                var pointer = new GameObject(left ? "Left OpenXR Pointer" : "Right OpenXR Pointer");
                pointer.transform.SetParent(offset, false);
                var ray = pointer.AddComponent<SpaceCanvasRayInteractor>();
                ray.enableUIInteraction = true; ray.maxRaycastDistance = 10;
                pointer.AddComponent<LineRenderer>();
                var input = pointer.AddComponent<SpaceCanvasOpenXRPointer>();
                input.leftHand = left; input.session = session;
                pointer.AddComponent<SpaceCanvasPointerVisual>();
            }
            var gazeFallback = runtime.GetComponent<SpaceCanvasEyeGazeInteractor>();
            if (gazeFallback == null) gazeFallback = runtime.gameObject.AddComponent<SpaceCanvasEyeGazeInteractor>();
            gazeFallback.head = camera;
            var gazeLine = gazeFallback.line ?? runtime.gameObject.GetComponent<LineRenderer>();
            if (gazeLine == null) gazeLine = runtime.gameObject.AddComponent<LineRenderer>();
            gazeLine.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/SpaceCanvasMaterials/OpenXRPointer.mat");
            gazeFallback.line = gazeLine;
            // Nested Canvas is a separate raycast boundary. Root canvases are layout
            // anchors only; leaving a raycaster there can hit a second, hidden UI plane.
            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>(true))
            {
                canvas.worldCamera = camera;
                var graphic = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                var tracked = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
                var enabled = graphic != null && graphic.enabled;
                if (enabled && tracked == null) tracked = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
                if (tracked != null) tracked.enabled = enabled;
            }
            var module = UnityEngine.Object.FindObjectOfType<XRUIInputModule>();
            if (module != null) { module.uiCamera = camera; module.enableXRInput = true; }
            SpaceCanvasFontBuilder.EnsurePresentationCharacters();
            SpaceCanvasSceneBootstrap.SaveSceneWithStableFonts(scene, ScenePath);
            ConfigurePointerVisuals();
            File.WriteAllText("Library/SpaceCanvas.Presentation.txt", "PASS\nIndependent side docks / compact toolbar / press-edge pointer\n");
        }

        public static void RebuildMRPresentationBatch()
        {
            ConfigureOpenXR();
            ConfigurePresentation();
            RenderPresentationPreview();
        }

        public static void BuildMRBatch() { ConfigureOpenXR(); BuildAndroid(); }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/测试/渲染 MR 布局预览")]
#endif
        public static void RenderPresentationPreview()
        {
            var workspace = UnityEngine.Object.FindObjectOfType<SpaceCanvasWorkspace>();
            if (workspace == null) throw new InvalidOperationException("请先打开 Full Space 工作台。");
            SpaceCanvasRuntime.ContentRoot = workspace.contentRoot;
            SpaceCanvasRuntime.SetCurrentProjectRoot(workspace.contentRoot.GetComponentInChildren<SpaceCanvasProjectRoot>(true));
            var ui = workspace.GetComponent<SpaceCanvasWorkspaceUI>();
            var projectUI = workspace.projectUI;
            var go = new GameObject("Temporary MR Preview Camera");
            var preview = go.AddComponent<Camera>(); preview.CopyFrom(workspace.viewCamera);
            preview.transform.SetPositionAndRotation(workspace.viewCamera.transform.position, workspace.viewCamera.transform.rotation);
            preview.stereoTargetEye = StereoTargetEyeMask.None;
            preview.fieldOfView = 64; preview.aspect = 1.6f;
            preview.clearFlags = CameraClearFlags.SolidColor; preview.backgroundColor = new Color(.065f,.085f,.105f,1);
            var target = new RenderTexture(1920,1200,24,RenderTextureFormat.ARGB32);
            var oldTarget = RenderTexture.active;
            var texture = new Texture2D(1920,1200,TextureFormat.RGB24,false);
            preview.targetTexture = target;
            var previousModal = projectUI.ModalOpen;
            try
            {
                Directory.CreateDirectory("Library/MRPreview");
                foreach (var state in new[] {"workspace", "handles-move", "handles-rotate", "handles-scale", "appearance", "tutorial", "tutorial-add", "tutorial-edit", "tutorial-save", "tutorial-import", "tutorial-complete", "focus", "about", "import", "confirm", "layout", "recovery", "placement"})
                {
                    var session = workspace.GetComponent<SpaceCanvasEditSession>();
                    var handles = workspace.GetComponent<SpaceCanvasTransformHandles>();
                    if (state.StartsWith("handles-") || state == "appearance")
                    {
                        var item = SpaceCanvasRuntime.GetEditableObjects().FirstOrDefault(x => x.AssetId == "furniture.armchair")
                            ?? SpaceCanvasRuntime.GetEditableObjects().FirstOrDefault();
                        session.Select(item);
                        session.tool = state == "handles-rotate" ? SpaceCanvasEditTool.Rotate : state == "handles-scale" ? SpaceCanvasEditTool.Scale : SpaceCanvasEditTool.Move;
                        ui.SetPrecisionMode(session.tool);
                        if (state == "appearance")
                            ui.ViewRoot.GetComponentsInChildren<UnityEngine.UI.Button>().First(x => x.name == "外观页").onClick.Invoke();
                        if (handles != null) { handles.InitializeView(); handles.RefreshView(); }
                    }
                    var tutorial = workspace.GetComponent<SpaceCanvasOnboarding>();
                    if (state == "tutorial")
                    { session.Select(null); if (handles != null) handles.RefreshView(); if (tutorial != null) tutorial.Restart(); }
                    if (state.StartsWith("tutorial-") && tutorial != null) { tutorial.Flow.Continue(); tutorial.RefreshView(); }
                    if (state == "focus" && tutorial != null) { tutorial.Flow.Dismiss(); tutorial.RefreshView(); }
                    if (state == "focus") ui.TogglePanels();
                    if (state == "about") { ui.TogglePanels(); projectUI.ShowAboutOpenSource(); ui.SetModalVisibility(true); }
                    if (state == "import") { projectUI.CloseAboutPanel(); projectUI.OpenImportPanel(); projectUI.ShowOfficialExamples(); ui.SetModalVisibility(true); }
                    if (state == "confirm")
                    {
                        projectUI.ShowOfficialExamples();
                        projectUI.officialListRoot.GetComponentInChildren<UnityEngine.UI.Button>().onClick.Invoke();
                        ui.SetModalVisibility(true);
                    }
                    if (state == "layout") { workspace.GetComponent<SpaceCanvasWorkspaceSettingsUI>().Open(); ui.SetModalVisibility(true); }
                    if (state == "recovery") { projectUI.OpenExternalPanel(projectUI.recoveryPanel); ui.SetModalVisibility(true); }
                    if (state == "placement")
                    {
                        projectUI.CloseExternalPanel(projectUI.recoveryPanel); ui.SetModalVisibility(false);
                        ui.placement.Begin("furniture.chair");
                        var point = SpaceCanvasRuntime.ContentRoot.TransformPoint(new Vector3(1.8f,0,-1));
                        ui.placement.UpdateRay(1,new Ray(point + Vector3.up,Vector3.down));
                    }
                    Canvas.ForceUpdateCanvases(); preview.Render();
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0,0,1920,1200),0,0); texture.Apply();
                    File.WriteAllBytes("Library/MRPreview/"+state+".png", texture.EncodeToPNG());
                }
            }
            finally
            {
                ui.placement.Cancel();
                projectUI.CloseExternalPanel(projectUI.layoutPanel); projectUI.CloseExternalPanel(projectUI.recoveryPanel);
                projectUI.CancelImport(); projectUI.CloseImportPanel(); projectUI.CloseAboutPanel(); ui.SetModalVisibility(previousModal);
                RenderTexture.active = oldTarget; preview.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void ConfigurePointerVisuals()
        {
            // XRI requires a LineRenderer but does not create its material. Persist an
            // unlit material so Android shader stripping cannot remove the ray shader.
            var scene = EditorSceneManager.OpenScene(ScenePath);
            const string materialPath = "Assets/Resources/SpaceCanvasMaterials/OpenXRPointer.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) throw new InvalidOperationException("缺少射线所需的 Sprites/Default shader。");
                material = new Material(shader) { name = "SpaceCanvas OpenXR Pointer" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            foreach (var pointer in UnityEngine.Object.FindObjectsOfType<SpaceCanvasOpenXRPointer>(true))
            {
                var line = pointer.GetComponent<LineRenderer>();
                if (line == null) throw new InvalidOperationException("射线缺少 LineRenderer。");
                line.sharedMaterial = material;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                EditorUtility.SetDirty(line);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
    }
}
