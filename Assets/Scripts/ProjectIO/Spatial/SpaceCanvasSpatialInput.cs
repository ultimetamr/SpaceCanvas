using ByteDance.PICO.SpatialAdapter;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using Phase = UnityEngine.InputSystem.TouchPhase;

namespace SpaceCanvas.ProjectIO.Spatial
{
    /// <summary>
    /// 官方 SpatialInputSupport 是手势/控制器的统一入口。眼动命中由系统提供，
    /// 本组件不自行采集眼睛数据。一次只接受一个编辑指针，另一只手仍可操作系统窗口。
    /// 鼠标路径仅供 Unity Editor 预览，不作为设备输入验证的替代。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasSpatialInput : MonoBehaviour
    {
        public SpaceCanvasEditSession session;
        public Camera previewCamera;
        private int interaction = -1;
        private Plane mousePlane;
        private Vector2 mouseStart;
        private Vector3 mouseWorldStart;
        private float lastSeen;

        private void OnEnable() { EnhancedTouchSupport.Enable(); }
        private void OnDisable()
        {
            if (session != null) session.EndGesture(true);
            interaction = -1; EnhancedTouchSupport.Disable();
        }
        private void OnApplicationFocus(bool focused)
        { if (!focused && session != null) { session.EndGesture(true); interaction = -1; } }

        private void Update()
        {
            if (session == null) return;
            foreach (var touch in Touch.activeTouches)
            {
                var state = SpatialInputSupport.GetInputState(touch);
                if (state.phase == Phase.Began && interaction < 0 && state.type != InteractionType.Touch)
                {
                    var target = state.targetObject;
                    var item = target == null ? null : target.GetComponentInParent<SpaceCanvasEditableObject>();
                    if (session.BeginGesture(item, state.currentPosition, state.deviceRotation))
                    { interaction = state.interactionId; lastSeen = Time.unscaledTime; }
                }
                if (interaction != state.interactionId) continue;
                lastSeen = Time.unscaledTime;
                if (state.phase == Phase.Ended || state.phase == Phase.Canceled || state.phase == Phase.None)
                { session.EndGesture(state.phase != Phase.Ended); interaction = -1; }
                else session.UpdateGesture(state.currentPosition, state.deviceRotation);
            }
            // 系统丢失追踪且未送达 Ended 时取消事务，避免物体持续跟手。
            if (interaction >= 0 && Time.unscaledTime - lastSeen > .5f)
            { session.EndGesture(true); interaction = -1; }
#if UNITY_EDITOR
            UpdateMousePreview();
#endif
        }

#if UNITY_EDITOR
        private void UpdateMousePreview()
        {
            var mouse = Mouse.current;
            if (mouse == null || previewCamera == null || interaction >= 0) return;
            var screen = mouse.position.ReadValue();
            var ray = previewCamera.ScreenPointToRay(screen);
            if (mouse.leftButton.wasPressedThisFrame && !session.IsManipulating)
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                if (!SpaceCanvasPicking.Raycast(ray, 20f, out var hit)) return;
                var item = hit.item;
                if (item == null) return;
                mousePlane = new Plane(-previewCamera.transform.forward, hit.point);
                mouseStart = screen; mouseWorldStart = hit.point;
                session.BeginGesture(item, hit.point, Quaternion.identity);
            }
            if (!session.IsManipulating) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            { session.EndGesture(true); return; }
            if (mouse.leftButton.isPressed && mousePlane.Raycast(ray, out var distance))
            {
                var rotation = Quaternion.Euler(0, (screen.x - mouseStart.x) * .3f, 0);
                session.UpdateGesture(ray.GetPoint(distance), rotation);
            }
            if (mouse.leftButton.wasReleasedThisFrame) session.EndGesture(false);
        }
#endif
    }
}
