using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    public static class SceneImporter
    {
        public static bool ImportScene(string filePath, out string errorMsg)
        {
            errorMsg = string.Empty;
            if (string.IsNullOrWhiteSpace(filePath)) { errorMsg = "未指定工程文件路径。"; return false; }
            try
            {
                if (!File.Exists(filePath)) { errorMsg = "工程文件不存在：" + filePath; return false; }
                var project = ProjectSerializer.Deserialize(File.ReadAllText(filePath));
                return ImportProject(project, out errorMsg, true);
            }
            catch (SpaceCanvasFormatException ex)
            {
                errorMsg = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                errorMsg = "导入失败：" + ex.Message;
                return false;
            }
        }

        public static bool ImportProject(SpaceCanvasProject project, out string errorMsg, bool recordUndo)
        {
            errorMsg = string.Empty;
            if (project == null) { errorMsg = "工程数据为空。"; return false; }
            GameObject stagedRoot = null;
            try
            {
                ProjectValidator.Validate(project);
                if (IsVersionHigher(project.formatVersion))
                    Debug.LogWarning("SpaceCanvas 工程版本 " + project.formatVersion + " 高于当前支持版本 1.0，将尽力兼容。");

                SpaceCanvasProject before = null;
                if (recordUndo && !SceneExporter.TryCaptureCurrentScene(null, out before, out var captureError))
                    throw new InvalidOperationException(captureError);

                // 先在暂存根下完整构建。任何素材或数据异常只清理暂存，不破坏原场景。
                var root = new GameObject(string.IsNullOrWhiteSpace(project.projectName) ? "SpaceCanvasProject" : project.projectName);
                stagedRoot = root;
                root.SetActive(false);
                var rootMarker = root.AddComponent<SpaceCanvasProjectRoot>();
                rootMarker.projectName = project.projectName;
                rootMarker.createTime = project.createTime;
                var settings = root.AddComponent<SpaceCanvasSceneSettings>();
                settings.Apply(project.settings);

                var byId = new Dictionary<string, SpaceCanvasEditableObject>(StringComparer.Ordinal);
                var pendingParents = new Dictionary<SpaceCanvasEditableObject, string>();
                var missingAssets = 0;
                foreach (var data in project.objects)
                {
                    if (data == null) continue;
                    var instance = SpaceCanvasAssetLibrary.CreateInstance(data.assetId, root.transform, out var missing);
                    if (missing)
                    {
                        missingAssets++;
                        Debug.LogWarning("SpaceCanvas 未找到素材 assetId='" + data.assetId + "'，已使用默认立方体替代。");
                    }
                    instance.name = string.IsNullOrWhiteSpace(data.assetId) ? data.id : data.assetId;
                    var editable = instance.GetComponent<SpaceCanvasEditableObject>();
                    editable.Apply(data);
                    if (!byId.ContainsKey(data.id)) byId.Add(data.id, editable);
                    pendingParents[editable] = data.parentId;
                }
                foreach (var pair in pendingParents)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Value) && byId.TryGetValue(pair.Value, out var parent))
                        pair.Key.transform.SetParent(parent.transform, false);
                    else pair.Key.transform.SetParent(root.transform, false);
                }
                foreach (var groupData in project.groups)
                {
                    if (groupData == null) continue;
                    var groupObject = new GameObject(string.IsNullOrWhiteSpace(groupData.groupName) ? "Group" : groupData.groupName);
                    groupObject.transform.SetParent(root.transform, false);
                    var group = groupObject.AddComponent<SpaceCanvasGroup>();
                    group.Apply(groupData);
                    foreach (var childId in groupData.childIds)
                    {
                        if (string.IsNullOrWhiteSpace(childId) || byId.ContainsKey(childId)) continue;
                        Debug.LogWarning("SpaceCanvas 分组 '" + groupData.groupName + "' 引用了不存在的物体 id='" + childId + "'。");
                    }
                }
                // 暂存根尚未注册到 ContentRoot。无容器的旧项目路径则临时禁用标记，避免清理误删它。
                if (SpaceCanvasRuntime.ContentRoot == null)
                {
                    foreach (var existing in UnityEngine.Object.FindObjectsOfType<SpaceCanvasProjectRoot>(true))
                    {
                        if (existing == rootMarker) continue;
                        DestroyRoot(existing.gameObject);
                    }
                    foreach (var existing in UnityEngine.Object.FindObjectsOfType<SpaceCanvasEditableObject>(true))
                        if (existing != null && !existing.transform.IsChildOf(root.transform)) DestroyRoot(existing.gameObject);
                    foreach (var existing in UnityEngine.Object.FindObjectsOfType<SpaceCanvasGroup>(true))
                        if (existing != null && !existing.transform.IsChildOf(root.transform)) DestroyRoot(existing.gameObject);
                }
                else SpaceCanvasRuntime.ClearEditableScene();
                if (before != null) SpaceCanvasUndoService.PushSnapshot(before);
                root.transform.SetParent(SpaceCanvasRuntime.ContentRoot, false);
                SpaceCanvasRuntime.SetCurrentProjectRoot(rootMarker);
                root.SetActive(true);
                stagedRoot = null;
                if (missingAssets > 0) errorMsg = "导入完成，但有 " + missingAssets + " 个素材缺失，已使用默认立方体。";
                if (IsVersionHigher(project.formatVersion)) errorMsg += " 工程版本较新，已尽力读取受支持字段。";
                return true;
            }
            catch (Exception ex)
            {
                if (stagedRoot != null) DestroyRoot(stagedRoot);
                errorMsg = "导入失败：" + ex.Message;
                return false;
            }
        }

        private static void DestroyRoot(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false); go.transform.SetParent(null, false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(go); else UnityEngine.Object.DestroyImmediate(go);
        }

        private static bool IsVersionHigher(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return false;
            if (!Version.TryParse(version, out var parsed)) return false;
            return parsed > new Version(1, 0);
        }
    }
}
