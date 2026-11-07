using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// Open-format helpers shared by runtime import/export, editor validation and
    /// third-party tools.  Keeping these helpers in the runtime assembly makes the
    /// .spacecanvas format independently reusable in a blank Unity project.
    /// </summary>
    public static class SpaceCanvasProjectFormatUtilities
    {
        public const string CurrentFormatVersion = "1.0";
        public const string Extension = ".spacecanvas";

        /// <summary>Compares scene parameters while optionally ignoring timestamps.</summary>
        public static bool AreEquivalent(SpaceCanvasProject left, SpaceCanvasProject right,
            bool ignoreTimestamps, out string difference)
        {
            difference = string.Empty;
            if (left == null || right == null)
            {
                difference = "工程数据为空。";
                return left == right;
            }

            try
            {
                var leftToken = JObject.Parse(ProjectSerializer.Serialize(left));
                var rightToken = JObject.Parse(ProjectSerializer.Serialize(right));
                if (ignoreTimestamps)
                {
                    leftToken.Remove("createTime");
                    leftToken.Remove("updateTime");
                    leftToken.Remove("savedAtUtcMilliseconds");
                    leftToken.Remove("preview"); // Generated save metadata does not represent an edit to the layout.
                    rightToken.Remove("createTime");
                    rightToken.Remove("updateTime");
                    rightToken.Remove("savedAtUtcMilliseconds");
                    rightToken.Remove("preview");
                }

                if (JToken.DeepEquals(leftToken, rightToken)) return true;
                difference = "工程字段不一致（可使用导出 JSON 对比工具查看具体差异）。";
                return false;
            }
            catch (Exception ex)
            {
                difference = "工程参数比较失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>Returns a stable SHA-256 for a UTF-8 JSON file.</summary>
        public static string ComputeSha256(string filePath, out string error)
        {
            error = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    error = "文件不存在。";
                    return string.Empty;
                }

                using (var stream = File.OpenRead(filePath))
                using (var sha = SHA256.Create())
                {
                    var bytes = sha.ComputeHash(stream);
                    var builder = new StringBuilder(bytes.Length * 2);
                    foreach (var value in bytes) builder.Append(value.ToString("x2"));
                    return builder.ToString();
                }
            }
            catch (Exception ex)
            {
                error = "计算文件校验值失败：" + ex.Message;
                return string.Empty;
            }
        }

        /// <summary>Normalizes a user-facing project name for a portable file name.</summary>
        public static string SanitizeProjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "UntitledProject";
            var result = value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid.ToString(), "_");
            return string.IsNullOrWhiteSpace(result) ? "UntitledProject" : result;
        }

        /// <summary>Ensures a file has the open-format extension without duplicating it.</summary>
        public static string EnsureExtension(string value)
        {
            var name = SanitizeProjectName(value);
            return name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? name : name + Extension;
        }
    }
}
