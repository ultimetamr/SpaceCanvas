using System;
using System.Collections.Generic;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>在触碰场景前检查身份、层级与数值；无效工程不会清空当前编辑内容。</summary>
    public static class ProjectValidator
    {
        public static void Validate(SpaceCanvasProject project)
        {
            if (project == null) throw new SpaceCanvasFormatException("工程为空。");
            project.EnsureDefaults();
            if (project.objects.Count > 10000) throw new SpaceCanvasFormatException("工程物体超过 10000 个，无法安全载入。");
            var byId = new Dictionary<string, SceneObject>(StringComparer.Ordinal);
            foreach (var item in project.objects)
            {
                if (byId.ContainsKey(item.id)) throw new SpaceCanvasFormatException("物体 ID 重复：" + item.id);
                byId.Add(item.id, item);
                Finite(item.localPosition, item.id); Finite(item.localRotation, item.id); Finite(item.localScale, item.id);
                var q = item.localRotation;
                if ((double)q[0] * q[0] + (double)q[1] * q[1] + (double)q[2] * q[2] + (double)q[3] * q[3] < 1e-12)
                    throw new SpaceCanvasFormatException("物体旋转四元数不能全为零：" + item.id);
            }
            var complete = new HashSet<string>();
            foreach (var item in project.objects)
            {
                var path = new HashSet<string>();
                var current = item;
                while (current != null && !complete.Contains(current.id))
                {
                    if (!path.Add(current.id)) throw new SpaceCanvasFormatException("父子层级存在循环：" + current.id);
                    if (string.IsNullOrEmpty(current.parentId) || !byId.TryGetValue(current.parentId, out var parent)) break;
                    current = parent;
                }
                complete.UnionWith(path);
            }
        }

        private static void Finite(float[] values, string id)
        {
            foreach (var value in values)
                if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 1000000)
                    throw new SpaceCanvasFormatException("物体坐标、旋转或缩放含非法数值：" + id);
        }
    }
}
