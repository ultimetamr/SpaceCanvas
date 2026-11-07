using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Editor
{
    /// <summary>仅供本地验证的 Console 快照，便于多个 Unity 进程并行时定位本工程错误。</summary>
    [InitializeOnLoad]
    internal static class SpaceCanvasConsoleDiagnostics
    {
        static SpaceCanvasConsoleDiagnostics() { EditorApplication.update += Poll; }
        private static void Poll()
        {
            const string request = "Library/SpaceCanvas.Console.request";
            if (!File.Exists(request) || EditorApplication.isCompiling) return;
            File.Move(request, request + "." + DateTime.UtcNow.Ticks + ".started");
            try { Dump(); } catch (Exception e) { File.WriteAllText("Library/SpaceCanvas.Console.txt", e.ToString()); }
        }
#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/测试/保存当前 Console 诊断")]
#endif
        public static void Dump()
        {
            var assembly = typeof(EditorApplication).Assembly;
            var entries = assembly.GetType("UnityEditor.LogEntries");
            var entryType = assembly.GetType("UnityEditor.LogEntry");
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var result = new StringBuilder(DateTime.UtcNow.ToString("O") + "\n");
            result.AppendLine("EditorLog=" + Application.consoleLogPath);
            entries.GetMethod("StartGettingEntries", flags).Invoke(null, null);
            try
            {
                var count = (int)entries.GetMethod("GetCount", flags).Invoke(null, null);
                var method = entries.GetMethod("GetEntryInternal", flags);
                for (var i = 0; i < count; i++)
                {
                    var entry = Activator.CreateInstance(entryType);
                    method.Invoke(null, new[] {(object)i, entry});
                    result.AppendLine((string)entryType.GetField("message").GetValue(entry));
                }
            }
            finally { entries.GetMethod("EndGettingEntries", flags).Invoke(null, null); }
            File.WriteAllText("Library/SpaceCanvas.Console.txt", result.ToString());
        }
    }
}
