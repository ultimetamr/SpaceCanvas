using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// Runtime move arrows, rotation rings and uniform-scale grips. Geometry stays outside ContentRoot,
    /// never participates in physics/export, and uses the same EditSession undo transaction as free dragging.
    /// A pointer owns a drag until release; tracking loss, focus loss or a modal cancels it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasTransformHandles : MonoBehaviour
    {
        public struct Hit { public SpaceCanvasEditAxis axis; public Vector3 point; public float distance; }
        public SpaceCanvasEditSession session;
        public bool IsDragging => owner != 0;
        public string Measurement { get; private set; } = "";
        private readonly LineRenderer[] lines = new LineRenderer[3];
        private readonly Vector3[] directions = new Vector3[3];
        private readonly Vector3[] ring = new Vector3[65];
        private readonly Material[] materials = new Material[3];
        private readonly int[] hoverOwners = new int[3];
        private readonly int[] hoverFrames = new int[3];
        private Vector3 center, dragCenter, dragAxis, startDirection, gesturePointer;
        private Vector3 startLocalPosition, startLocalScale;
        private float radius, dragRadius, startParameter;
        private int owner, activeAxis;
        private Quaternion pointerRotation;
        private SpaceCanvasEditTool dragTool;
        private SpaceCanvasEditAxis previousAxis;
        private SpaceCanvasEditableObject frameItem;
        private TMPro.TextMeshPro measurementLabel;

        public bool Available => isActiveAndEnabled && session != null && session.Selected != null
            && session.Selected.gameObject.activeInHierarchy
            && (SpaceCanvasRuntime.ContentRoot == null || session.Selected.transform.IsChildOf(SpaceCanvasRuntime.ContentRoot))
            && (session.projectUI == null || !session.projectUI.ModalOpen)
            && (session.workspaceUI == null || session.workspaceUI.placement == null || !session.workspaceUI.placement.IsPlacing)
            && (!session.IsManipulating || IsDragging);

        private void Awake() { InitializeView(); }
        private void Start()
        {
            if (Application.isPlaying) Debug.Log("[SpaceCanvas Handles] Ready: move arrows, rotation rings, uniform-scale grips.");
        }

        /// <summary>Also usable by the Editor preview renderer; repeated calls reuse the same view.</summary>
        public void InitializeView()
        {
            if (lines[0] != null) return;
            var template = Resources.Load<Material>("SpaceCanvasMaterials/OpenXRPointer");
            for (var i = 0; i < 3; i++)
            {
                var go = new GameObject("Interactive " + (SpaceCanvasEditAxis)(i + 1)); go.transform.SetParent(transform, false);
                var line = lines[i] = go.AddComponent<LineRenderer>(); line.useWorldSpace = true;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
                if (template != null) { materials[i] = new Material(template); line.sharedMaterial = materials[i]; }
                line.enabled = false;
            }
            var label = new GameObject("Handle measurement"); label.transform.SetParent(transform, false);
            measurementLabel = label.AddComponent<TMPro.TextMeshPro>(); measurementLabel.font = SpaceCanvasTypography.Font;
            measurementLabel.fontSize = 2.2f; measurementLabel.alignment = TMPro.TextAlignmentOptions.Center;
            measurementLabel.color = SpaceCanvasPresentationStyle.Ink; measurementLabel.raycastTarget = false;
            measurementLabel.rectTransform.sizeDelta = new Vector2(.44f, .065f);
            measurementLabel.gameObject.SetActive(false);
        }

        private void UpdateFrame()
        {
            if (IsDragging || session == null || session.Selected == null) return;
            var item = session.Selected; frameItem = item;
            var bounds = item.PickingBounds;
            center = item.transform.TransformPoint(bounds.center);
            var size = Vector3.Scale(bounds.extents, item.transform.lossyScale);
            radius = Mathf.Clamp(size.magnitude + .025f, .075f, .22f);
            for (var i = 0; i < 3; i++)
            {
                var axis = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
                directions[i] = item.transform.parent == null ? axis : session.tool == SpaceCanvasEditTool.Move
                    ? item.transform.parent.TransformVector(axis).normalized : item.transform.parent.TransformDirection(axis).normalized;
            }
        }

        public bool Raycast(Ray ray, float maxDistance, out Hit hit)
        {
            hit = default;
            if (!Available || IsDragging || session.IsManipulating || !SpaceCanvasHandleMath.Finite(ray.origin)
                || !SpaceCanvasHandleMath.Finite(ray.direction) || ray.direction.sqrMagnitude < .5f) return false;
            UpdateFrame();
            var bestError = float.PositiveInfinity; var found = false;
            for (var axis = 0; axis < 3; axis++)
            {
                var count = BuildShape(axis);
                for (var j = 1; j < count; j++)
                {
                    var error = SpaceCanvasHandleMath.RaySegment(ray, ring[j - 1], ring[j], out var distance, out var point);
                    if (distance > maxDistance || error > .009f || error >= bestError) continue;
                    // A selected model may cover its own handles. Other geometry still occludes them.
                    if (SpaceCanvasPicking.Raycast(ray, Mathf.Max(0, distance - .012f), out var blocker)
                        && blocker.item != session.Selected) continue;
                    hit = new Hit { axis = (SpaceCanvasEditAxis)(axis + 1), point = point, distance = distance };
                    bestError = error; found = true;
                }
            }
            return found;
        }

        public void Hover(int source, SpaceCanvasEditAxis axis)
        { var i = (int)axis - 1; if (i >= 0 && i < 3) { hoverOwners[i] = source; hoverFrames[i] = Time.frameCount; } }

        public bool BeginDrag(int source, Hit hit, Ray ray, Quaternion rotation)
        {
            if (source == 0 || !Available || IsDragging || session.IsManipulating || hit.axis == SpaceCanvasEditAxis.Free) return false;
            UpdateFrame(); activeAxis = (int)hit.axis - 1;
            if (activeAxis < 0 || activeAxis > 2) return false;
            dragCenter = center; dragRadius = radius; dragAxis = directions[activeAxis]; dragTool = session.tool;
            if (dragTool == SpaceCanvasEditTool.Rotate)
            { if (!SpaceCanvasHandleMath.PlaneDirection(ray, dragCenter, dragAxis, out startDirection)) return false; }
            else if (!SpaceCanvasHandleMath.AxisParameter(ray, dragCenter, dragAxis, out startParameter)) return false;
            previousAxis = session.axis; session.axis = hit.axis; pointerRotation = rotation;
            // Scale uses an artificial point measured from the object's pivot, preserving the existing uniform-scale contract.
            gesturePointer = dragTool == SpaceCanvasEditTool.Scale ? session.Selected.transform.position + dragAxis * dragRadius : hit.point;
            if (!session.BeginGesture(session.Selected, gesturePointer, rotation)) { session.axis = previousAxis; return false; }
            startLocalPosition = session.Selected.transform.localPosition; startLocalScale = session.Selected.transform.localScale;
            owner = source; Measurement = "";
#if DEVELOPMENT_BUILD
            Debug.Log("[SpaceCanvas Handles] Begin " + dragTool + " / " + hit.axis + " / " + session.Selected.AssetId);
#endif
            return true;
        }

        public void Drag(int source, Ray ray)
        {
            if (owner != source || !IsDragging) return;
            if (!Available || session.Selected != frameItem || !session.IsManipulating || session.tool != dragTool)
            { EndDrag(source, true); return; }
            if (dragTool == SpaceCanvasEditTool.Rotate)
            {
                if (!SpaceCanvasHandleMath.PlaneDirection(ray, dragCenter, dragAxis, out var direction)) return;
                var angle = Vector3.SignedAngle(startDirection, direction, dragAxis);
                session.UpdateGesture(gesturePointer, Quaternion.AngleAxis(angle, dragAxis) * pointerRotation);
                var shown = session.snapToGrid ? Mathf.Round(angle / session.rotationStep) * session.rotationStep : angle;
                Measurement = hitName() + "  " + shown.ToString("0") + "°";
            }
            else
            {
                if (!SpaceCanvasHandleMath.AxisParameter(ray, dragCenter, dragAxis, out var parameter)) return;
                var delta = Mathf.Clamp(parameter - startParameter, -1f, 1f);
                if (dragTool == SpaceCanvasEditTool.Move)
                {
                    session.UpdateGesture(gesturePointer + dragAxis * delta, pointerRotation);
                    var difference = session.Selected.transform.localPosition - startLocalPosition;
                    Measurement = hitName() + "  " + difference[activeAxis].ToString("+0.00;-0.00;0.00") + " m";
                }
                else
                {
                    var ratio = Mathf.Clamp(1 + delta / dragRadius, .1f, 10f);
                    session.UpdateGesture(gesturePointer + dragAxis * dragRadius * (ratio - 1), pointerRotation);
                    Measurement = "等比缩放  ×" + (session.Selected.transform.localScale.magnitude / Mathf.Max(.00001f, startLocalScale.magnitude)).ToString("0.00");
                }
            }
        }
        private string hitName() => ((SpaceCanvasEditAxis)(activeAxis + 1)).ToString();

        public void EndDrag(int source, bool cancel)
        {
            if (!IsDragging || owner != source) return;
            owner = 0;
            if (session != null) { session.EndGesture(cancel); session.axis = previousAxis; session.workspaceUI?.RefreshSelection(); }
#if DEVELOPMENT_BUILD
            Debug.Log("[SpaceCanvas Handles] " + (cancel ? "Cancelled" : "Released"));
#endif
            Measurement = "";
        }
        public void CancelDrag() { if (IsDragging) EndDrag(owner, true); }

        private int BuildShape(int axis)
        {
            var direction = directions[axis];
            var tangent = Vector3.Cross(direction, Mathf.Abs(direction.y) < .8f ? Vector3.up : Vector3.right).normalized;
            if (session.tool == SpaceCanvasEditTool.Rotate)
            {
                var bitangent = Vector3.Cross(direction, tangent);
                for (var i = 0; i < ring.Length; i++)
                { var a = i * Mathf.PI * 2 / (ring.Length - 1); ring[i] = center + (tangent * Mathf.Cos(a) + bitangent * Mathf.Sin(a)) * radius; }
                return ring.Length;
            }
            var tip = center + direction * radius;
            ring[0] = center + direction * radius * .3f; ring[1] = tip;
            if (session.tool == SpaceCanvasEditTool.Move)
            { ring[2] = tip - direction * .018f + tangent * .01f; ring[3] = tip; ring[4] = tip - direction * .018f - tangent * .01f; return 5; }
            ring[2] = tip + tangent * .009f; ring[3] = ring[2] + direction * .018f;
            ring[4] = ring[3] - tangent * .018f; ring[5] = ring[4] - direction * .018f; ring[6] = ring[2]; return 7;
        }

        private void LateUpdate() { RefreshView(); }
        public void RefreshView()
        {
            if (lines[0] == null) return;
            if (IsDragging && (!Available || !session.IsManipulating || session.Selected != frameItem)) EndDrag(owner, true);
            var visible = Available;
            for (var i = 0; i < 3; i++) if (lines[i] != null) lines[i].enabled = visible;
            if (measurementLabel != null) measurementLabel.gameObject.SetActive(visible && IsDragging && Measurement.Length > 0);
            if (!visible) return;
            UpdateFrame();
            if (IsDragging) center = session.Selected.transform.TransformPoint(session.Selected.PickingBounds.center);
            for (var i = 0; i < 3; i++)
            {
                var count = BuildShape(i); var line = lines[i]; if (line == null) continue;
                line.positionCount = count; for (var j = 0; j < count; j++) line.SetPosition(j, ring[j]);
                var highlighted = IsDragging ? activeAxis == i : hoverOwners[i] != 0 && hoverFrames[i] >= Time.frameCount - 1;
                var color = highlighted ? SpaceCanvasPresentationStyle.Accent : i == 0 ? new Color(.95f,.34f,.29f) : i == 1 ? new Color(.38f,.88f,.5f) : new Color(.35f,.64f,1);
                line.startColor = line.endColor = color; line.widthMultiplier = highlighted ? .004f : .0025f;
            }
            if (measurementLabel != null && measurementLabel.gameObject.activeSelf)
            {
                var camera = session.workspaceUI != null ? session.workspaceUI.viewCamera : Camera.main;
                measurementLabel.transform.position = center + Vector3.up * (radius + .04f);
                measurementLabel.transform.localScale = Vector3.one / Mathf.Max(.001f, transform.lossyScale.x);
                if (camera != null) measurementLabel.transform.rotation = camera.transform.rotation;
                measurementLabel.text = Measurement;
            }
        }
        private void OnDisable() { EndDrag(owner, true); foreach (var line in lines) if (line != null) line.enabled = false; if (measurementLabel != null) measurementLabel.gameObject.SetActive(false); }
        private void OnApplicationFocus(bool focus) { if (!focus) EndDrag(owner, true); }
        private void OnDestroy() { foreach (var material in materials) if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); } }
    }
}
