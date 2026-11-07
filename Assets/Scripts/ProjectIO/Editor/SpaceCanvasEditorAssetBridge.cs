using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SpaceCanvas.ProjectIO.Editor
{
    /// <summary>
    /// 把独立编辑器导入器接到运行时同一素材库和外观实现，避免两条导入路径随模型升级而分叉。
    /// 只有本适配文件依赖 SpaceCanvas 运行时；第三方只复制独立导入器时使用其 prefab/占位体逻辑。
    /// </summary>
    [InitializeOnLoad]
    public static class SpaceCanvasEditorAssetBridge
    {
        static SpaceCanvasEditorAssetBridge()
        {
            SpaceCanvasEditorImporter.CreateObjectOverride = Create;
            SpaceCanvasEditorImporter.ImportProjectOverride = ImportToWorkspace;
            SpaceCanvasEditorImporter.ImportTargetDescription = DescribeTarget;
            EditorApplication.update += PollImportRequest;
            Undo.undoRedoPerformed += RestoreWorkspaceRegistry;
        }

        // Unity Undo 会重建对象，静态引用不会随 Scene 一起还原；同步工程根，
        // 让撤销后的再次导出仍使用原工程名称、创建时间和场景设置。
        private static void RestoreWorkspaceRegistry()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;
            var workspaces = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<SpaceCanvasWorkspace>(true)).ToArray();
            if (workspaces.Length != 1 || workspaces[0].contentRoot == null) return;
            SpaceCanvasRuntime.ContentRoot = workspaces[0].contentRoot;
            SpaceCanvasRuntime.SetCurrentProjectRoot(workspaces[0].contentRoot.GetComponentInChildren<SpaceCanvasProjectRoot>(true));
        }

        // 与构建/测试请求一致的本地自动化入口，便于对具体头显文件复现整条链路。
        private static void PollImportRequest()
        {
            const string request = "Library/SpaceCanvas.ImportWorkspace.request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
            var path = File.ReadAllText(request).Trim();
            File.Move(request, request + "." + DateTime.UtcNow.Ticks + ".started");
            try
            {
                var report = ImportToWorkspace(SpaceCanvasEditorImporter.Parse(File.ReadAllText(path)));
                if (report == null) throw new InvalidOperationException("当前场景没有工作台。");
                var scene = SceneManager.GetActiveScene();
                if (string.IsNullOrWhiteSpace(scene.path)) throw new InvalidOperationException("请先保存工作台场景再运行自动导入。");
                SaveWorkspaceSceneForBuild(scene.path);
                if (!SceneExporter.TryCaptureCurrentScene(null, out var captured, out var error)) throw new InvalidOperationException(error);
                File.WriteAllText("Library/SpaceCanvas.WorkspaceImport.Captured.spacecanvas", ProjectSerializer.Serialize(captured));
                File.WriteAllText("Library/SpaceCanvas.WorkspaceImport.txt", "PASS\n" + report + "\nSource=" + path);
                Debug.Log(report);
            }
            catch (Exception ex)
            {
                File.WriteAllText("Library/SpaceCanvas.WorkspaceImport.txt", "FAIL\n" + ex);
                Debug.LogException(ex);
            }
        }

        private static SpaceCanvasWorkspace FindWorkspace()
        {
            var scene = SceneManager.GetActiveScene();
            var workspaces = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<SpaceCanvasWorkspace>(true)).ToArray();
            if (workspaces.Length > 1) throw new InvalidOperationException("当前场景包含多个工作台，请只保留一个导入目标。");
            return workspaces.SingleOrDefault();
        }

        private static string DescribeTarget()
        {
            return FindWorkspace() == null ? null
                : "导入将替换当前 MR 工作台的可编辑内容，保留相机、UI 和工作台布局。物体按工程局部坐标还原，可整体撤销。导入后保存当前场景再打包。";
        }

        /// <summary>
        /// 主工程使用与头显相同的场景导入器，写入实际 ContentRoot。
        /// 先在隔离容器完整构建，再以单个 Unity Undo 操作提交；独立脚本路径保持可移植。
        /// 工作台的米制坐标/缩放是展示基准，不乘入对象 JSON，也不在导入时重新包围盒适配。
        /// </summary>
        public static string ImportToWorkspace(SpaceCanvasEditorImporter.ProjectData data)
        {
            var workspace = FindWorkspace();
            if (workspace == null) return null;
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出播放模式后导入。");
            if (workspace.contentRoot == null) throw new InvalidOperationException("当前工作台缺少 ContentRoot，不能导入。");
            var project = ProjectSerializer.Deserialize(JsonUtility.ToJson(data));
            ProjectValidator.Validate(project);
            var previousContent = SpaceCanvasRuntime.ContentRoot;
            var previousProject = SpaceCanvasRuntime.CurrentProjectRoot;
            var staging = new GameObject("SpaceCanvas Import Staging");
            staging.SetActive(false);
            SceneManager.MoveGameObjectToScene(staging, workspace.gameObject.scene);
            GameObject imported = null;
            var undoGroup = -1;
            var committed = false;
            try
            {
                SpaceCanvasRuntime.ContentRoot = staging.transform;
                SpaceCanvasRuntime.SetCurrentProjectRoot(null);
                if (!SceneImporter.ImportProject(project, out var warning, false)) throw new InvalidOperationException(warning);
                imported = SpaceCanvasRuntime.CurrentProjectRoot.gameObject;
                // 材质成为持久资源，脚本与对象 ID 保留供播放、抓取及再次保存使用。
                foreach (var renderer in imported.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || AssetDatabase.Contains(material)) continue;
                        if (!AssetDatabase.IsValidFolder("Assets/SpaceCanvasImportedMaterials")) AssetDatabase.CreateFolder("Assets", "SpaceCanvasImportedMaterials");
                        AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath("Assets/SpaceCanvasImportedMaterials/Workspace.mat"));
                    }
                var oldContent = workspace.contentRoot.GetComponentsInChildren<SpaceCanvasProjectRoot>(true).Select(item => item.gameObject)
                    .Concat(workspace.contentRoot.GetComponentsInChildren<SpaceCanvasEditableObject>(true).Select(item => item.gameObject))
                    .Concat(workspace.contentRoot.GetComponentsInChildren<SpaceCanvasGroup>(true).Select(item => item.gameObject))
                    .Where(go => go != workspace.contentRoot.gameObject).Distinct().ToArray();
                var topLevel = oldContent.Where(go => !oldContent.Any(other => other != go && go.transform.IsChildOf(other.transform))).ToArray();
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("导入 PICO 工程到 MR 工作台");
                imported.transform.SetParent(workspace.contentRoot, false);
                // 项目根始终是单位变换。工作台自身的位姿和展示缩放完整保留。
                imported.transform.localPosition = Vector3.zero;
                imported.transform.localRotation = Quaternion.identity;
                imported.transform.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(imported, "导入 PICO 工程到 MR 工作台");
                foreach (var old in topLevel) Undo.DestroyObjectImmediate(old);
                SpaceCanvasRuntime.ContentRoot = workspace.contentRoot;
                SpaceCanvasRuntime.SetCurrentProjectRoot(imported.GetComponent<SpaceCanvasProjectRoot>());
                Selection.activeGameObject = imported;
                EditorSceneManager.MarkSceneDirty(workspace.gameObject.scene);
                Undo.CollapseUndoOperations(undoGroup);
                committed = true;
                return "SpaceCanvas 已导入 MR 工作台：" + project.objects.Count + " 个物体，保持全部局部位置/旋转/缩放。\n目标场景："
                    + workspace.gameObject.scene.path + "\n请保存场景后打包。" + (string.IsNullOrWhiteSpace(warning) ? "" : "\n" + warning);
            }
            catch
            {
                if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
                else if (imported != null && !imported.transform.IsChildOf(staging.transform)) Object.DestroyImmediate(imported);
                throw;
            }
            finally
            {
                if (staging != null) Object.DestroyImmediate(staging);
                if (!committed)
                {
                    SpaceCanvasRuntime.ContentRoot = previousContent;
                    SpaceCanvasRuntime.SetCurrentProjectRoot(previousProject);
                }
            }
        }

        /// <summary>构建读取磁盘 Scene；把当前目标场景的修改先保存，避免打进上一版布局。</summary>
        public static void SaveWorkspaceSceneForBuild(string path)
        {
            var active = SceneManager.GetActiveScene();
            if (active.path != path && active.IsValid() && active.GetRootGameObjects().Any(go => go.GetComponentInChildren<SpaceCanvasWorkspace>(true) != null))
                throw new InvalidOperationException("当前编辑的工作台场景为 " + active.path + "，构建目标却是 " + path + "。请先打开构建目标场景并在其中导入工程。");
            var scene = SceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded || !scene.isDirty) return;
            SpaceCanvasSceneBootstrap.SaveSceneWithStableFonts(scene, path);
            if (scene.isDirty) throw new InvalidOperationException("场景保存未完成，已停止构建以防打入旧布局：" + path);
            AssetDatabase.SaveAssets();
        }

        public static GameObject Create(SpaceCanvasEditorImporter.ObjectData data, Transform parent)
        {
            // 未注册 ID 交给独立导入器，以保留其缺失素材统计与占位提示。
            if (!SpaceCanvasBuiltinAssets.IsKnown(data.assetId)
                && Resources.Load<GameObject>("SpaceCanvasCurated/" + data.assetId) == null
                && Resources.Load<GameObject>("SpaceCanvasAssets/" + data.assetId) == null
                && Resources.Load<GameObject>(data.assetId) == null) return null;
            var instance = SpaceCanvasAssetLibrary.CreateInstance(data.assetId, parent, out _);
            try
            {
                var editable = instance.GetComponent<SpaceCanvasEditableObject>();
                editable.Apply(new SceneObject
                {
                    id = data.id, assetId = data.assetId, parentId = data.parentId,
                    localPosition = data.localPosition, localRotation = data.localRotation, localScale = data.localScale,
                    colorHex = data.colorHex, alpha = data.alpha, metallic = data.metallic, colliderEnabled = data.colliderEnabled
                });
                // 独立 Scene 还原结果保存最终材质，不注册到 MR 工作台。
                // 移除临时参数桥，避免进播放或重新打开场景后 Awake 对最终材质再次叠加染色。
                Object.DestroyImmediate(editable);
                return instance;
            }
            catch
            {
                Object.DestroyImmediate(instance);
                throw;
            }
        }
    }
}
