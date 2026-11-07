#if UNITY_EDITOR
/*
 * SpaceCanvas Editor Importer
 *
 * Usage:
 * 1. Open any Unity scene and choose 导入PICO工程 > 导入工程文件.
 * 2. Connect the headset over USB. Saved projects are read from its app sandbox before selection.
 *    主菜单先读取 PICO 的 Projects，再显示按保存时间排列的版本列表；读取失败时可选择本地文件。
 *    核对文件名、保存时间、物体数量及预览后，点击“确认导入此版本”生成工程根节点。
 *    确认导入使用列表读取时的数据快照；刷新列表可重新读取文件。
 * 3. To replace a placeholder, put a prefab at Resources/SpaceCanvasAssets/{assetId}.prefab,
 *    or change FindPrefab() to your own asset catalog lookup. SpaceCanvasCurated takes priority.
 * 4. Attach your interaction/selection scripts to the generated objects or to the prefab.
 * 5. Extend the JSON format by adding fields to the data classes and apply them in ApplyData().
 *
 * 独立使用：只需将本脚本复制到任意 Unity 工程的 Assets/Editor，不需要 SpaceCanvas/PICO。
 * 本编辑器工具自带只读 DTO，并使用 Unity 内置 JsonUtility 读取开放格式；
 * SpaceCanvas 运行时的序列化/反序列化仍统一使用 UPM Newtonsoft.Json。
 * 替换模型：放入 Assets/Resources/SpaceCanvasAssets/{assetId}.prefab，保留根节点局部变换。
 * 挂载交互：修改 prefab 或导入后给物体添加任意交互脚本。
 * 扩展字段：在 ObjectData 新增字段并在 ApplyData 中处理，缺失字段须保留默认值。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

public static class SpaceCanvasEditorImporter
{
    // 按 Unity 工程隔离目录记录，避免切换工程时打开另一个项目的文件夹。
    private static string ImportDirectoryKey => "SpaceCanvas.ProjectIO.ImportDirectory:" + Application.dataPath;
    // 与 Android 包名及运行时 Application.persistentDataPath/Projects 一致；修改包名时同步修改此路径。
    private const string RemoteProjectsDirectory = "/sdcard/Android/data/com.spacecanvas.prototype/files/Projects";
    private static bool readingDevice;

    /// <summary>
    /// 可选的宿主适配器：创建模型并应用 JSON 参数。返回 null 时使用独立 prefab/占位体路径。
    /// 此脚本本身不引用 SpaceCanvas 运行时；本工程由 SpaceCanvasEditorAssetBridge 接入同一素材库。
    /// </summary>
    public static Func<ObjectData, Transform, GameObject> CreateObjectOverride;

    // 宿主可把“确认导入”接入实际运行工作台；返回 null 表示使用独立布局导入。
    // 只使用通用 DTO/delegate，复制此文件到空 Unity 工程时不依赖业务程序集。
    public static Func<ProjectData, string> ImportProjectOverride;
    public static Func<string> ImportTargetDescription;

    public static string ImportForCurrentScene(ProjectData project)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        Validate(project);
        return ImportProjectOverride?.Invoke(project) ?? ImportIntoScene(project);
    }

    [MenuItem("导入PICO工程/导入工程文件", priority = 10)]
    public static async void ImportProjectFile()
    {
        if (readingDevice) return;
        string directory;
        readingDevice = true;
        try
        {
            EditorUtility.DisplayProgressBar("读取 PICO 工程", "正在读取头显里已保存的工程，请保持 USB 连接……", .25f);
            directory = await DownloadDeviceProjectsAsync();
        }
        catch (Exception ex)
        {
            EditorUtility.ClearProgressBar();
            if (!EditorUtility.DisplayDialog("读取 PICO 工程失败", ex.Message + "\n\n也可以选择电脑上已有的 .spacecanvas 文件。", "选择本地文件", "取消")) return;
            directory = GetImportDirectory();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            readingDevice = false;
        }
        ProjectVersionWindow.OpenDirectory(directory);
    }

    [MenuItem("导入PICO工程/导入工程文件", true)]
    private static bool CanReadDevice() => !readingDevice && !EditorApplication.isPlayingOrWillChangePlaymode;

    public static void ImportLocalProjectFile()
    {
        if (!readingDevice) ImportFromDirectory(GetImportDirectory());
    }

    private static void ImportFromDirectory(string directory)
    {
        var path = EditorUtility.OpenFilePanel("选择 SpaceCanvas 工程", directory, "spacecanvas,json");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            ProjectVersionWindow.OpenFile(path);
        }
        catch (Exception ex) { EditorUtility.DisplayDialog("SpaceCanvas 导入失败", ex.Message, "确定"); }
    }

    /// <summary>
    /// 只读头显沙盒，下载到独立批次目录，不覆盖头显文件或之前的本地副本。
    /// Unity API、进度框和文件框留在主线程，外部进程等待在后台完成。
    /// 本脚本独立复制到其他 Unity 工程时，仍可使用本地导入，无需安装 PICO SDK。
    /// </summary>
    private static async Task<string> DownloadDeviceProjectsAsync()
    {
        var adb = FindAdb();
        var deviceList = await RunAdbAsync(adb, 15000, "devices");
        var online = new List<string>();
        foreach (var line in deviceList.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var columns = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length >= 2 && columns[1] == "device") online.Add(columns[0]);
        }
        if (online.Count == 0)
            throw new IOException("未找到已授权的 PICO。请连接 USB，并在头显中允许电脑进行 USB 调试。");
        if (online.Count != 1)
            throw new IOException("检测到多台 Android 设备。请仅保留需要导入工程的 PICO，或关闭其他模拟器后重试。");

        var serial = online[0];
        await RunAdbAsync(adb, 15000, "-s", serial, "shell", "ls", "-d", RemoteProjectsDirectory);
        // 设备序列号不直接组成文件路径（网络 ADB 的序列号可能包含冒号）。
        var safeSerial = serial;
        foreach (var invalid in Path.GetInvalidFileNameChars()) safeSerial = safeSerial.Replace(invalid, '_');
        var parent = Path.Combine(Path.GetDirectoryName(Application.dataPath), "DeviceProjects", safeSerial);
        Directory.CreateDirectory(parent);
        var batch = Path.Combine(parent, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        await RunAdbAsync(adb, 45000, "-s", serial, "pull", RemoteProjectsDirectory, batch);
        var projects = Directory.GetFiles(batch, "*.spacecanvas", SearchOption.AllDirectories);
        if (projects.Length == 0)
            throw new IOException("头显中还没有已保存的 .spacecanvas 工程。请先在 SpaceCanvas 内点击“保存工程”再重试。");
        Debug.Log("SpaceCanvas：已从 PICO 读取 " + projects.Length + " 个工程。头显路径：" + RemoteProjectsDirectory + "；电脑副本：" + batch);
        return Path.GetDirectoryName(projects[0]);
    }

    private static string FindAdb()
    {
        var executable = Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb";
        var candidates = new List<string>
        {
            Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK/platform-tools", executable)
        };
        foreach (var sdk in new[] { EditorPrefs.GetString("AndroidSdkRoot", ""), Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"), Environment.GetEnvironmentVariable("ANDROID_HOME") })
            if (!string.IsNullOrWhiteSpace(sdk)) candidates.Add(Path.Combine(sdk, "platform-tools", executable));
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            try { candidates.Add(Path.Combine(entry.Trim('"'), executable)); }
            catch (ArgumentException) { /* Ignore malformed PATH entries from other applications. */ }
        }
        foreach (var path in candidates) if (File.Exists(path)) return path;
        throw new FileNotFoundException("未找到 Android ADB。请在 Unity Hub 安装 Android Build Support（含 SDK），或将已有 platform-tools 加入 PATH。");
    }

    private static Task<string> RunAdbAsync(string executable, int timeoutMilliseconds, params string[] arguments)
    {
        return Task.Run(async () =>
        {
            var commandLine = new StringBuilder();
            foreach (var argument in arguments)
            {
                if (commandLine.Length > 0) commandLine.Append(' ');
                commandLine.Append(QuoteProcessArgument(argument));
            }
            using (var process = new System.Diagnostics.Process())
            {
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = executable, Arguments = commandLine.ToString(), UseShellExecute = false,
                    CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    throw new IOException("读取 PICO 超时，请检查 USB 连接后重试。");
                }
                var output = await outputTask;
                var error = await errorTask;
                if (process.ExitCode != 0)
                    throw new IOException("无法读取头显工程，请确认 SpaceCanvas 已保存过工程、设备在线且已授权 USB 调试。\n" + error.Trim() + "\n" + output.Trim());
                return output;
            }
        });
    }

    // 使用进程参数，不经过 shell；正确转义含空格路径及末尾反斜杠。
    private static string QuoteProcessArgument(string argument)
    {
        var quoted = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"') quoted.Append('\\', slashes * 2 + 1);
            else quoted.Append('\\', slashes);
            quoted.Append(character);
            slashes = 0;
        }
        return quoted.Append('\\', slashes * 2).Append('"').ToString();
    }

    /// <summary>优先复用有效的历史目录；首次使用或原目录已删除时打开头显工程拉取目录。</summary>
    private static string GetImportDirectory()
    {
        var previous = EditorPrefs.GetString(ImportDirectoryKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(previous) && Directory.Exists(previous)) return previous;
        try
        {
            var directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "DeviceProjects");
            Directory.CreateDirectory(directory);
            return directory;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("SpaceCanvas：无法打开默认工程目录，将打开 Assets。" + ex.Message);
            return Application.dataPath;
        }
    }

    /// <summary>
    /// 独立编辑器导入快照。哈希来自实际读取字节，时间来自 JSON，避免 USB 复制时间误导版本选择。
    /// 保留于同一个脚本中，第三方复制本文件即可使用；不引用运行时业务程序集。
    /// </summary>
    public sealed class ProjectFileVersion
    {
        public string FilePath { get; private set; }
        public string FileName => Path.GetFileName(FilePath);
        public ProjectData Project { get; private set; }
        public string Error { get; private set; }
        public string VersionId { get; private set; }
        public long SavedTime { get; private set; }
        public long SortTimeMilliseconds { get; private set; }
        public bool UsesFileTime { get; private set; }
        public bool IsValid => Project != null && string.IsNullOrEmpty(Error);
        public string TimeLabel
        {
            get
            {
                try { return DateTimeOffset.FromUnixTimeSeconds(SavedTime).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + (UsesFileTime ? "（文件时间）" : ""); }
                catch (ArgumentOutOfRangeException) { return "时间未知"; }
            }
        }

        public static ProjectFileVersion Read(string path)
        {
            var entry = new ProjectFileVersion { FilePath = path, VersionId = "" };
            try
            {
                var info = new FileInfo(path);
                entry.SavedTime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
                entry.SortTimeMilliseconds = entry.SavedTime * 1000;
                entry.UsesFileTime = true;
                if (info.Length > 16 * 1024 * 1024) throw new IOException("工程文件超过 16 MB。");
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length > 16 * 1024 * 1024) throw new IOException("工程文件超过大小限制。");
                using (var hash = SHA256.Create())
                    entry.VersionId = BitConverter.ToString(hash.ComputeHash(bytes), 0, 4).Replace("-", "").ToLowerInvariant();
                using (var stream = new MemoryStream(bytes))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    entry.Project = Parse(reader.ReadToEnd());
                if (entry.Project.updateTime > 0 && entry.Project.updateTime <= 253402300799L)
                {
                    entry.SavedTime = entry.Project.updateTime;
                    entry.SortTimeMilliseconds = entry.SavedTime * 1000;
                    entry.UsesFileTime = false;
                }
                var precise = entry.Project.savedAtUtcMilliseconds;
                if (!entry.UsesFileTime && precise > 0 && precise / 1000 == entry.SavedTime)
                    entry.SortTimeMilliseconds = precise;
            }
            catch (Exception ex) { entry.Project = null; entry.Error = "工程读取失败：" + ex.Message; }
            return entry;
        }

        public static ProjectFileVersion[] ReadSorted(IEnumerable<string> paths) => paths.Select(Read)
            .OrderByDescending(item => item.IsValid).ThenByDescending(item => item.SortTimeMilliseconds)
            .ThenByDescending(item => item.FileName, StringComparer.OrdinalIgnoreCase).ToArray();

        /// <summary>预览不可信且可选；先限制字节数与 PNG 尺寸，再交给 Unity 解码。</summary>
        public Texture2D DecodePreview()
        {
            var preview = Project?.preview;
            const int maxBytes = 256 * 1024;
            if (preview == null || preview.encoding != "png-base64" || string.IsNullOrEmpty(preview.pngBase64)
                || preview.pngBase64.Length > (maxBytes + 2) / 3 * 4) return null;
            Texture2D texture = null;
            try
            {
                var bytes = Convert.FromBase64String(preview.pngBase64);
                if (bytes.Length < 33 || bytes.Length > maxBytes) return null;
                byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                for (var i = 0; i < signature.Length; i++) if (bytes[i] != signature[i]) return null;
                if (bytes[8] != 0 || bytes[9] != 0 || bytes[10] != 0 || bytes[11] != 13
                    || bytes[12] != 73 || bytes[13] != 72 || bytes[14] != 68 || bytes[15] != 82) return null;
                var width = BigEndian(bytes, 16); var height = BigEndian(bytes, 20);
                if (width == 0 || height == 0 || width > 1024 || height > 1024) return null;
                texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                if (texture.LoadImage(bytes, true)) return texture;
            }
            catch (Exception) { /* 损坏缩略图不能阻止读取工程数据。 */ }
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            return null;
        }
        private static uint BigEndian(byte[] bytes, int i) => ((uint)bytes[i] << 24) | ((uint)bytes[i + 1] << 16) | ((uint)bytes[i + 2] << 8) | bytes[i + 3];
    }

    /// <summary>显式确认版本后才创建场景对象。选择列表项不会自动导入。</summary>
    public sealed class ProjectVersionWindow : EditorWindow
    {
        private ProjectFileVersion[] entries = Array.Empty<ProjectFileVersion>();
        private int selected = -1;
        private Vector2 listScroll, detailScroll;
        private Texture2D preview;
        private string directory;

        public static void OpenDirectory(string path)
        {
            var window = GetWindow<ProjectVersionWindow>("导入 PICO 工程");
            window.minSize = new Vector2(960, 620);
            window.directory = path;
            window.RefreshFiles();
            window.Show();
        }
        public static void OpenFile(string path)
        {
            var window = GetWindow<ProjectVersionWindow>("导入 PICO 工程");
            window.minSize = new Vector2(960, 620);
            window.directory = Path.GetDirectoryName(path);
            window.SetEntries(ProjectFileVersion.ReadSorted(new[] { path }));
            window.Show();
        }
        private void RefreshFiles()
        {
            try { SetEntries(ProjectFileVersion.ReadSorted(Directory.GetFiles(directory, "*.spacecanvas"))); }
            catch (Exception ex) { SetEntries(Array.Empty<ProjectFileVersion>()); ShowNotification(new GUIContent("读取列表失败：" + ex.Message)); }
        }
        private void SetEntries(ProjectFileVersion[] versions)
        {
            entries = versions;
            Select(entries.Length == 0 ? -1 : 0);
        }
        private void Select(int index)
        {
            if (preview != null) DestroyImmediate(preview);
            selected = index;
            preview = index >= 0 ? entries[index].DecodePreview() : null;
            detailScroll = Vector2.zero;
            Repaint();
        }
        private void OnDisable() { if (preview != null) DestroyImmediate(preview); }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            GUILayout.Label("选择要导入的保存版本", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("核对文件名、保存时间与模型预览后再导入。列表按 JSON 保存时间排序；导入使用当前展示的数据快照。", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("选择本地文件", GUILayout.Width(120))) ImportFromDirectory(directory);
                if (GUILayout.Button("刷新此目录", GUILayout.Width(120))) RefreshFiles();
                GUILayout.Label("共 " + entries.Length + " 个文件", EditorStyles.miniLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(330)))
                {
                    listScroll = EditorGUILayout.BeginScrollView(listScroll);
                    for (var i = 0; i < entries.Length; i++)
                    {
                        var entry = entries[i];
                        var label = (i == 0 && entry.IsValid ? "最新 · " : "") + entry.FileName + "\n"
                            + (entry.IsValid ? entry.TimeLabel + " · " + entry.Project.objects.Count + " 个物体" : "文件损坏 · 查看原因");
                        if (GUILayout.Toggle(selected == i, new GUIContent(label, entry.FilePath), "Button", GUILayout.Height(58)) && selected != i) Select(i);
                    }
                    if (entries.Length == 0) EditorGUILayout.HelpBox("此目录没有工程，请选择本地文件。", MessageType.Info);
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                    var entry = selected >= 0 && selected < entries.Length ? entries[selected] : null;
                    if (entry != null)
                    {
                        GUILayout.Label(entry.FileName, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField("电脑副本路径", EditorStyles.miniBoldLabel);
                        EditorGUILayout.SelectableLabel(entry.FilePath, EditorStyles.wordWrappedLabel, GUILayout.Height(52));
                        if (!entry.IsValid) EditorGUILayout.HelpBox(entry.Error, MessageType.Error);
                        else
                        {
                            var project = entry.Project;
                            EditorGUILayout.LabelField("工程名称", project.projectName ?? "未命名");
                            EditorGUILayout.LabelField("保存时间（本地时区）", entry.TimeLabel);
                            EditorGUILayout.LabelField("物体 / 分组", project.objects.Count + " / " + project.groups.Count);
                            EditorGUILayout.LabelField("格式版本 / 文件指纹", project.formatVersion + " / " + entry.VersionId);
                            var rect = GUILayoutUtility.GetRect(320, 220, GUILayout.ExpandWidth(true));
                            if (preview != null) GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
                            else GUI.Box(rect, "此版本暂无预览\n使用新版 SpaceCanvas 重新保存即可生成");
                            if (Version.TryParse(project.formatVersion, out var version) && version > new Version(1, 0))
                                EditorGUILayout.HelpBox("此工程格式高于 1.0，将按已支持字段尽力还原。", MessageType.Warning);
                            EditorGUILayout.HelpBox(ImportTargetDescription?.Invoke()
                                ?? "导入将创建独立工程根节点，并保留当前 Scene 内容。可通过 Unity 撤销移除本次导入。", MessageType.None);
                        }
                    }
                    EditorGUILayout.EndScrollView();
                    using (new EditorGUI.DisabledScope(entry == null || !entry.IsValid))
                    {
                        if (GUILayout.Button("确认导入此版本", GUILayout.Height(38)))
                        {
                            try
                            {
                                var report = ImportForCurrentScene(entry.Project);
                                EditorPrefs.SetString(ImportDirectoryKey, Path.GetDirectoryName(entry.FilePath));
                                Debug.Log(report + "\n来源：" + entry.FilePath + "\n保存时间：" + entry.TimeLabel + "；文件指纹：" + entry.VersionId);
                                Close();
                                GUIUtility.ExitGUI();
                            }
                            catch (ExitGUIException) { throw; }
                            catch (Exception ex) { EditorUtility.DisplayDialog("SpaceCanvas 导入失败", ex.Message, "确定"); }
                        }
                    }
                }
            }
        }
    }

    public static string ImportIntoScene(ProjectData project)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        Validate(project);
        Undo.IncrementCurrentGroup();
        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Import SpaceCanvas Project");
        var root = new GameObject(string.IsNullOrWhiteSpace(project.projectName) ? "SpaceCanvasProject" : project.projectName);
        Undo.RegisterCreatedObjectUndo(root, "Import SpaceCanvas Project");
        var byId = new Dictionary<string, Transform>(StringComparer.Ordinal);
        var pendingParents = new Dictionary<Transform, string>();
        var missingAssets = 0;
        var warnings = new List<string>();
        try
        {
        foreach (var data in project.objects)
        {
            if (data == null) continue;
            // 宿主必须使用和头显相同的“真实模型 > 精细模型 > 自定义 prefab”解析顺序。
            // 先创建模型、应用参数，再建立用户父子关系，避免修改父物体材质时染到子物体。
            GameObject instance = CreateObjectOverride?.Invoke(data, root.transform);
            if (instance != null)
            {
                Undo.RegisterCreatedObjectUndo(instance, "Import SpaceCanvas Object");
            }
            else
            {
                var prefab = FindPrefab(data.assetId);
                if (prefab != null)
                {
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    Undo.RegisterCreatedObjectUndo(instance, "Import SpaceCanvas Object");
                }
                else
                {
                missingAssets++;
                warnings.Add("缺失素材：" + data.assetId);
                instance = GameObject.CreatePrimitive(GuessPrimitive(data.assetId));
                if (data.assetId == "primitive.plane" || data.assetId == "decor.rug")
                {
                    var slab = instance;
                    instance = new GameObject("PlaneRoot");
                    slab.transform.SetParent(instance.transform, false);
                    slab.transform.localScale = new Vector3(1, .025f, 1);
                }
                Undo.RegisterCreatedObjectUndo(instance, "Import SpaceCanvas Placeholder");
                }
                instance.transform.SetParent(root.transform, false);
                ApplyData(instance, data);
            }
            instance.name = (string.IsNullOrWhiteSpace(data.assetId) ? "object" : data.assetId) + " [" + data.id + "]";
            instance.transform.SetParent(root.transform, false);
            PersistMaterials(instance);
            if (!byId.ContainsKey(data.id)) byId.Add(data.id, instance.transform);
            pendingParents[instance.transform] = data.parentId;
        }

        foreach (var pair in pendingParents)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value) && byId.TryGetValue(pair.Value, out var parent))
                pair.Key.SetParent(parent, false);
        }

        foreach (var group in project.groups)
        {
            if (group == null) continue;
            var groupObject = new GameObject(string.IsNullOrWhiteSpace(group.groupName) ? "Group" : group.groupName);
            Undo.RegisterCreatedObjectUndo(groupObject, "Import SpaceCanvas Group");
            groupObject.transform.SetParent(root.transform, false);
            foreach (var childId in group.childIds)
            {
                if (string.IsNullOrWhiteSpace(childId) || byId.ContainsKey(childId)) continue;
                warnings.Add("分组 '" + group.groupName + "' 引用了不存在的物体：" + childId);
            }
        }
        Selection.activeGameObject = root;
        Undo.CollapseUndoOperations(undoGroup);
        return "SpaceCanvas 导入完成：成功 " + project.objects.Count + " 个物体，缺失素材 " + missingAssets + " 个，警告 " + warnings.Count + " 条。\n" + string.Join("\n", warnings);
        }
        catch { Undo.RevertAllDownToGroup(undoGroup); throw; }
    }

    private static GameObject FindPrefab(string assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId)) return null;
        var curated = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/SpaceCanvasCurated/" + assetId + ".prefab");
        if (curated != null) return curated;
        var direct = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/SpaceCanvasAssets/" + assetId + ".prefab");
        if (direct != null) return direct;
        var guids = AssetDatabase.FindAssets(assetId + " t:Prefab");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && string.Equals(prefab.name, assetId, StringComparison.OrdinalIgnoreCase)) return prefab;
        }
        return null;
    }

    private static PrimitiveType GuessPrimitive(string assetId)
    {
        var id = (assetId ?? string.Empty).ToLowerInvariant();
        if (id.Contains("sphere")) return PrimitiveType.Sphere;
        if (id.Contains("cylinder")) return PrimitiveType.Cylinder;
        if (id.Contains("capsule")) return PrimitiveType.Capsule;
        if (id.Contains("plane")) return PrimitiveType.Cube;
        return PrimitiveType.Cube;
    }

    private static void ApplyData(GameObject instance, ObjectData data)
    {
        var t = instance.transform;
        t.localPosition = new Vector3(data.localPosition[0], data.localPosition[1], data.localPosition[2]);
        t.localRotation = new Quaternion(data.localRotation[0], data.localRotation[1], data.localRotation[2], data.localRotation[3]);
        t.localScale = new Vector3(data.localScale[0], data.localScale[1], data.localScale[2]);
        if (!ColorUtility.TryParseHtmlString(data.colorHex, out var color)) color = Color.white;
        color.a = Mathf.Clamp01(data.alpha);
        // 独立脚本也保留 prefab 的每个材质槽、贴图、法线与粗糙度；不能把所有槽位换成同一个白材质。
        var preserveAuthored = false;
        var tintStrength = 1f;
        foreach (var component in instance.GetComponents<Component>())
        {
            if (component == null) continue;
            if (component.GetType().Name == "SpaceCanvasPremiumAssetTag") preserveAuthored = true;
            if (component.GetType().Name != "SpaceCanvasCuratedAsset") continue;
            preserveAuthored = true;
            var property = new SerializedObject(component).FindProperty("tintStrength");
            if (property != null) tintStrength = Mathf.Clamp01(property.floatValue);
        }
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            var shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            var slots = renderer.sharedMaterials;
            if (slots.Length == 0) slots = new Material[1];
            for (var i = 0; i < slots.Length; i++)
            {
            var source = slots[i];
            if (source == null && shader == null) continue;
            var material = source != null ? new Material(source) : new Material(shader);
            var authored = source != null && source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                : source != null && source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            var display = preserveAuthored ? authored * Color.Lerp(Color.white, color, tintStrength) : color;
            display.a = preserveAuthored ? authored.a * color.a : color.a;
            if (material.HasProperty("_Color")) material.SetColor("_Color", display);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", display);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", preserveAuthored
                ? Mathf.Max(material.GetFloat("_Metallic"), Mathf.Clamp01(data.metallic)) : Mathf.Clamp01(data.metallic));
            var transparent = display.a < .999f;
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", transparent ? 3f : 0f);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", transparent ? 1f : 0f);
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", transparent ? (int)UnityEngine.Rendering.BlendMode.SrcAlpha : (int)UnityEngine.Rendering.BlendMode.One);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", transparent ? (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : (int)UnityEngine.Rendering.BlendMode.Zero);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", transparent ? 0 : 1);
            material.DisableKeyword("_ALPHATEST_ON");
            if (transparent)
            {
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.EnableKeyword("_ALPHABLEND_ON");
            }
            else
            {
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHABLEND_ON");
            }
            material.renderQueue = transparent ? TransparentRenderQueue : 2000;
            slots[i] = material;
            }
            renderer.sharedMaterials = slots;
        }
        foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = data.colliderEnabled;
    }

    private static void PersistMaterials(GameObject instance)
    {
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || AssetDatabase.Contains(material)) continue;
                if (!AssetDatabase.IsValidFolder("Assets/SpaceCanvasImportedMaterials"))
                    AssetDatabase.CreateFolder("Assets", "SpaceCanvasImportedMaterials");
                AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath("Assets/SpaceCanvasImportedMaterials/Object.mat"));
            }
        }
    }

    private const int TransparentRenderQueue = 3000;

    [Serializable] public sealed class ProjectData
    {
        public string formatVersion = "1.0";
        public string projectName = "SpaceCanvas Project";
        public long createTime, updateTime, savedAtUtcMilliseconds;
        public PreviewData preview;
        public SettingsData settings = new SettingsData();
        public List<ObjectData> objects = new List<ObjectData>();
        public List<GroupData> groups = new List<GroupData>();
    }
    [Serializable] public sealed class SettingsData
    {
        public int unitSystem;
        public bool gridEnabled = true;
        public float gridSize = .25f;
    }
    [Serializable] public sealed class PreviewData
    {
        public string encoding = "png-base64", pngBase64;
        public long captureTime;
    }
    [Serializable] public sealed class ObjectData
    {
        public string id, assetId = "primitive.cube", parentId;
        public float[] localPosition = { 0, 0, 0 };
        public float[] localRotation = { 0, 0, 0, 1 };
        public float[] localScale = { 1, 1, 1 };
        public string colorHex = "#FFFFFF";
        public float alpha = 1, metallic;
        public bool colliderEnabled = true;
    }
    [Serializable] public sealed class GroupData
    {
        public string groupId, groupName;
        public List<string> childIds = new List<string>();
    }

    public static ProjectData Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new FormatException("工程文件为空。");
        ProjectData project;
        try { project = JsonUtility.FromJson<ProjectData>(json); }
        catch (Exception ex) { throw new FormatException("工程 JSON 格式损坏：" + ex.Message, ex); }
        if (project == null) throw new FormatException("工程 JSON 根节点为空。");
        Validate(project);
        return project;
    }

    private static void Validate(ProjectData project)
    {
        if (project.objects == null) project.objects = new List<ObjectData>();
        if (project.groups == null) project.groups = new List<GroupData>();
        if (project.objects.Count > 10000) throw new FormatException("物体数量超过限制。");
        var parents = new Dictionary<string, string>();
        for (var i = 0; i < project.objects.Count; i++)
        {
            var item = project.objects[i];
            if (item == null) throw new FormatException("物体数据为空。");
            if (string.IsNullOrWhiteSpace(item.id)) item.id = "object-" + i;
            if (parents.ContainsKey(item.id)) throw new FormatException("物体 ID 重复：" + item.id);
            parents.Add(item.id, item.parentId);
            if (item.localPosition == null || item.localPosition.Length != 3) item.localPosition = new float[] { 0, 0, 0 };
            if (item.localRotation == null || item.localRotation.Length != 4) item.localRotation = new float[] { 0, 0, 0, 1 };
            if (item.localScale == null || item.localScale.Length != 3) item.localScale = new float[] { 1, 1, 1 };
            foreach (var vector in new[] { item.localPosition, item.localRotation, item.localScale })
                foreach (var value in vector) if (float.IsNaN(value) || float.IsInfinity(value)) throw new FormatException("物体参数含非法数字：" + item.id);
        }
        foreach (var id in parents.Keys)
        {
            var path = new HashSet<string>(); var current = id;
            while (!string.IsNullOrEmpty(current) && parents.TryGetValue(current, out var next))
            { if (!path.Add(current)) throw new FormatException("父子层级存在循环：" + current); current = next; }
        }
        foreach (var group in project.groups) if (group != null && group.childIds == null) group.childIds = new List<string>();
    }
}
#endif
