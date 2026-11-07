using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// 一次读取的工程版本。列表、确认卡和实际导入共享这份数据，避免确认后读到另一版本。
    /// JSON 保存时间优先于文件修改时间：ADB 复制会改变电脑副本时间。
    /// </summary>
    public sealed class SpaceCanvasProjectEntry
    {
        public const long MaxFileBytes = 16 * 1024 * 1024;
        public string FilePath { get; private set; }
        public string FileName => Path.GetFileName(FilePath);
        public SpaceCanvasProject Project { get; private set; }
        public string Error { get; private set; }
        public string VersionId { get; private set; }
        public long SavedTime { get; private set; }
        public long SortTimeMilliseconds { get; private set; }
        public bool UsesFileTime { get; private set; }
        public bool IsLatest { get; internal set; }
        public bool IsValid => Project != null && string.IsNullOrEmpty(Error);
        public string TimeLabel => FormatTime(SavedTime) + (UsesFileTime ? "（文件时间）" : "");
        public string Detail => IsValid
            ? TimeLabel + "  ·  " + Project.objects.Count + " 个物体  ·  " + VersionId
            : "无法读取此版本，请检查文件";

        public static SpaceCanvasProjectEntry Read(string path)
        {
            var entry = new SpaceCanvasProjectEntry { FilePath = path, Error = string.Empty, VersionId = "" };
            try
            {
                var info = new FileInfo(path);
                entry.SavedTime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
                entry.SortTimeMilliseconds = entry.SavedTime * 1000;
                entry.UsesFileTime = true;
                if (info.Length > MaxFileBytes) throw new IOException("工程文件超过 16 MB，请检查文件大小。");
                var bytes = File.ReadAllBytes(path);
                if (bytes.LongLength > MaxFileBytes) throw new IOException("工程文件超过大小限制。");
                using (var hash = SHA256.Create())
                    entry.VersionId = BitConverter.ToString(hash.ComputeHash(bytes), 0, 4).Replace("-", "").ToLowerInvariant();
                using (var stream = new MemoryStream(bytes))
                using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true))
                    entry.Project = ProjectSerializer.Deserialize(reader.ReadToEnd());
                ProjectValidator.Validate(entry.Project);
                if (IsValidTime(entry.Project.updateTime))
                { entry.SavedTime = entry.Project.updateTime; entry.SortTimeMilliseconds = entry.SavedTime * 1000; entry.UsesFileTime = false; }
                var precise = entry.Project.savedAtUtcMilliseconds;
                if (precise > 0 && IsValidTime(precise / 1000) && precise / 1000 == entry.Project.updateTime)
                    entry.SortTimeMilliseconds = precise;
            }
            catch (Exception ex) { entry.Project = null; entry.Error = "工程读取失败：" + ex.Message; }
            return entry;
        }

        public static bool IsValidTime(long seconds) => seconds > 0 && seconds <= 253402300799L;

        public static string FormatTime(long seconds)
        {
            if (!IsValidTime(seconds)) return "时间未知";
            try { return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"); }
            catch (ArgumentOutOfRangeException) { return "时间未知"; }
        }
    }
}
