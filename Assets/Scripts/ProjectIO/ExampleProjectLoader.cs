using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace SpaceCanvas.ProjectIO
{
    public static class ExampleProjectLoader
    {
        public static string[] GetLocalProjectFiles()
        {
            return SpaceCanvasProjectCatalog.GetLocalProjectFiles();
        }

        public static string[] GetOfficialExampleNames()
        {
            var names = SpaceCanvasProjectCatalog.OfficialExampleNames;
            var result = new string[names.Count];
            for (var i = 0; i < names.Count; i++) result[i] = names[i];
            return result;
        }

        public static IEnumerator LoadOfficialExample(string fileName, Action<bool, string> completed)
        {
            if (!SpaceCanvasProjectCatalog.IsOfficialExample(fileName))
            {
                completed?.Invoke(false, "未知的官方示例工程。请从内置示例列表选择。");
                yield break;
            }

            var safeName = Path.GetFileName(fileName);
            var uri = SpaceCanvasProjectCatalog.GetOfficialExampleUri(safeName);
            using (var request = UnityWebRequest.Get(uri))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    completed?.Invoke(false, "示例工程读取失败：" + request.error);
                    yield break;
                }
                try
                {
                    var project = ProjectSerializer.Deserialize(request.downloadHandler.text);
                    var ok = SceneImporter.ImportProject(project, out var error, true);
                    completed?.Invoke(ok, error);
                }
                catch (Exception ex) { completed?.Invoke(false, "示例工程解析失败：" + ex.Message); }
            }
        }
    }
}
