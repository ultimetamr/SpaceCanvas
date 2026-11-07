using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// Unity 内容槽位适配草案，不是原生多窗口实现。
    ///
    /// PICO SpatialAdapter exposes one SpatialCamera for the application output. The
    /// host therefore keeps the four logical surfaces (three Planar panels and one
    /// Volumetric editor) in one stable hierarchy and lets the SDK own the actual
    /// system-level app window. This is deliberately reflection based: a blank Unity
    /// project can use ProjectIO without taking a hard compile-time dependency on the
    /// optional SpatialAdapter package, while a Swan build is detected automatically.
    ///
    /// Attach this component once to the scene's XR origin or global manager. Existing
    /// UI can call RegisterPlanarContent/RegisterVolumetricContent after creating its
    /// Canvas or 3D root. In a non-Swan build the same hierarchy remains usable as a
    /// normal Unity fallback and no native API is called.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasSwanWindowHost : MonoBehaviour
    {
        public enum WindowKind
        {
            AssetLibrary,
            Properties,
            Toolbar,
            VolumetricScene
        }

        [Header("Swan / Shared Space")]
        public bool autoCreate = true;
        public bool logSdkDetection = true;
        public float defaultDistance = 1.2f;
        public float minDistance = 0.8f;
        public float maxDistance = 2.5f;
        public float followSmoothTime = 0.18f;
        public float recenterAngle = 25f;

        private readonly Dictionary<WindowKind, Transform> slots = new Dictionary<WindowKind, Transform>();
        private Type spatialCameraType;
        private Component spatialCamera;

        public bool IsSpatialAdapterAvailable { get; private set; }
        public bool IsSharedSpaceReady { get; private set; }
        public bool IsNativePlanarWindowApiAvailable { get; private set; }
        public bool IsNativeVolumetricWindowApiAvailable { get; private set; }
        public PicoSpatialCapabilityReport CapabilityReport { get; private set; }

        private void Awake()
        {
            if (!autoCreate) return;
            EnsureWindowHierarchy();
            DetectSpatialAdapter();
        }

        private void LateUpdate()
        {
            foreach (var pair in slots)
            {
                var follow = pair.Value == null ? null : pair.Value.GetComponent<SpaceCanvasSwanWindowFollow>();
                if (follow == null) continue;
                follow.minDistance = minDistance;
                follow.maxDistance = maxDistance;
                follow.smoothTime = followSmoothTime;
                follow.recenterAngle = recenterAngle;
            }
        }

        public Transform GetWindowSlot(WindowKind kind)
        {
            EnsureWindowHierarchy();
            return slots[kind];
        }

        public void RegisterPlanarContent(WindowKind kind, GameObject content)
        {
            if (kind == WindowKind.VolumetricScene) throw new ArgumentException("VolumetricScene 必须使用 RegisterVolumetricContent。", nameof(kind));
            RegisterContent(kind, content);
        }

        public void RegisterVolumetricContent(GameObject content)
        {
            RegisterContent(WindowKind.VolumetricScene, content);
        }

        public void RecenterAllWindows()
        {
            foreach (var pair in slots)
            {
                var follow = pair.Value == null ? null : pair.Value.GetComponent<SpaceCanvasSwanWindowFollow>();
                if (follow != null) follow.RequestRecenter();
            }
        }

        private void RegisterContent(WindowKind kind, GameObject content)
        {
            if (content == null) return;
            var slot = GetWindowSlot(kind);
            content.transform.SetParent(slot, false);
            content.transform.localPosition = Vector3.zero;
            content.transform.localRotation = Quaternion.identity;
            content.transform.localScale = Vector3.one;
        }

        private void EnsureWindowHierarchy()
        {
            CreateSlot(WindowKind.AssetLibrary, "SwanPlanar_AssetLibrary", new Vector3(-0.56f, 0.06f, defaultDistance));
            CreateSlot(WindowKind.Properties, "SwanPlanar_Properties", new Vector3(0.56f, 0.06f, defaultDistance));
            CreateSlot(WindowKind.Toolbar, "SwanPlanar_Toolbar", new Vector3(0f, -0.46f, 1.0f));
            CreateSlot(WindowKind.VolumetricScene, "SwanVolumetric_Scene", new Vector3(0f, 0.02f, 1.5f));
        }

        private void CreateSlot(WindowKind kind, string name, Vector3 localPosition)
        {
            if (slots.ContainsKey(kind) && slots[kind] != null) return;
            var existing = transform.Find(name);
            var go = existing == null ? new GameObject(name) : existing.gameObject;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var follow = go.GetComponent<SpaceCanvasSwanWindowFollow>();
            if (follow == null) follow = go.AddComponent<SpaceCanvasSwanWindowFollow>();
            follow.target = transform;
            follow.defaultLocalPosition = localPosition;
            follow.defaultDistance = localPosition.z;
            follow.minDistance = minDistance;
            follow.maxDistance = maxDistance;
            follow.smoothTime = followSmoothTime;
            follow.recenterAngle = recenterAngle;
            slots[kind] = go.transform;
        }

        private void DetectSpatialAdapter()
        {
            CapabilityReport = PicoSpatialCapabilityReport.Collect();
            spatialCameraType = Type.GetType("ByteDance.PICO.SpatialAdapter.SpatialCamera, ByteDance.PICO.SpatialAdapter.Runtime")
                ?? Type.GetType("ByteDance.PICO.SpatialAdapter.SpatialCamera");
            if (spatialCameraType == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        spatialCameraType = assembly.GetType("ByteDance.PICO.SpatialAdapter.SpatialCamera", false);
                        if (spatialCameraType != null) break;
                    }
                    catch { /* 某些厂商程序集可能无法完整枚举，继续 fallback。 */ }
                }
            }
            IsSpatialAdapterAvailable = CapabilityReport.spatialAdapterAssemblyFound && CapabilityReport.spatialCameraTypeFound;
            IsNativePlanarWindowApiAvailable = CapabilityReport.nativePlanarWindowApiFound;
            IsNativeVolumetricWindowApiAvailable = CapabilityReport.nativeVolumetricWindowApiFound;
            if (!IsSpatialAdapterAvailable)
            {
                IsSharedSpaceReady = false;
                if (logSdkDetection) Debug.LogWarning("SpaceCanvas Swan：未检测到 PICO SpatialAdapter，已使用 Unity fallback 窗口层级。请在 Swan 构建中启用官方 SpatialAdapter 包。");
                return;
            }

            var cameras = FindObjectsOfType(spatialCameraType, true);
            if (cameras != null && cameras.Length > 0) spatialCamera = cameras[0] as Component;
            // 检测到组件不等于系统已打开 Shared Space 窗口；只读取实际窗口状态。
            var windowOpen = spatialCameraType.GetProperty("WindowOpen");
            IsSharedSpaceReady = CapabilityReport.sharedSpaceModeReported && spatialCamera != null && Application.platform == RuntimePlatform.Android
                && windowOpen != null && windowOpen.GetValue(spatialCamera) is bool open && open;
            if (logSdkDetection)
            {
                Debug.Log(IsSharedSpaceReady
                    ? "SpaceCanvas Swan：SpatialCamera 报告窗口已打开。"
                    : "SpaceCanvas Swan：尚未确认原生系统窗口打开。当前内容槽位只表示 Unity 层级。 ");
                if (!CapabilityReport.NativeMultiWindowApiFound)
                    Debug.LogWarning("SpaceCanvas Swan：当前 SDK 未暴露可调用的 Planar/Volumetric 创建 API，继续使用稳定的 Full Space MR fallback。");
            }
        }
    }

    /// <summary>Smoothed, bounded follow behaviour for a logical Swan window slot.</summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasSwanWindowFollow : MonoBehaviour
    {
        public Transform target;
        public Vector3 defaultLocalPosition;
        public float defaultDistance = 1.2f;
        public float minDistance = 0.8f;
        public float maxDistance = 2.5f;
        public float smoothTime = 0.18f;
        public float recenterAngle = 25f;

        private Vector3 velocity;
        private bool recenterRequested;
        private Vector3 lastTargetForward;

        public void RequestRecenter() => recenterRequested = true;

        private void LateUpdate()
        {
            if (target == null) return;
            if (lastTargetForward == Vector3.zero) lastTargetForward = target.forward;
            var targetTurned = Vector3.Angle(lastTargetForward, target.forward) > recenterAngle;
            var local = transform.localPosition;
            var distance = Mathf.Clamp(local.z, minDistance, maxDistance);
            local.z = distance;
            if (recenterRequested || targetTurned)
            {
                local = defaultLocalPosition;
                local.z = Mathf.Clamp(local.z <= 0f ? defaultDistance : local.z, minDistance, maxDistance);
                recenterRequested = false;
                lastTargetForward = target.forward;
            }
            transform.localPosition = Vector3.SmoothDamp(transform.localPosition, local, ref velocity, Mathf.Max(0.01f, smoothTime));
            // Window content faces the host's forward direction. Keep the local
            // rotation stable so a panel never flips when the user moves around it.
            transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.identity, Time.unscaledDeltaTime * 5f);
        }
    }
}
