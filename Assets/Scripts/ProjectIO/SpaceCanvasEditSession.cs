using System;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    public enum SpaceCanvasEditTool { Move, Rotate, Scale }
    public enum SpaceCanvasEditAxis { Free, X, Y, Z }

    /// <summary>
    /// MR 与桌面调试共用的单指针编辑事务。一次完整手势只产生一个撤销快照。
    /// 指针以 Unity 世界坐标传入；文件始终记录物体相对于父节点的局部米制坐标。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasEditSession : MonoBehaviour
    {
        public SpaceCanvasWorkspaceUI workspaceUI;
        public SpaceCanvasProjectIOUI projectUI;
        public SpaceCanvasEditTool tool;
        public bool snapToGrid;
        public SpaceCanvasEditAxis axis;
        [Range(1f, 90f)] public float rotationStep = 15f;
        [Min(.001f)] public float snapSize = .25f;
        public SpaceCanvasEditableObject Selected { get; private set; }
        public bool IsManipulating { get; private set; }
        public SpaceCanvasTransformHandles Handles { get; private set; }
        private Vector3 pointerStart, startPosition, startLocalPosition, startScale;
        private Quaternion pointerRotation, startRotation;
        private SpaceCanvasProject before;
        private bool changed;
        public event System.Action<SpaceCanvasEditableObject> EditCommitted;

        private void Awake()
        {
            Handles = GetComponent<SpaceCanvasTransformHandles>();
            if (Handles == null) Handles = gameObject.AddComponent<SpaceCanvasTransformHandles>();
            Handles.session = this;
        }

        public void Select(SpaceCanvasEditableObject item)
        {
            if (IsManipulating) EndGesture(false);
            Selected = IsEditable(item) ? item : null;
            if (workspaceUI != null) workspaceUI.SelectObject(Selected);
        }

        public bool BeginGesture(SpaceCanvasEditableObject item, Vector3 pointer, Quaternion rotation)
        {
            if (IsManipulating || !IsEditable(item) || !Finite(pointer) || !Finite(rotation)) return false;
            workspaceUI?.FinishPropertyEdit();
            Select(item);
            if (!SceneExporter.TryCaptureCurrentScene(null, out before, out var error))
            { Notify("无法开始编辑：" + error, false); return false; }
            pointerStart = pointer; pointerRotation = rotation;
            startPosition = item.transform.position; startRotation = item.transform.rotation;
            startLocalPosition = item.transform.localPosition;
            startScale = item.transform.localScale;
            changed = false; IsManipulating = true;
            return true;
        }

        public void UpdateGesture(Vector3 pointer, Quaternion rotation)
        {
            if (!IsManipulating || !Finite(pointer) || !Finite(rotation)) return;
            if (!IsEditable(Selected)) { ResetGesture(); return; }
            var target = Selected.transform;
            switch (tool)
            {
                case SpaceCanvasEditTool.Move:
                    var delta = Vector3.ClampMagnitude(pointer - pointerStart, 1f);
                    var parent = target.parent;
                    var localDelta = parent != null ? parent.InverseTransformVector(delta) : delta;
                    localDelta = Constrain(localDelta, axis);
                    // Assign in parent coordinates so untouched axes do not drift through repeated matrix conversions.
                    target.localPosition = startLocalPosition + localDelta;
                    if (snapToGrid && snapSize > 0)
                    {
                        var p = target.localPosition;
                        target.localPosition = new Vector3(axis == SpaceCanvasEditAxis.Free || axis == SpaceCanvasEditAxis.X ? Snap(p.x) : p.x,
                            axis == SpaceCanvasEditAxis.Free || axis == SpaceCanvasEditAxis.Y ? Snap(p.y) : p.y,
                            axis == SpaceCanvasEditAxis.Free || axis == SpaceCanvasEditAxis.Z ? Snap(p.z) : p.z);
                    }
                    break;
                case SpaceCanvasEditTool.Rotate:
                    var turn = rotation * Quaternion.Inverse(pointerRotation);
                    if (axis != SpaceCanvasEditAxis.Free)
                    {
                        var localAxis = axis == SpaceCanvasEditAxis.X ? Vector3.right : axis == SpaceCanvasEditAxis.Y ? Vector3.up : Vector3.forward;
                        var worldAxis = target.parent != null ? target.parent.TransformDirection(localAxis).normalized : localAxis;
                        var projected = Vector3.Dot(new Vector3(turn.x, turn.y, turn.z), worldAxis);
                        var angle = Mathf.DeltaAngle(0, 2f * Mathf.Atan2(projected, turn.w) * Mathf.Rad2Deg);
                        if (snapToGrid) angle = Mathf.Round(angle / Mathf.Clamp(rotationStep, 1, 90)) * Mathf.Clamp(rotationStep, 1, 90);
                        turn = Quaternion.AngleAxis(angle, worldAxis);
                    }
                    else if (snapToGrid)
                    {
                        turn.ToAngleAxis(out var angle, out var direction);
                        var step = Mathf.Clamp(rotationStep, 1, 90);
                        turn = Quaternion.AngleAxis(Mathf.Round(angle / step) * step, direction);
                    }
                    target.rotation = turn * startRotation;
                    break;
                case SpaceCanvasEditTool.Scale:
                    var distance = Mathf.Max(.05f, Vector3.Distance(pointerStart, startPosition));
                    var ratio = Mathf.Clamp(Vector3.Distance(pointer, startPosition) / distance, .1f, 10f);
                    if (snapToGrid) ratio = Mathf.Max(.1f, Mathf.Round(ratio * 10f) / 10f);
                    var min = Mathf.Min(Mathf.Abs(startScale.x), Mathf.Min(Mathf.Abs(startScale.y), Mathf.Abs(startScale.z)));
                    var max = Mathf.Max(Mathf.Abs(startScale.x), Mathf.Max(Mathf.Abs(startScale.y), Mathf.Abs(startScale.z)));
                    if (min > .000001f && max > .000001f) ratio = Mathf.Clamp(ratio, .01f / min, 100f / max);
                    target.localScale = startScale * ratio;
                    break;
            }
            changed = (target.position - startPosition).sqrMagnitude > .00000001f
                || Quaternion.Angle(target.rotation, startRotation) > .05f
                || (target.localScale - startScale).sqrMagnitude > .00000001f;
        }

        public void EndGesture(bool cancel)
        {
            if (!IsManipulating) return;
            SpaceCanvasEditableObject committed = null;
            if (IsEditable(Selected))
            {
                if (cancel) Selected.transform.SetPositionAndRotation(startPosition, startRotation);
                if (cancel) Selected.transform.localScale = startScale;
                else if (changed && before != null) { SpaceCanvasUndoService.PushSnapshot(before); committed = Selected; }
            }
            ResetGesture();
            if (workspaceUI != null) workspaceUI.RefreshSelection();
            if (committed != null) EditCommitted?.Invoke(committed);
        }

        public void SetTool(SpaceCanvasEditTool value)
        {
            workspaceUI?.placement?.Cancel();
            EndGesture(false); tool = value;
            workspaceUI?.SetPrecisionMode(value);
            Notify(value == SpaceCanvasEditTool.Move ? "移动：拖动彩色箭头，或直接拖动物体" : value == SpaceCanvasEditTool.Rotate
                ? "旋转：拖动彩色圆环，或捏住物体转动手腕" : "缩放：拖动方形手柄，保持模型比例", true);
        }

        private float Snap(float value) { return Mathf.Round(value / snapSize) * snapSize; }
        public static Vector3 Constrain(Vector3 delta, SpaceCanvasEditAxis constraint)
        { return constraint == SpaceCanvasEditAxis.Free ? delta : constraint == SpaceCanvasEditAxis.X ? new Vector3(delta.x, 0, 0)
            : constraint == SpaceCanvasEditAxis.Y ? new Vector3(0, delta.y, 0) : new Vector3(0, 0, delta.z); }
        private static bool IsEditable(SpaceCanvasEditableObject item)
        { return item != null && item.gameObject.activeInHierarchy && (SpaceCanvasRuntime.ContentRoot == null || item.transform.IsChildOf(SpaceCanvasRuntime.ContentRoot)); }
        private static bool Finite(Vector3 v) { return Number(v.x) && Number(v.y) && Number(v.z); }
        private static bool Finite(Quaternion q) { return Number(q.x) && Number(q.y) && Number(q.z) && Number(q.w) && q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w > .000001f; }
        private static bool Number(float n) { return !float.IsNaN(n) && !float.IsInfinity(n); }
        private void ResetGesture() { IsManipulating = false; before = null; changed = false; }
        private void Notify(string message, bool success) { if (projectUI != null) projectUI.ShowFeedback(message, success); }
        private void OnDisable() { EndGesture(true); }
    }
}
