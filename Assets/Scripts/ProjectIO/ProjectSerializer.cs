using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace SpaceCanvas.ProjectIO
{
    public sealed class SpaceCanvasFormatException : Exception
    {
        public SpaceCanvasFormatException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>Single JSON boundary for the open .spacecanvas format.</summary>
    public static class ProjectSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
            ContractResolver = new DefaultContractResolver()
        };

        public static string Serialize(SpaceCanvasProject project)
        {
            if (project == null) throw new SpaceCanvasFormatException("无法序列化空工程。");
            project.EnsureDefaults();
            try
            {
                return JsonConvert.SerializeObject(project, Settings);
            }
            catch (Exception ex)
            {
                throw new SpaceCanvasFormatException("工程序列化失败：" + ex.Message, ex);
            }
        }

        public static SpaceCanvasProject Deserialize(string jsonText)
        {
            if (string.IsNullOrWhiteSpace(jsonText))
                throw new SpaceCanvasFormatException("工程文件为空，无法解析。");

            try
            {
                var project = JsonConvert.DeserializeObject<SpaceCanvasProject>(jsonText, Settings);
                if (project == null) throw new SpaceCanvasFormatException("工程 JSON 根节点为空。");
                project.EnsureDefaults();
                return project;
            }
            catch (SpaceCanvasFormatException)
            {
                throw;
            }
            catch (JsonException ex)
            {
                throw new SpaceCanvasFormatException("工程 JSON 格式损坏：" + ex.Message, ex);
            }
            catch (Exception ex)
            {
                throw new SpaceCanvasFormatException("工程解析失败：" + ex.Message, ex);
            }
        }
    }
}
