using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceCanvas.ProjectIO.Editor
{
    /// <summary>首次成功编译后生成真正可见、可保存、可运行的启动场景；保留原 SampleScene。</summary>
    [InitializeOnLoad]
    public static class SpaceCanvasSceneBootstrap
    {
        public const string ScenePath = "Assets/Scenes/SpaceCanvas_Start.unity";
        private static double retryAt;
        static SpaceCanvasSceneBootstrap() { EditorApplication.update += FirstSetup; }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("SpaceCanvas/测试/导出当前场景")]
#endif
#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/测试/导出当前场景")]
#endif
        public static void ExportTest()
        {
            if (SceneExporter.ExportCurrentScene("EditorExport", out var path, out var error)) Debug.Log("SpaceCanvas 导出成功：" + path);
            else Debug.LogError(error);
        }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/刷新并保存预览界面")]
#endif
        public static void RefreshPreview()
        {
            if (EditorApplication.isPlaying) return;
            SpaceCanvasFontBuilder.EnsureFont();
            var workspace = UnityEngine.Object.FindObjectOfType<SpaceCanvasWorkspace>();
            if (workspace == null) return;
            SpaceCanvasRuntime.ContentRoot = workspace.contentRoot;
            SpaceCanvasRuntime.UpgradeBuiltinAssets();
            var root = workspace.contentRoot.GetComponentInChildren<SpaceCanvasProjectRoot>(true);
            SpaceCanvasRuntime.SetCurrentProjectRoot(root);
            workspace.projectUI.BuildRuntimeToolbar();
            workspace.GetComponent<SpaceCanvasWorkspaceUI>().BuildInterface();
            EditorSceneManager.MarkSceneDirty(workspace.gameObject.scene);
            SaveSceneWithStableFonts(workspace.gameObject.scene, workspace.gameObject.scene.path);
        }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/素材/升级当前场景为高质量模型")]
#endif
        public static void UpgradeCurrentSceneAssets()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("请先退出播放模式。"); return; }
            var workspace = UnityEngine.Object.FindObjectOfType<SpaceCanvasWorkspace>();
            if (workspace == null || workspace.contentRoot == null)
            {
                Debug.LogWarning("当前场景没有 SpaceCanvas Workspace。");
                return;
            }
            SpaceCanvasRuntime.ContentRoot = workspace.contentRoot;
            var count = SpaceCanvasRuntime.UpgradeBuiltinAssets();
            PersistSceneMaterials();
            EditorSceneManager.MarkSceneDirty(workspace.gameObject.scene);
            SaveSceneWithStableFonts(workspace.gameObject.scene, workspace.gameObject.scene.path);
            AssetDatabase.SaveAssets();
            Debug.Log("SpaceCanvas：高质量素材升级完成，数量=" + count);
        }

        private static void FirstSetup()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < retryAt) return;
            if (File.Exists(ScenePath)) { EditorApplication.update -= FirstSetup; return; }
            // 用户已修改的场景不自动切走；仍可通过菜单明确执行搭建。
            if (SceneManager.GetActiveScene().isDirty) return;
            try { Build(); EditorApplication.update -= FirstSetup; }
            catch (Exception ex) { retryAt = EditorApplication.timeSinceStartup + 30; Debug.LogException(ex); }
        }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/搭建并打开启动场景")]
#endif
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("请先退出播放模式。"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            Directory.CreateDirectory("Assets/Scenes");
            SpaceCanvasFontBuilder.EnsureFont();
            EnsureBuiltinPrefabs();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(6.5f, 5.2f, -8.5f);
            camera.transform.LookAt(new Vector3(0, .4f, 0));
            camera.backgroundColor = new Color(.09f, .14f, .2f); camera.clearFlags = CameraClearFlags.SolidColor; camera.fieldOfView = 48;
            camera.nearClipPlane = .05f; camera.farClipPlane = 100;
            var light = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.4f; light.transform.rotation = Quaternion.Euler(48, -35, 0);
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientLight = new Color(.55f, .61f, .7f);
            var manager = new GameObject("SpaceCanvas Workspace");
            var workspace = manager.AddComponent<SpaceCanvasWorkspace>(); workspace.viewCamera = camera;
            workspace.contentRoot = new GameObject("Editable Content").transform; workspace.contentRoot.SetParent(manager.transform, false);
            SpaceCanvasRuntime.ContentRoot = workspace.contentRoot;
            var example = ProjectSerializer.Deserialize(File.ReadAllText("Assets/StreamingAssets/ExampleProjects/livingroom_demo.spacecanvas"));
            if (!SceneImporter.ImportProject(example, out var error, false)) throw new InvalidOperationException(error);
            // 预览材质保存为资源，防止临时 material 在重开场景后丢失。
            PersistSceneMaterials();
            var projectUI = manager.AddComponent<SpaceCanvasProjectIOUI>();
            projectUI.createSwanWindowHost = false; projectUI.createRuntimeToolbar = true;
            workspace.projectUI = projectUI;
            projectUI.BuildRuntimeToolbar();
            var ui = manager.AddComponent<SpaceCanvasWorkspaceUI>(); ui.BuildInterface();
            SaveSceneWithStableFonts(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }
                .Concat(EditorBuildSettings.scenes.Where(s => s.path != ScenePath)).ToArray();
            AssetDatabase.SaveAssets();
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.LookAt(new Vector3(0, .5f, 0), Quaternion.Euler(30, -35, 0), 7);
            Debug.Log("SpaceCanvas 启动场景已生成：" + ScenePath + "。含客厅示例、36 个素材、属性面板、保存/导入/关于开源。点击 Play 交互。此场景是 Unity 桌面编辑预览，尚未验证为 Swan 多原生窗口。");
        }

        private static void EnsureBuiltinPrefabs()
        {
            Directory.CreateDirectory("Assets/Resources/SpaceCanvasAssets");
            Directory.CreateDirectory("Assets/Resources/SpaceCanvasMaterials");
            const string materialPath = "Assets/Resources/SpaceCanvasMaterials/DefaultPBR.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null) throw new InvalidOperationException("未找到 PBR Shader。");
                material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath);
            }
            foreach (var id in SpaceCanvasBuiltinAssets.Ids)
            {
                var path = "Assets/Resources/SpaceCanvasAssets/" + id + ".prefab";
                if (File.Exists(path)) continue;
                var go = SpaceCanvasBuiltinAssets.Create(id, null, material);
                PrefabUtility.SaveAsPrefabAsset(go, path); UnityEngine.Object.DestroyImmediate(go);
            }
            AssetDatabase.SaveAssets();
        }

        public static void SaveSceneWithStableFonts(Scene scene, string path)
        {
            // Unity 2022 uGUI 在保存时重建动态中文字库可能修改正在枚举的 FontUpdateTracker。
            // 暂时注销回调追踪，不改变 Text 的字体引用或场景内容，保存完成后恢复。
            var texts = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>(true);
            foreach (var text in texts) UnityEngine.UI.FontUpdateTracker.UntrackText(text);
            try { EditorSceneManager.SaveScene(scene, path); }
            finally
            {
                foreach (var text in texts)
                    if (text != null && text.isActiveAndEnabled) UnityEngine.UI.FontUpdateTracker.TrackText(text);
            }
        }

        private static void PersistSceneMaterials()
        {
            foreach (var item in SpaceCanvasRuntime.GetEditableObjects())
            {
                var index = 0;
                foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (var i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null || AssetDatabase.Contains(materials[i])) continue;
                        var path = "Assets/Resources/SpaceCanvasMaterials/" + item.ObjectId + "_" + index++ + ".mat";
                        path = AssetDatabase.GenerateUniqueAssetPath(path); AssetDatabase.CreateAsset(materials[i], path);
                    }
                    renderer.sharedMaterials = materials;
                }
            }
        }
    }
}
