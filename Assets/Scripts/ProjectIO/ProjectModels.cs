using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>Unit system used by a SpaceCanvas scene.</summary>
    public enum SceneUnitSystem
    {
        Meter = 0,
        Centimeter = 1,
        Millimeter = 2
    }

    [Serializable]
    public sealed class SceneSettings
    {
        [JsonProperty("unitSystem")]
        public SceneUnitSystem unitSystem = SceneUnitSystem.Meter;

        [JsonProperty("gridEnabled")]
        public bool gridEnabled = true;

        [JsonProperty("gridSize")]
        public float gridSize = 0.25f;

        public void EnsureDefaults()
        {
            if (gridSize <= 0f || float.IsNaN(gridSize) || float.IsInfinity(gridSize))
                gridSize = 0.25f;
        }
    }

    [Serializable]
    public sealed class SceneObject
    {
        [JsonProperty("id")]
        public string id = string.Empty;

        [JsonProperty("assetId")]
        public string assetId = "primitive.cube";

        [JsonProperty("parentId")]
        public string parentId = string.Empty;

        [JsonProperty("localPosition")]
        public float[] localPosition = new float[3];

        [JsonProperty("localRotation")]
        public float[] localRotation = new[] { 0f, 0f, 0f, 1f };

        [JsonProperty("localScale")]
        public float[] localScale = new[] { 1f, 1f, 1f };

        [JsonProperty("colorHex")]
        public string colorHex = "#FFFFFFFF";

        [JsonProperty("alpha")]
        public float alpha = 1f;

        [JsonProperty("metallic")]
        public float metallic;

        [JsonProperty("colliderEnabled")]
        public bool colliderEnabled = true;

        public void EnsureDefaults(int index)
        {
            if (string.IsNullOrWhiteSpace(id)) id = "object-" + index;
            if (string.IsNullOrWhiteSpace(assetId)) assetId = "primitive.cube";
            if (localPosition == null || localPosition.Length != 3) localPosition = new float[3];
            if (localRotation == null || localRotation.Length != 4) localRotation = new[] { 0f, 0f, 0f, 1f };
            if (localScale == null || localScale.Length != 3) localScale = new[] { 1f, 1f, 1f };
            if (string.IsNullOrWhiteSpace(colorHex)) colorHex = "#FFFFFFFF";
            alpha = Clamp01(alpha, 1f);
            metallic = Clamp01(metallic, 0f);
        }

        private static float Clamp01(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return Math.Max(0f, Math.Min(1f, value));
        }
    }

    [Serializable]
    public sealed class SceneGroup
    {
        [JsonProperty("groupId")]
        public string groupId = string.Empty;

        [JsonProperty("groupName")]
        public string groupName = "Group";

        [JsonProperty("childIds")]
        public List<string> childIds = new List<string>();

        public void EnsureDefaults(int index)
        {
            if (string.IsNullOrWhiteSpace(groupId)) groupId = "group-" + index;
            if (string.IsNullOrWhiteSpace(groupName)) groupName = "Group";
            if (childIds == null) childIds = new List<string>();
        }
    }

    [Serializable]
    public sealed class SpaceCanvasProject
    {
        [JsonProperty("formatVersion")]
        public string formatVersion = "1.0";

        [JsonProperty("projectName")]
        public string projectName = "Untitled SpaceCanvas Project";

        [JsonProperty("createTime")]
        public long createTime;

        [JsonProperty("updateTime")]
        public long updateTime;

        // 新保存的可选毫秒时间；连续保存发生在同一秒时仍可准确排序，旧工具忽略它即可。
        [JsonProperty("savedAtUtcMilliseconds", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public long savedAtUtcMilliseconds;

        [JsonProperty("settings")]
        public SceneSettings settings = new SceneSettings();

        [JsonProperty("objects")]
        public List<SceneObject> objects = new List<SceneObject>();

        [JsonProperty("groups")]
        public List<SceneGroup> groups = new List<SceneGroup>();

        // 可选扩展，不改变 1.0 布局格式；旧阅读器可以忽略，旧工程可以没有预览。
        [JsonProperty("preview", NullValueHandling = NullValueHandling.Ignore)]
        public SpaceCanvasProjectPreview preview;

        public void EnsureDefaults()
        {
            if (string.IsNullOrWhiteSpace(formatVersion)) formatVersion = "1.0";
            if (string.IsNullOrWhiteSpace(projectName)) projectName = "Untitled SpaceCanvas Project";
            if (settings == null) settings = new SceneSettings();
            settings.EnsureDefaults();
            if (objects == null) objects = new List<SceneObject>();
            if (groups == null) groups = new List<SceneGroup>();
            for (var i = 0; i < objects.Count; i++)
                if (objects[i] == null) objects[i] = new SceneObject();
            for (var i = 0; i < objects.Count; i++) objects[i].EnsureDefaults(i);
            for (var i = 0; i < groups.Count; i++)
            {
                if (groups[i] == null) groups[i] = new SceneGroup();
                groups[i].EnsureDefaults(i);
            }
        }
    }

    [Serializable]
    public sealed class SpaceCanvasProjectPreview
    {
        public string encoding = "png-base64";
        public string pngBase64;
        public long captureTime;
    }
}
