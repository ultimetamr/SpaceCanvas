using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>Non-destructive placement preview. Preview lives outside ContentRoot and is never serialized.</summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasPlacement : MonoBehaviour
    {
        public SpaceCanvasWorkspaceUI workspace;
        public SpaceCanvasProjectIOUI projectUI;
        [SerializeField] private GameObject panel;
        [SerializeField] private TextMeshProUGUI label;
        private GameObject ghostRoot;
        private SpaceCanvasEditableObject ghost;
        private Bounds bounds;
        private string assetId;
        private Vector3 localPosition;
        private int owner;
        private static int preferredPointer;
        private float lastRay;
        private readonly RaycastHit[] hits = new RaycastHit[64];
        private Canvas[] interfaceCanvases;
        private readonly System.Collections.Generic.List<Material> previewMaterials = new System.Collections.Generic.List<Material>();
        public bool IsPlacing => ghost != null;
        public bool HasValidPose { get; private set; }
        public event Action ViewChanged;
        public static void MarkInputSource(int pointerId) { preferredPointer = pointerId; }

        private void Start()
        {
            if (panel != null)
            {
                var button = panel.GetComponentInChildren<Button>(true);
                button.onClick.RemoveAllListeners(); button.onClick.AddListener(Cancel);
                panel.SetActive(false);
            }
        }

        public void Begin(string id)
        {
            Cancel();
            try
            {
                workspace.FinishPropertyEdit();
                if (workspace.editSession != null) workspace.editSession.EndGesture(false);
                assetId = id;
                var canvases = new System.Collections.Generic.List<Canvas>();
                if (workspace.ViewRoot != null) canvases.AddRange(workspace.ViewRoot.GetComponentsInChildren<Canvas>(true));
                if (projectUI != null && projectUI.ViewRoot != null) canvases.AddRange(projectUI.ViewRoot.GetComponentsInChildren<Canvas>(true));
                interfaceCanvases = canvases.ToArray();
                ghostRoot = new GameObject("Placement preview (not saved)");
                var parent = ProjectParent;
                if (parent != null)
                {
                    ghostRoot.transform.SetPositionAndRotation(parent.position, parent.rotation);
                    ghostRoot.transform.localScale = parent.lossyScale;
                }
                ghost = SpaceCanvasAssetLibrary.CreateInstance(id, ghostRoot.transform, out _).GetComponent<SpaceCanvasEditableObject>();
                var data = ghost.ToSceneObject(); data.alpha = .38f; data.colliderEnabled = false;
                ghost.Apply(data);
                ApplyPreviewMaterials();
                foreach (var child in ghost.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
                bounds = SpaceCanvasGeometry.LocalBounds(ghost);
                ghost.gameObject.SetActive(false);
                owner = preferredPointer; lastRay = Time.unscaledTime;
                if (panel != null) panel.SetActive(true);
                if (label != null) label.text = "放置  " + SpaceCanvasBuiltinAssets.DisplayName(id) + "  ·  对准落点，再按一次扳机 / 捏合";
                workspace.RefreshChrome(); ViewChanged?.Invoke();
                Trace("Begin asset=" + id + "; pointer=" + owner + "; alpha=0.38");
            }
            catch (Exception ex) { Cancel(); if (projectUI != null) projectUI.ShowFeedback("无法预览素材：" + ex.Message, false); }
        }

        private Transform ProjectParent => SpaceCanvasRuntime.CurrentProjectRoot != null
            ? SpaceCanvasRuntime.CurrentProjectRoot.transform : SpaceCanvasRuntime.ContentRoot;

        private void ApplyPreviewMaterials()
        {
            var template = Resources.Load<Material>("SpaceCanvasMaterials/PlacementPreview");
            if (template == null) return; // Portable imports without this optional resource retain Apply(alpha).
            foreach (var renderer in ghost.GetComponentsInChildren<Renderer>(true))
            {
                var source = renderer.sharedMaterials;
                var replacement = new Material[source.Length];
                for (var i = 0; i < source.Length; i++)
                {
                    var material = new Material(template);
                    var original = source[i];
                    var color = original != null && original.HasProperty("_BaseColor")
                        ? original.GetColor("_BaseColor") : original != null && original.HasProperty("_Color") ? original.color : Color.white;
                    color.a = .38f;
                    material.SetColor("_BaseColor", color);
                    if (original != null && original.HasProperty("_BaseMap"))
                    {
                        material.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
                        material.SetTextureScale("_BaseMap", original.GetTextureScale("_BaseMap"));
                        material.SetTextureOffset("_BaseMap", original.GetTextureOffset("_BaseMap"));
                    }
                    replacement[i] = material;
                    previewMaterials.Add(material);
                }
                renderer.sharedMaterials = replacement;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        /// <summary>Current-ray panel test closes the one-frame gap before XRI publishes its next UI hit.</summary>
        public bool RayHitsInterface(Ray ray)
        {
            if (interfaceCanvases == null) return false;
            foreach (var canvas in interfaceCanvases)
            {
                if (canvas == null || !canvas.gameObject.activeInHierarchy) continue;
                var raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster == null || !raycaster.enabled) continue;
                var group = canvas.GetComponent<CanvasGroup>();
                if (group != null && !group.blocksRaycasts) continue;
                var rect = (RectTransform)canvas.transform;
                if (new Plane(rect.forward, rect.position).Raycast(ray, out var distance) && distance > 0 && distance < 3f
                    && rect.rect.Contains(rect.InverseTransformPoint(ray.GetPoint(distance)))) return true;
            }
            return false;
        }

        /// <summary>Lease one tracked pointer so left and right rays cannot move the preview in the same frame.</summary>
        public bool UpdateRay(int pointerId, Ray ray)
        {
            if (!IsPlacing || (owner != 0 && owner != pointerId && Time.unscaledTime - lastRay < .3f)) return false;
            var parent = ProjectParent;
            if (parent == null) return false;
            var valid = new Plane(parent.up, parent.position).Raycast(ray, out var distance) && distance > 0 && distance <= 3f;
            var point = valid ? ray.GetPoint(distance) : Vector3.zero;
            var count = Physics.RaycastNonAlloc(ray, hits, 3f, ~0, QueryTriggerInteraction.Ignore);
            var candidates = hits;
            // NonAlloc does not promise the closest hits when its buffer fills.
            if (count == hits.Length) { candidates = Physics.RaycastAll(ray, 3f, ~0, QueryTriggerInteraction.Ignore); count = candidates.Length; }
            var closest = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var hit = candidates[i];
                var editable = hit.collider.GetComponentInParent<SpaceCanvasEditableObject>();
                if (editable == null || !editable.transform.IsChildOf(SpaceCanvasRuntime.ContentRoot)
                    || Vector3.Dot(hit.normal, parent.up) < .8f || hit.distance >= closest) continue;
                point = hit.point; closest = hit.distance; valid = true;
            }
            var localContact = parent.InverseTransformPoint(point);
            valid &= Mathf.Abs(localContact.x) <= 6f && Mathf.Abs(localContact.z) <= 6f && Mathf.Abs(localContact.y) <= 6f;
            if (valid)
            {
                owner = pointerId; lastRay = Time.unscaledTime;
                if (workspace.editSession != null && workspace.editSession.snapToGrid)
                {
                    var step = Mathf.Max(.001f, workspace.editSession.snapSize);
                    localContact.x = Mathf.Round(localContact.x / step) * step;
                    localContact.z = Mathf.Round(localContact.z / step) * step;
                }
                localPosition = SpaceCanvasGeometry.GroundedPosition(localContact, bounds, ghost.transform.localScale);
                ghost.transform.localPosition = localPosition;
            }
            if (HasValidPose != valid) Trace("Pose valid=" + valid + "; pointer=" + pointerId + "; local=" + localPosition);
            HasValidPose = valid; ghost.gameObject.SetActive(valid);
            return valid;
        }

        public bool Confirm(int pointerId)
        {
            if (!HasValidPose || owner != pointerId || !IsPlacing) return false;
            var id = assetId; var position = localPosition;
            Trace("Confirm asset=" + id + "; pointer=" + pointerId + "; local=" + position);
            Cancel();
            return workspace.CommitAsset(id, position);
        }

        public void Cancel()
        {
            var wasPlacing = IsPlacing;
            if (ghost != null) ghost.ReleaseOwnedMaterials();
            foreach (var material in previewMaterials)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            }
            previewMaterials.Clear();
            ghost = null;
            if (ghostRoot != null)
            {
                ghostRoot.SetActive(false);
                if (Application.isPlaying) Destroy(ghostRoot); else DestroyImmediate(ghostRoot);
            }
            ghostRoot = null; owner = 0; HasValidPose = false;
            if (panel != null) panel.SetActive(false);
            if (wasPlacing) { if (workspace != null) workspace.RefreshChrome(); ViewChanged?.Invoke(); }
        }

        private void Update()
        {
            if (IsPlacing && (Time.unscaledTime - lastRay > .4f || (projectUI != null && projectUI.ModalOpen)))
            { HasValidPose = false; ghost.gameObject.SetActive(false); }
        }
        private void OnDisable() { Cancel(); }
        private void OnApplicationFocus(bool focus) { if (!focus) Cancel(); }

        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void Trace(string message) { Debug.Log("[SpaceCanvas Placement] " + message); }

        public void BuildView(Transform parent, Camera camera)
        {
            if (panel != null) { if (Application.isPlaying) Destroy(panel); else DestroyImmediate(panel); }
            panel = SpaceCanvasPresentationStyle.SurfacePanel(parent, "放置预览", Vector2.zero, new Vector2(820, 82), SpaceCanvasPresentationStyle.Surface, 16).gameObject;
            SpaceCanvasPresentationStyle.Dock(panel, new Vector3(0, 352, -160), new Vector2(820, 82), 0, 3060);
            panel.GetComponent<Canvas>().worldCamera = camera;
            label = SpaceCanvasPresentationStyle.Label(panel.transform, "Placement status", "", new Vector2(-64, 0), new Vector2(640, 62), 19);
            SpaceCanvasPresentationStyle.Button(panel.transform, "取消放置", "取消", new Vector2(334, 0), new Vector2(112, 48), Cancel);
            panel.SetActive(false);
        }
    }
}
