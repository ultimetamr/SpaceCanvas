using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

namespace SpaceCanvas.ProjectIO.XR
{
    /// <summary>
    /// Reads aim and press from the same OpenXR hand/controller. XRI owns UI event dispatch.
    /// Pinch/trigger also edits 3D objects when the ray is not over UI. Tracking loss cancels
    /// the current gesture; a newly acquired device must release before it can press.
    /// </summary>
    [DefaultExecutionOrder(-20000)]
    [RequireComponent(typeof(XRRayInteractor))]
    public sealed class SpaceCanvasOpenXRPointer : MonoBehaviour
    {
        public bool leftHand;
        public SpaceCanvasEditSession session;
        private XRRayInteractor rayInteractor;
        private XRInputButtonReader uiPress;
        private int deviceId = -1;
        private bool lastPressed, released, uiDelivered, ownsGesture, ownsHandle;
        private float gestureDistance;
        private InputActionMap inputActions;
        private static readonly System.Collections.Generic.HashSet<SpaceCanvasOpenXRPointer> pointers = new System.Collections.Generic.HashSet<SpaceCanvasOpenXRPointer>();
        public bool Tracked { get; private set; }
        public bool Pressed => lastPressed;
        public XRRayInteractor Interactor => rayInteractor;
        public static bool AnyTracked { get { foreach (var p in pointers) if (p != null && p.Tracked) return true; return false; } }
        public static void ConsumeUIActivation()
        {
            foreach(var pointer in pointers)
            {
                if(pointer==null)continue;
                pointer.released=false; pointer.lastPressed=false; pointer.DeliverUIPress(false);
            }
        }

        private void Awake()
        {
            rayInteractor = GetComponent<XRRayInteractor>();
            uiPress = new XRInputButtonReader("SpaceCanvas UI", inputSourceMode: XRInputButtonReader.InputSourceMode.ManualValue);
            rayInteractor.uiPressInput = uiPress;
            rayInteractor.enableUIInteraction = true;
            rayInteractor.maxRaycastDistance = 3f;
        }

        private void OnEnable()
        {
            pointers.Add(this);
            // OpenXR activates bindings through enabled InputActions. Direct ReadValue()
            // alone does not request native pose/button actions on every runtime.
            var usage = leftHand ? "LeftHand" : "RightHand";
            inputActions = new InputActionMap("SpaceCanvas " + usage);
            foreach (var control in new[] {"devicePosition", "deviceRotation", "pointerPosition", "pointerRotation",
                "pointer/isTracked", "pointer/trackingState", "isTracked", "trackingState", "triggerPressed", "trigger", "pinchValue", "select", "pointerActivated", "pointerActivateValue"})
                inputActions.AddAction(control, InputActionType.PassThrough, "<XRController>{" + usage + "}/" + control);
            inputActions.Enable();
        }

        private void Update()
        {
            var device = FindDevice();
            if (device == null) { LoseTracking(); return; }
            if (!TryReadAim(device, out var p, out var q)) { LoseTracking(); return; }
            if (device.deviceId != deviceId)
            {
                LoseTracking(); deviceId = device.deviceId;
                Debug.Log("[SpaceCanvas FullSpace] " + (leftHand ? "Left" : "Right") + " pointer acquired: " + device.layout);
            }
            transform.SetLocalPositionAndRotation(p, q);
            Tracked=true;
            rayInteractor.enabled = true;
            var pressed = ReadPress(device,lastPressed);
            if (!pressed) released = true;
            pressed &= released;

            var ray = new Ray(transform.position, transform.forward);
            var overUI = rayInteractor.TryGetCurrentUIRaycastResult(out var uiHit);
            var hitObject = SpaceCanvasPicking.Raycast(ray, 3f, out var hit) ? hit.item : null;
            if (overUI && hitObject != null && hit.distance < uiHit.distance) overUI = false;
#if DEVELOPMENT_BUILD
            if (pressed && !lastPressed)
                Debug.Log("[SpaceCanvas FullSpace] " + (leftHand ? "Left" : "Right")
                    + " press; ui=" + (overUI && uiHit.gameObject != null ? uiHit.gameObject.name : "none")
                    + "; object=" + (hitObject != null ? hitObject.name : "none"));
#endif
            var modal = session != null && session.projectUI != null && session.projectUI.ModalOpen;
            var handles = session != null ? session.Handles : null;
            var handleHit = default(SpaceCanvasTransformHandles.Hit);
            var overHandle = !modal && handles != null && handles.Raycast(ray, overUI ? uiHit.distance : 3f, out handleHit);
            if (overHandle) { handles.Hover(SpaceCanvasUnityCompatibility.ObjectId(this), handleHit.axis); overUI = false; }
            if (overUI && pressed && !lastPressed) SpaceCanvasPlacement.MarkInputSource(SpaceCanvasUnityCompatibility.ObjectId(this));
            var placement = session != null && session.workspaceUI != null ? session.workspaceUI.placement : null;
            var placing = placement != null && placement.IsPlacing;
            if (placing && !overUI && !modal && !placement.RayHitsInterface(ray))
            {
                placement.UpdateRay(SpaceCanvasUnityCompatibility.ObjectId(this), ray);
                if (pressed && !lastPressed && placement.Confirm(SpaceCanvasUnityCompatibility.ObjectId(this)))
                { ConsumeUIActivation(); pressed = false; }
            }
            if (!placing && pressed && !lastPressed && !overUI && !modal && overHandle)
                ownsHandle = handles.BeginDrag(SpaceCanvasUnityCompatibility.ObjectId(this), handleHit, ray, transform.rotation);
            if (ownsHandle && handles != null)
            {
                if (!pressed || modal) { handles.EndDrag(SpaceCanvasUnityCompatibility.ObjectId(this), modal); ownsHandle = false; }
                else { handles.Drag(SpaceCanvasUnityCompatibility.ObjectId(this), ray); ownsHandle = handles.IsDragging; }
            }
            if (!placing && !ownsHandle && !overHandle && pressed && !lastPressed && !overUI && !modal && hitObject != null && session != null)
            {
                ownsGesture = session.BeginGesture(hitObject, hit.point, transform.rotation);
                gestureDistance = hit.distance;
            }
            if (ownsGesture && session != null)
            {
                if (!pressed || modal) { session.EndGesture(modal); ownsGesture = false; }
                else session.UpdateGesture(ray.GetPoint(gestureDistance), transform.rotation);
            }
            if (!placing && pressed && !lastPressed && !overUI && !modal && !overHandle && hitObject == null
                && session != null && !session.IsManipulating) session.Select(null);
            // XRI performs the current-frame UI hit test itself. Gating the press on
            // last frame's hit can lose a short pinch when the pointer enters a button.
            DeliverUIPress(pressed && !ownsGesture && !ownsHandle && (handles == null || !handles.IsDragging));
            lastPressed = pressed;
        }

        private InputDevice FindDevice()
        {
            InputDevice candidate = null;
            var bestScore = -1;
            foreach (var device in InputSystem.devices)
            {
                var hasUsage = false;
                foreach (var usage in device.usages)
                    if (usage.ToString() == (leftHand ? "LeftHand" : "RightHand")) { hasUsage = true; break; }
                if (!hasUsage || !device.enabled || device.TryGetChildControl<Vector3Control>("devicePosition") == null
                    || device.TryGetChildControl<QuaternionControl>("deviceRotation") == null) continue;
                // Unity 6 XR Hands can publish an aim-capable XRHandDevice in addition
                // to the native OpenXR profile. Wrist tracking does not imply aim tracking.
                var trigger = device.TryGetChildControl<ButtonControl>("triggerPressed");
                var triggerValue = device.TryGetChildControl<AxisControl>("trigger");
                var pinch = device.TryGetChildControl<AxisControl>("pinchValue");
                var select = device.TryGetChildControl<ButtonControl>("select");
                var activation = device.TryGetChildControl<ButtonControl>("pointerActivated");
                if (trigger == null && triggerValue == null && pinch == null && select == null && activation == null) continue;
                var tracked = device.TryGetChildControl<ButtonControl>("isTracked");
                if (tracked != null && !tracked.isPressed) continue;
                if (!TryReadAim(device, out _, out _)) continue;
                var score = (trigger != null || triggerValue != null ? 8 : 2)
                    + (device is UnityEngine.InputSystem.XR.XRController ? 2 : 0)
                    + (device.deviceId == deviceId ? 1 : 0);
                if (score <= bestScore) continue;
                bestScore = score;
                candidate = device;
            }
            return candidate;
        }

        /// <summary>
        /// Read position and rotation from one pose. Never fall back to the wrist when
        /// an explicit aim pose exists but is lost: its reset identity rotation points
        /// at a completely different placement location. No dependency on XR Hands is needed.
        /// </summary>
        private static bool TryReadAim(InputDevice device, out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = Quaternion.identity;
            var aimPosition = device.TryGetChildControl<Vector3Control>("pointerPosition")
                ?? device.TryGetChildControl<Vector3Control>("aimPosition");
            var aimRotation = device.TryGetChildControl<QuaternionControl>("pointerRotation")
                ?? device.TryGetChildControl<QuaternionControl>("aimRotation");
            var hasAim = aimPosition != null || aimRotation != null;
            if (hasAim)
            {
                if (aimPosition == null || aimRotation == null) return false;
                var aimTracked = device.TryGetChildControl<ButtonControl>("pointer/isTracked");
                if (aimTracked != null && !aimTracked.isPressed) return false;
                var aimState = device.TryGetChildControl<IntegerControl>("aimTrackingState")
                    ?? device.TryGetChildControl<IntegerControl>("pointer/trackingState");
                // InputTrackingState.Position | InputTrackingState.Rotation.
                if (aimState != null && (aimState.ReadValue() & 3) != 3) return false;
            }
            var p = hasAim ? aimPosition : device.TryGetChildControl<Vector3Control>("devicePosition");
            var q = hasAim ? aimRotation : device.TryGetChildControl<QuaternionControl>("deviceRotation");
            if (p == null || q == null) return false;
            position = p.ReadValue(); rotation = q.ReadValue();
            return Finite(position) && Finite(rotation) && Quaternion.Dot(rotation, rotation) >= .5f;
        }

        private static bool ReadPress(InputDevice device, bool previous)
        {
            // The OpenXR hand profile exposes a native activation edge. Continuous pinch
            // strength must not keep UI held after that activation has ended.
            var activation = device.TryGetChildControl<ButtonControl>("pointerActivated");
            if(activation!=null) return activation.isPressed;
            var trigger = device.TryGetChildControl<ButtonControl>("triggerPressed");
            var triggerValue = device.TryGetChildControl<AxisControl>("trigger");
            var pinch = device.TryGetChildControl<AxisControl>("pinchValue");
            var select = device.TryGetChildControl<ButtonControl>("select");
            var threshold = previous ? .55f : .75f;
            return (trigger != null && trigger.isPressed) || (triggerValue != null && triggerValue.ReadValue() >= threshold)
                || (pinch != null && pinch.ReadValue() >= threshold)
                || (select != null && select.isPressed);
        }

        private void DeliverUIPress(bool pressed)
        {
            if (uiPress == null) return;
            uiPress.manualPerformed = pressed; uiPress.manualValue = pressed ? 1f : 0f;
            if (pressed && !uiDelivered) uiPress.manualFramePerformed = Time.frameCount;
            if (!pressed && uiDelivered) uiPress.manualFrameCompleted = Time.frameCount;
            uiDelivered = pressed;
        }
        private void LoseTracking()
        {
            Tracked=false;
            if (ownsHandle && session != null && session.Handles != null) session.Handles.EndDrag(SpaceCanvasUnityCompatibility.ObjectId(this), true);
            ownsHandle = false;
            if (ownsGesture && session != null) session.EndGesture(true);
            ownsGesture = false; released = false; lastPressed = false; deviceId = -1;
            DeliverUIPress(false);
            if (rayInteractor != null) rayInteractor.enabled = false;
        }
        private void OnDisable()
        {
            pointers.Remove(this);
            LoseTracking();
            if (inputActions != null) { inputActions.Disable(); inputActions.Dispose(); inputActions = null; }
        }
        private void OnApplicationFocus(bool focus) { if (!focus) LoseTracking(); }
        private static bool Finite(Vector3 v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }
        private static bool Finite(Quaternion q) { return Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w); }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
