using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Management;
#if ENABLE_PICO_OPENXR_SDK
using ByteDance.PICO.OpenXR;
#endif

namespace SpaceCanvas.ProjectIO.XR
{
    /// <summary>
    /// Full Space MR entry: Unity/OpenXR renders the scene; PICO passthrough supplies reality.
    /// This component creates no system window and never enables SpatialAdapter.
    /// Place the workbench once after valid head tracking, rather than parenting it to the head.
    /// </summary>
    public sealed class SpaceCanvasFullSpaceRuntime : MonoBehaviour
    {
        public Camera head;
        public Transform workbench;
        public SpaceCanvasWorkspaceSettingsUI comfort;
        private bool placed;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private float targetScale = 1;
        private bool transitioning;
        private UnityEngine.InputSystem.InputActionMap headTrackingActions;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule diagnosticModule;
        private void ObserveUIDown(GameObject target, UnityEngine.EventSystems.PointerEventData data)
            { if (target != null) Debug.Log("[SpaceCanvas FullSpace] UI down=" + target.name); }
        private void ObserveUIUp(GameObject target, UnityEngine.EventSystems.PointerEventData data)
            { if (target != null) Debug.Log("[SpaceCanvas FullSpace] UI up=" + target.name); }
        private void ObserveUIClick(GameObject target, UnityEngine.EventSystems.PointerEventData data)
            { if (target != null) Debug.Log("[SpaceCanvas FullSpace] UI click=" + target.name); }
#endif

        private void OnEnable()
        {
            if (comfort != null) comfort.Applied += RecenterWorkbench;
            headTrackingActions = new UnityEngine.InputSystem.InputActionMap("SpaceCanvas Head Tracking");
            headTrackingActions.AddAction("Tracked", UnityEngine.InputSystem.InputActionType.PassThrough, "<XRHMD>/isTracked");
            headTrackingActions.AddAction("TrackingState", UnityEngine.InputSystem.InputActionType.PassThrough, "<XRHMD>/trackingState");
            headTrackingActions.Enable();
        }

        private void OnDisable()
        {
            if (comfort != null) comfort.Applied -= RecenterWorkbench;
            if (headTrackingActions != null) { headTrackingActions.Disable(); headTrackingActions.Dispose(); headTrackingActions = null; }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (diagnosticModule != null)
            {
                diagnosticModule.pointerDown -= ObserveUIDown;
                diagnosticModule.pointerUp -= ObserveUIUp;
                diagnosticModule.pointerClick -= ObserveUIClick;
            }
#endif
        }

        private void Awake()
        {
            // Use the project's mobile quality preset before its first XR frame.
            // World-space UI needs multisampling to stabilize thin edges during head motion.
            // Keep HDR disabled so passthrough alpha and the mobile bandwidth budget are preserved.
            if (Application.platform == RuntimePlatform.Android) QualitySettings.SetQualityLevel(0, true);
            if (head != null) { head.allowHDR = false; head.allowMSAA = true; }
        }

        private IEnumerator Start()
        {
            try { Debug.Log("[SpaceCanvas Capability]\n" + PicoSpatialCapabilities.Current.ToHumanText()); }
            catch (System.Exception ex) { Debug.LogWarning("[SpaceCanvas Capability] 检测失败：" + ex.Message); }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            diagnosticModule = FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
            if (diagnosticModule != null)
            {
                diagnosticModule.pointerDown += ObserveUIDown;
                diagnosticModule.pointerUp += ObserveUIUp;
                diagnosticModule.pointerClick += ObserveUIClick;
            }
#endif
            Debug.Log("[SpaceCanvas FullSpace] Scene started; editableObjects=" + SpaceCanvasRuntime.GetEditableObjects().Count);
            SpaceCanvasUIClarity.Configure(workbench);
            var deadline = Time.realtimeSinceStartup + 12f;
            var passthrough = false;
            while (Time.realtimeSinceStartup < deadline)
            {
#if ENABLE_PICO_OPENXR_SDK && UNITY_ANDROID && !UNITY_EDITOR
                if (!passthrough && PassthroughFeature.isExtensionEnable)
                {
                    PassthroughFeature.EnableVideoSeeThrough = true;
                    passthrough = true;
                    Debug.Log("[SpaceCanvas FullSpace] Passthrough enabled.");
                }
#else
                passthrough = true;
#endif
                if (!placed && HeadTracked()) RecenterWorkbench();
                if (placed && passthrough) break;
                yield return null;
            }
            var manager = XRGeneralSettings.Instance == null ? null : XRGeneralSettings.Instance.Manager;
            Debug.Log("[SpaceCanvas FullSpace] loader=" + (manager == null || manager.activeLoader == null ? "none" : manager.activeLoader.name)
                + "; tracked=" + placed + "; passthrough=" + passthrough);
            if (!placed) Debug.LogWarning("[SpaceCanvas FullSpace] No valid head pose; retaining the saved visible workbench pose.");
            if (!passthrough) Debug.LogWarning("[SpaceCanvas FullSpace] Passthrough extension unavailable. Check PICO OpenXR features/runtime.");
#if DEVELOPMENT_BUILD
            foreach (var input in UnityEngine.InputSystem.InputSystem.devices)
                Debug.Log("[SpaceCanvas FullSpace] input=" + input.layout + "; usages=" + string.Join(",", input.usages)
                    + "; tracked=" + input.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("isTracked")?.isPressed);
#endif
        }

        private void LateUpdate()
        {
            // A guardian dialog or unworn headset may postpone tracking past startup.
            // Keep recovering until the first valid pose, then leave the workbench anchored.
            if (!placed && HeadTracked()) RecenterWorkbench();
            if (transitioning && workbench != null)
            {
                var blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 7f);
                workbench.position = Vector3.Lerp(workbench.position, targetPosition, blend);
                workbench.rotation = Quaternion.Slerp(workbench.rotation, targetRotation, blend);
                workbench.localScale = Vector3.Lerp(workbench.localScale, Vector3.one * targetScale, blend);
                if (Vector3.Distance(workbench.position, targetPosition) < .001f && Quaternion.Angle(workbench.rotation, targetRotation) < .1f
                    && Mathf.Abs(workbench.localScale.x - targetScale) < .001f)
                { workbench.SetPositionAndRotation(targetPosition, targetRotation); workbench.localScale = Vector3.one * targetScale; transitioning = false; }
            }
        }

        private static bool HeadTracked()
        {
#if UNITY_EDITOR
            return false; // Keep the authoring camera and saved placement in desktop preview.
#else
            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out var tracked) && tracked) return true;
            // Use the same Input System source as TrackedPoseDriver if the legacy XRNode
            // mapping is not published by the OpenXR runtime.
            foreach (var input in UnityEngine.InputSystem.InputSystem.devices)
                if (input is UnityEngine.InputSystem.XR.XRHMD hmd && hmd.isTracked.isPressed) return true;
            return false;
#endif
        }

        /// <summary>Can be connected to a host recenter button. Never called every frame.</summary>
        public void RecenterWorkbench()
        {
            if (head == null || workbench == null) return;
            var forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            targetPosition = head.transform.position + forward * (comfort != null ? comfort.Distance : 1.5f) + Vector3.up * (comfort != null ? comfort.Height : -.2f);
            targetRotation = Quaternion.LookRotation(forward, Vector3.up); targetScale = comfort != null ? comfort.Scale : 1;
            if (!placed) { workbench.SetPositionAndRotation(targetPosition, targetRotation); workbench.localScale = Vector3.one * targetScale; }
            else transitioning = true;
            placed = true;
            Debug.Log("[SpaceCanvas FullSpace] Workbench placed from tracked head pose.");
        }

        private void OnApplicationPause(bool paused)
        {
#if ENABLE_PICO_OPENXR_SDK && UNITY_ANDROID && !UNITY_EDITOR
            if (PassthroughFeature.isExtensionEnable) PassthroughFeature.EnableVideoSeeThrough = !paused;
#endif
        }
    }
}
