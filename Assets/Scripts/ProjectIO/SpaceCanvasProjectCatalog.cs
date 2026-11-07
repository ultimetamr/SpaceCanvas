using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// Safe project catalog for the runtime import panel and external integrations.
    /// Local files are restricted to Application.persistentDataPath/Projects and
    /// official examples are restricted to the three shipped allow-listed names.
    /// </summary>
    public static class SpaceCanvasProjectCatalog
    {
        private static readonly string[] OfficialNames =
        {
            "livingroom_demo.spacecanvas",
            "booth_demo.spacecanvas",
            "desk_demo.spacecanvas"
        };

        public static string LocalProjectsDirectory => Path.Combine(Application.persistentDataPath, "Projects");

        public static IReadOnlyList<string> OfficialExampleNames => OfficialNames;

        public static string[] GetLocalProjectFiles()
        { return GetLocalProjects().Select(entry => entry.FilePath).ToArray(); }

        /// <summary>返回实际工程保存时间倒序的快照；损坏文件可见但排在末尾且不可导入。</summary>
        public static SpaceCanvasProjectEntry[] GetLocalProjects()
        {
            try
            {
                if (!Directory.Exists(LocalProjectsDirectory)) return new SpaceCanvasProjectEntry[0];
                var root = Path.GetFullPath(LocalProjectsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return ReadEntries(Directory.GetFiles(LocalProjectsDirectory, "*" + SpaceCanvasProjectFormatUtilities.Extension)
                    .Select(Path.GetFullPath)
                    .Where(path => path.StartsWith(root, StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SpaceCanvas 本地工程目录读取失败：" + ex.Message);
                return new SpaceCanvasProjectEntry[0];
            }
        }

        public static SpaceCanvasProjectEntry[] ReadEntries(IEnumerable<string> paths)
        {
            var entries = paths.Select(SpaceCanvasProjectEntry.Read).OrderByDescending(x => x.IsValid)
                .ThenByDescending(x => x.SortTimeMilliseconds).ThenByDescending(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
            if (entries.Length > 0 && entries[0].IsValid) entries[0].IsLatest = true;
            return entries;
        }

        public static bool IsOfficialExample(string fileName)
        {
            var safeName = Path.GetFileName(fileName ?? string.Empty);
            return OfficialNames.Any(name => string.Equals(name, safeName, StringComparison.OrdinalIgnoreCase));
        }

        public static string GetOfficialExampleUri(string fileName)
        {
            if (!IsOfficialExample(fileName)) return string.Empty;
            return Application.streamingAssetsPath.TrimEnd('/', '\\') + "/ExampleProjects/" + Path.GetFileName(fileName);
        }

        public static bool TryReadLocal(string filePath, out SpaceCanvasProject project, out string error)
        {
            project = null;
            error = string.Empty;
            try
            {
                if (!IsInsideLocalDirectory(filePath))
                {
                    error = "工程文件必须位于应用的 Projects 沙盒目录中。";
                    return false;
                }

                project = ProjectSerializer.Deserialize(File.ReadAllText(filePath));
                ProjectValidator.Validate(project);
                return true;
            }
            catch (SpaceCanvasFormatException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = "本地工程读取失败：" + ex.Message;
                return false;
            }
        }

        public static bool IsInsideLocalDirectory(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            try
            {
                var root = Path.GetFullPath(LocalProjectsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var fullPath = Path.GetFullPath(filePath);
                return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetExtension(fullPath), SpaceCanvasProjectFormatUtilities.Extension, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
