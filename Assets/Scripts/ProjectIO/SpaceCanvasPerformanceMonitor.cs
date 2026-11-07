using System;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>Bounded, allocation-free frame sampling. Frame intervals are not GPU render time or compositor FPS.</summary>
    public sealed class SpaceCanvasPerformanceMonitor : MonoBehaviour
    {
        [Serializable] public sealed class Report
        {
            public string capturedUtc, appVersion, unityVersion, device, platform, graphics;
            public string measurement = "Foreground Unity unscaled frame intervals; includes CPU wait; not compositor FPS. GPU ms only when XRStats reports it.";
            public int samples, minObjects, maxObjects, gc0Collections;
            public float sampleSeconds, averageFrameMs, p50Ms, p95Ms, p99Ms, maxFrameMs, averageGpuMs;
            public int gpuSamples;
            public long allocatedMemoryBytes, reservedMemoryBytes;
        }
        private readonly float[] frames = new float[5400];
        private int count, cursor, gcStart, minObjects = int.MaxValue, maxObjects;
        private float nextCount, gpuTotal;
        private int gpuCount;
        private float foregroundSeconds;
        private int skipFrames;
        private bool focused = true, autoExported;
        private void Start() { gcStart = GC.CollectionCount(0); }
        private void Update()
        {
            if (!focused || Time.unscaledDeltaTime <= 0) return;
            if (skipFrames > 0) { skipFrames--; return; }
            foregroundSeconds += Time.unscaledDeltaTime;
            frames[cursor] = Time.unscaledDeltaTime * 1000f; cursor = (cursor + 1) % frames.Length; count = Mathf.Min(count + 1, frames.Length);
            if (UnityEngine.XR.XRStats.TryGetGPUTimeLastFrame(out var gpu) && gpu > 0) { gpuTotal += gpu; gpuCount++; }
            if (Time.unscaledTime >= nextCount)
            {
                var objects = SpaceCanvasRuntime.GetEditableObjects().Count; minObjects = Mathf.Min(minObjects, objects); maxObjects = Mathf.Max(maxObjects, objects); nextCount = Time.unscaledTime + 2;
            }
            // Capture once after one minute of foreground sampling. Explicit exports can be made later.
            if (!autoExported && foregroundSeconds >= 60) { autoExported = true; Export(out _, out _); }
        }
        public Report Capture()
        {
            var sorted = new float[count]; Array.Copy(frames, sorted, count); Array.Sort(sorted);
            var total = 0f; foreach (var sample in sorted) total += sample;
            return new Report
            {
                capturedUtc = DateTime.UtcNow.ToString("O"), appVersion = Application.version, unityVersion = Application.unityVersion,
                device = SystemInfo.deviceModel, platform = Application.platform.ToString(), graphics = SystemInfo.graphicsDeviceName,
                samples = count, sampleSeconds = total / 1000, averageFrameMs = count > 0 ? total / count : 0,
                p50Ms = Percentile(sorted, .5f), p95Ms = Percentile(sorted, .95f), p99Ms = Percentile(sorted, .99f), maxFrameMs = count > 0 ? sorted[count - 1] : 0,
                minObjects = minObjects == int.MaxValue ? 0 : minObjects, maxObjects = maxObjects, gc0Collections = GC.CollectionCount(0) - gcStart,
                allocatedMemoryBytes = Profiler.GetTotalAllocatedMemoryLong(), reservedMemoryBytes = Profiler.GetTotalReservedMemoryLong(),
                averageGpuMs = gpuCount > 0 ? gpuTotal / gpuCount : 0, gpuSamples = gpuCount
            };
        }
        public static float Percentile(float[] sorted, float fraction)
        { return sorted.Length == 0 ? 0 : sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Length * fraction) - 1, 0, sorted.Length - 1)]; }
        public bool Export(out string path, out string error)
        {
            path = ""; error = "";
            try
            {
                var directory = Path.Combine(Application.persistentDataPath, "Diagnostics"); Directory.CreateDirectory(directory);
                path = Path.Combine(directory, "performance-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json");
                File.WriteAllText(path, JsonUtility.ToJson(Capture(), true), new System.Text.UTF8Encoding(false));
                Debug.Log("SpaceCanvas Performance: " + path); return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        private void OnApplicationFocus(bool focus) { focused = focus; skipFrames = 2; }
        private void OnApplicationPause(bool pause) { focused = !pause; skipFrames = 2; }
    }
}
