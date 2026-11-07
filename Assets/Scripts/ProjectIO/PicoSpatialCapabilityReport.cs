using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// PICO Spatial/Swan 能力探测结果。
    ///
    /// 这个类只做反射和环境探测，不引用厂商私有 API，因此 ProjectIO 在普通 Unity
    /// 工程中也可以独立编译。能力探测不会把“检测到 SpatialCamera”误报成“已经打开
    /// 多个系统窗口”；只有发现可调用的窗口创建 API 时才会标记原生多窗口可用。
    /// </summary>
    [Serializable]
    public sealed class PicoSpatialCapabilityReport
    {
        public bool isAndroid;
        public bool isEditor;
        public bool spatialAdapterAssemblyFound;
        public bool spatialCameraTypeFound;
        public bool spatialCameraInstanceFound;
        public bool sharedSpaceModeReported;
        public bool nativePlanarWindowApiFound;
        public bool nativeVolumetricWindowApiFound;
        public bool systemKeyboardSupported;
        public bool eyeTrackingDeviceDetected;
        public string spatialMode = string.Empty;
        public string sdkAssembly = string.Empty;
        public string note = string.Empty;

        public bool NativeMultiWindowApiFound => nativePlanarWindowApiFound || nativeVolumetricWindowApiFound;

        public static PicoSpatialCapabilityReport Collect()
        {
            var report = new PicoSpatialCapabilityReport
            {
                isAndroid = Application.platform == RuntimePlatform.Android,
                isEditor = Application.isEditor,
                systemKeyboardSupported = TouchScreenKeyboard.isSupported
            };

            var spatialType = FindType("ByteDance.PICO.SpatialAdapter.SpatialCamera");
            report.spatialCameraTypeFound = spatialType != null;
            if (spatialType != null)
            {
                report.spatialAdapterAssemblyFound = spatialType.Assembly != null;
                report.sdkAssembly = spatialType.Assembly.GetName().Name;
                try
                {
                    var current = spatialType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                    report.spatialCameraInstanceFound = current != null;
                    var modeProperty = spatialType.GetProperty("CurrentSpatialMode", BindingFlags.Public | BindingFlags.Static);
                    report.spatialMode = modeProperty == null ? "unknown" : (modeProperty.GetValue(null, null)?.ToString() ?? "unknown");
                    report.sharedSpaceModeReported = report.spatialMode.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0
                        || report.spatialMode.IndexOf("Shared", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                catch (Exception ex)
                {
                    report.note = "SpatialCamera 状态读取失败：" + ex.Message;
                }

                DetectWindowCreationApi(spatialType.Assembly, report);
            }

            report.eyeTrackingDeviceDetected = DetectEyeTrackingDevice();
            if (!report.spatialCameraTypeFound)
                report.note = Append(report.note, "未检测到 SpatialAdapter，运行时使用 OpenXR Full Space MR fallback。");
            else if (!report.NativeMultiWindowApiFound)
                report.note = Append(report.note, "检测到 SpatialCamera，但当前 Unity 包未暴露可调用的多原生窗口创建 API。");
            return report;
        }

        public string ToHumanText()
        {
            var lines = new List<string>
            {
                "PICO 空间能力矩阵",
                "运行平台：" + (isAndroid ? "Android 真机/模拟器" : (isEditor ? "Unity Editor" : Application.platform.ToString())),
                "SpatialAdapter：" + (spatialAdapterAssemblyFound ? "已安装" : "未安装"),
                "SpatialCamera：" + (spatialCameraTypeFound ? (spatialCameraInstanceFound ? "已实例化" : "类型存在，场景未实例化") : "未检测到"),
                "空间模式：" + (string.IsNullOrEmpty(spatialMode) ? "未报告" : spatialMode),
                "Shared Space 状态：" + (sharedSpaceModeReported ? "由 SDK 报告为窗口容器模式" : "未确认"),
                "原生 Planar 窗口 API：" + (nativePlanarWindowApiFound ? "已发现" : "未发现，使用 Unity Canvas fallback"),
                "原生 Volumetric 窗口 API：" + (nativeVolumetricWindowApiFound ? "已发现" : "未发现，使用 Unity 场景 fallback"),
                "系统键盘：" + (systemKeyboardSupported ? "可用" : "不可用/由系统策略决定"),
                "眼动设备：" + (eyeTrackingDeviceDetected ? "已检测到" : "未检测到，使用手柄/手势射线"),
                "当前交付模式：Full Space MR + PICO Passthrough + Unity 世界空间 UI。"
            };
            if (!string.IsNullOrWhiteSpace(note)) lines.Add("说明：" + note);
            return string.Join("\n", lines);
        }

        private static void DetectWindowCreationApi(Assembly assembly, PicoSpatialCapabilityReport report)
        {
            if (assembly == null) return;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
            foreach (var type in types)
            {
                var typeName = type.Name;
                var isWindowType = typeName.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0
                    || typeName.IndexOf("Planar", StringComparison.OrdinalIgnoreCase) >= 0
                    || typeName.IndexOf("Volumetric", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isWindowType) continue;
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                foreach (var method in methods)
                {
                    var name = method.Name;
                    var create = name.IndexOf("Create", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("Open", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("Spawn", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!create) continue;
                    if (name.IndexOf("Planar", StringComparison.OrdinalIgnoreCase) >= 0 || typeName.IndexOf("Planar", StringComparison.OrdinalIgnoreCase) >= 0)
                        report.nativePlanarWindowApiFound = true;
                    if (name.IndexOf("Volumetric", StringComparison.OrdinalIgnoreCase) >= 0 || typeName.IndexOf("Volumetric", StringComparison.OrdinalIgnoreCase) >= 0)
                        report.nativeVolumetricWindowApiFound = true;
                }
            }
        }

        private static bool DetectEyeTrackingDevice()
        {
            // 仅在 XR 模块存在时反射调用，避免普通 Unity 工程增加硬依赖。
            var inputDevices = FindType("UnityEngine.XR.InputDevices, UnityEngine.XRModule");
            var characteristics = FindType("UnityEngine.XR.InputDeviceCharacteristics, UnityEngine.XRModule");
            if (inputDevices == null || characteristics == null) return false;
            try
            {
                var eye = Enum.Parse(characteristics, "EyeTracking", true);
                var listType = typeof(List<>).MakeGenericType(FindType("UnityEngine.XR.InputDevice, UnityEngine.XRModule"));
                var list = Activator.CreateInstance(listType);
                var method = inputDevices.GetMethod("GetDevicesWithCharacteristics", BindingFlags.Public | BindingFlags.Static);
                method?.Invoke(null, new[] { eye, list });
                return (int)listType.GetProperty("Count").GetValue(list, null) > 0;
            }
            catch { return false; }
        }

        private static Type FindType(string name)
        {
            return Type.GetType(name, false) ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType(name, false); } catch { return null; } })
                .FirstOrDefault(t => t != null);
        }

        private static string Append(string current, string value)
        {
            return string.IsNullOrEmpty(current) ? value : current + " " + value;
        }
    }

    public static class PicoSpatialCapabilities
    {
        private static PicoSpatialCapabilityReport cached;
        public static PicoSpatialCapabilityReport Current => cached ?? (cached = PicoSpatialCapabilityReport.Collect());
        public static void Refresh() => cached = PicoSpatialCapabilityReport.Collect();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void LogOnStartup()
        {
            // 日志只输出一次，方便评审和真机诊断；不影响运行时 UI。
            var report = Current;
            Debug.Log("[SpaceCanvas Capability]\n" + report.ToHumanText());
        }
    }
}
