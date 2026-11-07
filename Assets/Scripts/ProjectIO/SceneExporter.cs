using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    public static class SceneExporter
    {
        public static bool ExportCurrentScene(string saveName, out string outputPath, out string errorMsg)
        {
            outputPath = string.Empty;
            errorMsg = string.Empty;
            try
            {
                SpaceCanvasProject project;
                if (!TryCaptureCurrentScene(saveName, out project, out errorMsg)) return false;
                project.savedAtUtcMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                project.updateTime = project.savedAtUtcMilliseconds / 1000;
                project.preview = SpaceCanvasProjectThumbnail.Capture(project.updateTime);
                var directory = Path.Combine(Application.persistentDataPath, "Projects");
                Directory.CreateDirectory(directory);
                var baseName = SpaceCanvasProjectFormatUtilities.SanitizeProjectName(
                    string.IsNullOrWhiteSpace(saveName) ? "UntitledProject" : saveName);
                var json = ProjectSerializer.Serialize(project);
                // CreateNew reserves the name atomically. An existence check followed by
                // WriteAllText could overwrite a file created by another caller meanwhile.
                for (var index = 1; index <= 10000; index++)
                {
                    var candidate = Path.Combine(directory, baseName + (index == 1 ? "" : "_" + index) + ".spacecanvas");
                    FileStream stream;
                    try { stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
                    catch (IOException) when (File.Exists(candidate)) { continue; }
                    try
                    {
                        using (stream)
                        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) writer.Write(json);
                    }
                    catch
                    {
                        // Only remove the file this call reserved; never remove existing saves.
                        try { File.Delete(candidate); } catch (Exception) { }
                        throw;
                    }
                    outputPath = candidate;
                    return true;
                }
                errorMsg = "同名工程过多，请换一个工程名。";
                return false;
            }
            catch (Exception ex)
            {
                errorMsg = "导出失败：" + ex.Message;
                return false;
            }
        }

        public static bool TryCaptureCurrentScene(string projectName, out SpaceCanvasProject project, out string errorMsg)
        {
            project = null;
            errorMsg = string.Empty;
            try
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var existingRoot = SpaceCanvasRuntime.CurrentProjectRoot;
                var preservedName = existingRoot == null ? string.Empty : existingRoot.projectName;
                var preservedCreateTime = existingRoot == null ? 0L : existingRoot.createTime;
                project = new SpaceCanvasProject
                {
                    projectName = string.IsNullOrWhiteSpace(projectName)
                        ? (string.IsNullOrWhiteSpace(preservedName) ? "Untitled SpaceCanvas Project" : preservedName)
                        : projectName,
                    createTime = preservedCreateTime > 0 ? preservedCreateTime : now,
                    updateTime = now,
                    objects = new List<SceneObject>(),
                    groups = new List<SceneGroup>()
                };
                var settings = SpaceCanvasRuntime.CurrentProjectRoot == null
                    ? UnityEngine.Object.FindObjectOfType<SpaceCanvasSceneSettings>(true)
                    : SpaceCanvasRuntime.CurrentProjectRoot.GetComponent<SpaceCanvasSceneSettings>();
                if (settings == null) settings = UnityEngine.Object.FindObjectOfType<SpaceCanvasSceneSettings>(true);
                if (settings != null) project.settings = settings.ToData();

                var objects = SpaceCanvasRuntime.GetEditableObjects();
                var ids = new HashSet<string>();
                foreach (var item in objects.OrderBy(x => x == null ? string.Empty : x.name))
                {
                    if (item == null) continue;
                    item.EnsureIdentity();
                    while (!ids.Add(item.ObjectId)) item.ObjectId = Guid.NewGuid().ToString("N");
                    project.objects.Add(item.ToSceneObject());
                }
                foreach (var group in SpaceCanvasRuntime.GetGroups())
                    if (group != null) project.groups.Add(group.ToData());
                project.EnsureDefaults();
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = "读取当前场景失败：" + ex.Message;
                return false;
            }
        }

    }
}
