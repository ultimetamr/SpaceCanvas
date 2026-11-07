using System;
using System.IO;
using System.Linq;
using ByteDance.PICO.SpatialAdapter;
using ByteDance.PICO.SpatialAdapter.Exporter.Editor;
using ByteDance.PICO.XR;
using ByteDance.PICO.XR.Editor;
using SpaceCanvas.ProjectIO.Spatial;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace SpaceCanvas.ProjectIO.Editor.Spatial
{
    /// <summary>
    /// 创建实际 Shared Space 入口。当前 SDK 只允许一个活动 SpatialCamera，
    /// 所以使用一个系统管理的三维工作台容器，内置世界空间 uGUI 面板。
    /// 不把普通 Canvas 命名为独立原生窗口。保留桌面场景供 JSON 开发调试。
    /// </summary>
    [InitializeOnLoad]
    public static class SpaceCanvasMRSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/SpaceCanvas_MR.unity";
        private const string Request = "Library/SpaceCanvas.BuildMR.request";
        static SpaceCanvasMRSceneBuilder() { EditorApplication.update += Poll; }

        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (Consume(Request))
            {
                try { Build(); File.WriteAllText("Library/SpaceCanvas.MRSetup.txt", "PASS " + DateTime.UtcNow.ToString("O") + "\n" + ScenePath); }
                catch (Exception e) { File.WriteAllText("Library/SpaceCanvas.MRSetup.txt", "FAIL\n" + e); Debug.LogException(e); }
            }
            const string layoutRequest = "Library/SpaceCanvas.RefreshMR.request";
            if (Consume(layoutRequest))
            {
                RefreshLayout();
            }
            const string buildRequest = "Library/SpaceCanvas.BuildAndroid.request";
            if (!Consume(buildRequest)) return;
            BuildAndroid();
        }

        private static bool Consume(string path)
        {
            if (!File.Exists(path)) return false;
            try { File.Move(path, path + "." + DateTime.UtcNow.Ticks + ".started"); return true; }
            catch (IOException) { return false; } // 写入方尚未关闭句柄，下一次更新再消费。
        }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/打开 MR 空间工作台")]
#endif
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            SpaceCanvasFontBuilder.EnsureFont();
            if (!File.Exists(SpaceCanvasSceneBootstrap.ScenePath)) SpaceCanvasSceneBootstrap.Build();
            // 已有 MR 场景保留用户布置；菜单再次执行只打开场景。
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            var scene = EditorSceneManager.OpenScene(SpaceCanvasSceneBootstrap.ScenePath);
            var workspace = UnityEngine.Object.FindObjectOfType<SpaceCanvasWorkspace>();
            if (workspace == null || workspace.contentRoot == null) throw new InvalidOperationException("启动场景缺少工作台入口。");
            ConfigureProject();
            workspace.contentRoot.localPosition = new Vector3(0, -.12f, .05f);
            workspace.contentRoot.localScale = Vector3.one * .14f;
            var camera = workspace.viewCamera;
            camera.transform.SetPositionAndRotation(new Vector3(0, .1f, -2.6f), Quaternion.identity);
            camera.orthographic = false; camera.fieldOfView = 42;
            camera.backgroundColor = new Color(.025f, .045f, .065f, 0);
            var spatialCamera = UnityEngine.Object.FindObjectOfType<SpatialCamera>();
            if (spatialCamera == null)
            {
                var go = new GameObject("SpatialCamera"); Undo.RegisterCreatedObjectUndo(go, "创建 MR 空间工作台");
                spatialCamera = go.AddComponent<SpatialCamera>();
                spatialCamera.Dimensions = new Vector3(2f, 1.5f, 1.2f);
            }
            spatialCamera.transform.position = new Vector3(0, .1f, 0);
            if (spatialCamera.GetComponent<PXR_SpatialCameraSpatialModeSynchronizer>() == null)
                Undo.AddComponent<PXR_SpatialCameraSpatialModeSynchronizer>(spatialCamera.gameObject);
            const string configPath = "Assets/Settings/SpaceCanvasMRConfiguration.asset";
            var config = AssetDatabase.LoadAssetAtPath<SpatialCameraConfiguration>(configPath);
            if (config == null) { config = ScriptableObject.CreateInstance<SpatialCameraConfiguration>(); AssetDatabase.CreateAsset(config, configPath); }
            config.mode = SpatialCamera.CameraMode.Constrained;
            config.outputDimensions = spatialCamera.Dimensions;
            spatialCamera.OutputConfiguration = config; spatialCamera.OpenWindowOnLoad = true;
            EditorUtility.SetDirty(config);
            var runtimeSettings = AssetDatabase.LoadAssetAtPath<SpatialAdapterRuntimeSettings>(SpatialAdapterRuntimeSettings.k_SettingsPath);
            if (runtimeSettings != null)
            {
                var settings = new SerializedObject(runtimeSettings);
                settings.FindProperty("m_enableSpatialAdapter").boolValue = true;
                settings.FindProperty("m_autoCreateSpatialCamera").boolValue = false;
                settings.FindProperty("m_autoAddUIHoverEffect").boolValue = true;
                settings.FindProperty("m_defaultSpatialCameraConfiguration").objectReferenceValue = config;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            var ui = workspace.GetComponent<SpaceCanvasWorkspaceUI>();
            var projectUI = workspace.projectUI;
            var session = workspace.GetComponent<SpaceCanvasEditSession>() ?? workspace.gameObject.AddComponent<SpaceCanvasEditSession>();
            session.workspaceUI = ui; session.projectUI = projectUI;
            ui.spatialMode = true; ui.viewCamera = camera; ui.editSession = session;
            projectUI.spatialMode = true; projectUI.viewCamera = camera; projectUI.createSwanWindowHost = false;
            var input = workspace.GetComponent<SpaceCanvasSpatialInput>() ?? workspace.gameObject.AddComponent<SpaceCanvasSpatialInput>();
            input.session = session; input.previewCamera = camera;
            var bounds = workspace.GetComponent<SpaceCanvasSelectionBounds>() ?? workspace.gameObject.AddComponent<SpaceCanvasSelectionBounds>();
            bounds.session = session;
            if (workspace.GetComponent<SpaceCanvasFontWarmup>() == null) workspace.gameObject.AddComponent<SpaceCanvasFontWarmup>();
            SpaceCanvasRuntime.ContentRoot = workspace.contentRoot;
            SpaceCanvasRuntime.SetCurrentProjectRoot(workspace.contentRoot.GetComponentInChildren<SpaceCanvasProjectRoot>(true));
            projectUI.BuildRuntimeToolbar(); ui.BuildInterface();
            SpaceCanvasSceneBootstrap.SaveSceneWithStableFonts(scene, ScenePath);
            // Android 只构建 MR 入口，避免把桌面预览作为可启动场景交付。
            EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene(ScenePath, true)}
                .Concat(EditorBuildSettings.scenes.Where(s => s.path != ScenePath).Select(s => new EditorBuildSettingsScene(s.path, false))).ToArray();
            AssetDatabase.SaveAssets();
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.LookAt(new Vector3(0, .1f, 0), Quaternion.identity, 2.6f);
            Debug.Log("SpaceCanvas MR 入口已保存：" + ScenePath + "。一个原生三维容器，内部空间面板；设备行为需在 PICO Emulator/真机验证。");
        }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/刷新 MR 工作台布局")]
#endif
        public static void RefreshLayout()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var workspace = UnityEngine.Object.FindObjectOfType<SpaceCanvasWorkspace>();
            if (workspace == null || workspace.gameObject.scene.path != ScenePath) return;
            SpaceCanvasFontBuilder.EnsureFont();
            if (workspace.GetComponent<SpaceCanvasFontWarmup>() == null) workspace.gameObject.AddComponent<SpaceCanvasFontWarmup>();
            workspace.contentRoot.localPosition = new Vector3(0, -.12f, .05f);
            var camera = workspace.viewCamera;
            camera.transform.position = new Vector3(0, .65f, -2.6f);
            camera.transform.LookAt(new Vector3(0, .04f, 0));
            workspace.projectUI.BuildRuntimeToolbar(); workspace.GetComponent<SpaceCanvasWorkspaceUI>().BuildInterface();
            SpaceCanvasSceneBootstrap.SaveSceneWithStableFonts(workspace.gameObject.scene, ScenePath);
        }

        private static void ConfigureProject()
        {
            var backup = "Library/SpaceCanvas.BeforeMR"; Directory.CreateDirectory(backup);
            foreach (var path in new[] {"Assets/Resources/PXR_ProjectSetting.asset", "Assets/XR/Settings/PXR_Settings.asset", "Assets/XR/XRGeneralSettingsPerBuildTarget.asset", "ProjectSettings/ProjectSettings.asset"})
                if (File.Exists(path) && !File.Exists(Path.Combine(backup, Path.GetFileName(path)))) File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));
            var pxr = PXR_Settings.GetSettings();
            if (pxr == null) throw new InvalidOperationException("未找到 PICO 设置，请先通过 PICO Portal 初始化 SDK。");
            pxr.appMode = PXR_Settings.AppMode.Spatial; EditorUtility.SetDirty(pxr);
            PXR_ProjectSetting.GetProjectConfig().isSpatialAdapter = true;
            PXR_ProjectSetting.SaveAssets();
            var perTarget = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget")
                .Select(g => AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            if (perTarget == null) throw new InvalidOperationException("缺少 XR Plug-in Management 配置。");
            var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (general == null || general.Manager == null) throw new InvalidOperationException("缺少 Android XR Manager。");
            foreach (var loader in general.Manager.activeLoaders.ToArray())
                XRPackageMetadataStore.RemoveLoader(general.Manager, loader.GetType().FullName, BuildTargetGroup.Android);
            if (!XRPackageMetadataStore.AssignLoader(general.Manager, "ByteDance.PICO.XR.PXR_Loader", BuildTargetGroup.Android))
                throw new InvalidOperationException("PICO XR Loader 注册失败。");
            general.Manager.automaticLoading = true; general.Manager.automaticRunning = true; general.InitManagerOnStart = true;
            EditorUtility.SetDirty(general.Manager); EditorUtility.SetDirty(general); EditorUtility.SetDirty(perTarget);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = PXR_Utils.minSdkVersionInEditor;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.spacecanvas.prototype");
            AssetDatabase.SaveAssets(); PXR_Utils.UpdateSDKSymbols();
        }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/构建 MR Android APK")]
#endif
        public static void BuildAndroid()
        {
            try
            {
                if (!File.Exists(ScenePath)) Build();
                Directory.CreateDirectory("Builds");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] {ScenePath}, target = BuildTarget.Android,
                    locationPathName = "Builds/SpaceCanvas-MR.apk", options = BuildOptions.Development });
                File.WriteAllText("Library/SpaceCanvas.AndroidBuild.txt", report.summary.result + "\nErrors=" + report.summary.totalErrors + "\nWarnings=" + report.summary.totalWarnings + "\n" + report.summary.outputPath);
                var diagnostics = new System.Text.StringBuilder();
                foreach (var step in report.steps)
                    foreach (var message in step.messages)
                        if (message.type == LogType.Warning || message.type == LogType.Error || message.type == LogType.Exception)
                            diagnostics.AppendLine(message.type + ": " + message.content);
                File.WriteAllText("Library/SpaceCanvas.AndroidBuild.Diagnostics.txt", diagnostics.ToString());
                // Unity may return Succeeded even if an exporter logged errors. Keep that visible.
                if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors > 0) Debug.LogError("SpaceCanvas MR Android 构建未通过，查看 Library/SpaceCanvas.AndroidBuild.txt 和 Console。");
            }
            catch (Exception e) { File.WriteAllText("Library/SpaceCanvas.AndroidBuild.txt", "FAIL\n" + e); Debug.LogException(e); }
        }
    }
}
