using System.Collections.Generic;
using SpaceCanvas.ProjectIO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace SpaceCanvas.ProjectIO.XR
{
    /// <summary>
    /// 眼动 fallback：当运行时公开 EyeTracking InputDevice 时，用视线做 UI 悬停；
    /// 可选驻留点击不会覆盖手柄/手势射线，默认关闭以避免误触。
    /// PICO 设备未公开眼动或权限不可用时，本组件安静退出并保留现有射线。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasEyeGazeInteractor : MonoBehaviour
    {
        public Camera head;
        public bool enableGazeHover = true;
        public bool enableDwellClick;
        [Range(.2f, 3f)] public float dwellSeconds = .9f;
        public float maxDistance = 10f;
        public LineRenderer line;

        private readonly List<InputDevice> devices = new List<InputDevice>();
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private PointerEventData pointer;
        private GameObject hovered;
        private float hoverStarted;
        private bool clicked;
        private float nextDiscovery;

        public bool EyeTrackingAvailable { get; private set; }

        private void Awake()
        {
            if (head == null) head = Camera.main;
            if (line == null) line = GetComponent<LineRenderer>();
            if (line != null)
            {
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.startWidth = .002f;
                line.endWidth = .001f;
                line.enabled = false;
            }
        }

        private void Update()
        {
            if (!enableGazeHover || head == null || EventSystem.current == null || SpaceCanvasOpenXRPointer.AnyTracked)
            {
                EyeTrackingAvailable = false;
                ClearHover();
                return;
            }
            if (Time.unscaledTime >= nextDiscovery)
            {
                devices.Clear(); InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking, devices);
                nextDiscovery = Time.unscaledTime + (devices.Count == 0 ? 2f : .5f);
            }
            EyeTrackingAvailable = false;
            var origin = head.transform.position;
            var direction = head.transform.forward;
            foreach (var device in devices)
            {
                if (!device.isValid) continue;
                if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out var p)
                    || !device.TryGetFeatureValue(CommonUsages.deviceRotation, out var q)
                    || !device.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) || !tracked
                    || !Finite(p.x) || !Finite(p.y) || !Finite(p.z) || !Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w)
                    || q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w < .5f) continue;
                var trackingOrigin = head.transform.parent;
                origin = trackingOrigin == null ? p : trackingOrigin.TransformPoint(p);
                direction = (trackingOrigin == null ? q : trackingOrigin.rotation * q) * Vector3.forward;
                EyeTrackingAvailable = true;
                break;
            }
            if (!EyeTrackingAvailable)
            {
                ClearHover();
                return;
            }
            var end = origin + direction.normalized * maxDistance;
            if (line != null)
            {
                line.enabled = true;
                line.SetPosition(0, origin);
                line.SetPosition(1, end);
            }
            var screen = head.WorldToScreenPoint(origin + direction.normalized * Mathf.Min(2f, maxDistance));
            pointer ??= new PointerEventData(EventSystem.current) { pointerId = -101, button = PointerEventData.InputButton.Left };
            pointer.position = new Vector2(screen.x, screen.y);
            hits.Clear();
            EventSystem.current.RaycastAll(pointer, hits);
            var target = hits.Count == 0 ? null : hits[0].gameObject;
            if (target == hovered)
            {
                if (enableDwellClick && target != null && !clicked && Time.unscaledTime - hoverStarted >= dwellSeconds)
                {
                    ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
                    clicked = true;
                }
                return;
            }
            ClearHover();
            hovered = target;
            hoverStarted = Time.unscaledTime;
            clicked = false;
            if (hovered != null) ExecuteEvents.Execute(hovered, pointer, ExecuteEvents.pointerEnterHandler);
        }

        private void ClearHover()
        {
            if (line != null) line.enabled = false;
            if (hovered != null && pointer != null) ExecuteEvents.Execute(hovered, pointer, ExecuteEvents.pointerExitHandler);
            hovered = null;
            clicked = false;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private void OnDisable() { EyeTrackingAvailable = false; ClearHover(); }
    }
}
