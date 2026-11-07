using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// Optional bridge component for the host editor. Add it to every object that can be saved.
    /// External object managers can populate the same fields or replace the registry adapter.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasEditableObject : MonoBehaviour
    {
        [SerializeField] private string objectId;
        [SerializeField] private string assetId = "primitive.cube";
        [SerializeField] private string colorHex = "#FFFFFFFF";
        [SerializeField, Range(0f, 1f)] private float alpha = 1f;
        [SerializeField, Range(0f, 1f)] private float metallic;
        [SerializeField] private bool colliderEnabled = true;
        // 编辑器导入会把最终材质保存为资源。保存原始 PBR 基色，避免重开/打包时
        // Awake 把 JSON 颜色再次乘到已经染过色的材质上。
        [Serializable] private sealed class MaterialBaseline
        {
            public Renderer renderer;
            public Color[] colors;
            public float[] metallicValues;
        }
        [SerializeField, HideInInspector] private List<MaterialBaseline> materialBaselines = new List<MaterialBaseline>();
        private readonly Dictionary<Renderer, Material[]> ownedMaterials = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Material, Color> authoredColors = new Dictionary<Material, Color>();
        private readonly Dictionary<Material, float> authoredMetallic = new Dictionary<Material, float>();
        private Bounds pickingBounds;
        private bool pickingBoundsValid;

        internal Bounds PickingBounds
        {
            get
            {
                if (!pickingBoundsValid) { pickingBounds = SpaceCanvasGeometry.LocalBounds(this); pickingBoundsValid = true; }
                return pickingBounds;
            }
        }

        /// <summary>Call after changing child mesh geometry or child transforms in a custom asset.</summary>
        public void InvalidatePickingBounds() { pickingBoundsValid = false; }
        private void OnEnable() { InvalidatePickingBounds(); }
        private void OnTransformChildrenChanged() { InvalidatePickingBounds(); }

        public string ObjectId { get { return objectId; } set { objectId = value; } }
        public string AssetId { get { return assetId; } set { assetId = string.IsNullOrWhiteSpace(value) ? "primitive.cube" : value; } }
        public string ColorHex { get { return colorHex; } set { colorHex = string.IsNullOrWhiteSpace(value) ? "#FFFFFFFF" : value; } }
        public float Alpha { get { return alpha; } set { alpha = Mathf.Clamp01(value); } }
        public float Metallic { get { return metallic; } set { metallic = Mathf.Clamp01(value); } }
        public bool ColliderEnabled { get { return colliderEnabled; } set { colliderEnabled = value; ApplyColliderState(); } }

        private void Awake()
        {
            EnsureIdentity();
            ApplyVisualState();
            ApplyColliderState();
        }

        private void OnValidate()
        {
            EnsureIdentity();
        }

        public void EnsureIdentity()
        {
            if (string.IsNullOrWhiteSpace(objectId)) objectId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(assetId)) assetId = "primitive.cube";
            if (string.IsNullOrWhiteSpace(colorHex)) colorHex = "#FFFFFFFF";
        }

        public SceneObject ToSceneObject()
        {
            EnsureIdentity();
            var parent = transform.parent == null ? null : transform.parent.GetComponentInParent<SpaceCanvasEditableObject>();
            return new SceneObject
            {
                id = objectId,
                assetId = assetId,
                parentId = parent == null ? string.Empty : parent.ObjectId,
                localPosition = new[] { transform.localPosition.x, transform.localPosition.y, transform.localPosition.z },
                localRotation = new[] { transform.localRotation.x, transform.localRotation.y, transform.localRotation.z, transform.localRotation.w },
                localScale = new[] { transform.localScale.x, transform.localScale.y, transform.localScale.z },
                colorHex = colorHex,
                alpha = alpha,
                metallic = metallic,
                colliderEnabled = colliderEnabled
            };
        }

        public void Apply(SceneObject data)
        {
            if (data == null) return;
            InvalidatePickingBounds();
            data.EnsureDefaults(0);
            ObjectId = data.id;
            AssetId = data.assetId;
            ColorHex = data.colorHex;
            Alpha = data.alpha;
            Metallic = data.metallic;
            ColliderEnabled = data.colliderEnabled;
            transform.localPosition = new Vector3(data.localPosition[0], data.localPosition[1], data.localPosition[2]);
            transform.localRotation = new Quaternion(data.localRotation[0], data.localRotation[1], data.localRotation[2], data.localRotation[3]);
            transform.localScale = new Vector3(data.localScale[0], data.localScale[1], data.localScale[2]);
            ApplyVisualState();
        }

        private void ApplyVisualState()
        {
            if (materialBaselines == null) materialBaselines = new List<MaterialBaseline>();
            if (!ColorUtility.TryParseHtmlString(colorHex, out var color)) color = Color.white;
            color.a = alpha;
            var curated = GetComponent<SpaceCanvasCuratedAsset>();
            var preserveAuthored = curated != null || GetComponent<SpaceCanvasPremiumAssetTag>() != null;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                // Imports and development audits build under inactive staging roots.
                // Include those parents so each renderer still receives an owned clone.
                if (renderer.GetComponentInParent<SpaceCanvasEditableObject>(true) != this) continue;
                // 每个物体拥有自己的材质实例，禁止修改共享 prefab/PBR 资源导致全部物体变色。
                if (!ownedMaterials.TryGetValue(renderer, out var materials))
                {
                    var source = renderer.sharedMaterials;
                    if (source.Length == 0) source = new[] { SpaceCanvasAssetLibrary.DefaultMaterial };
                    var baseline = materialBaselines.Find(item => item.renderer == renderer);
                    if (baseline == null || baseline.colors == null || baseline.colors.Length != source.Length
                        || baseline.metallicValues == null || baseline.metallicValues.Length != source.Length)
                    {
                        if (baseline != null) materialBaselines.Remove(baseline);
                        baseline = new MaterialBaseline { renderer = renderer, colors = new Color[source.Length], metallicValues = new float[source.Length] };
                        for (var slot = 0; slot < source.Length; slot++)
                        {
                            var original = source[slot] == null ? SpaceCanvasAssetLibrary.DefaultMaterial : source[slot];
                            baseline.colors[slot] = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") : original.HasProperty("_Color") ? original.color : Color.white;
                            baseline.metallicValues[slot] = original.HasProperty("_Metallic") ? original.GetFloat("_Metallic") : 0;
                        }
                        materialBaselines.Add(baseline);
                    }
                    materials = new Material[source.Length];
                    for (var i = 0; i < source.Length; i++)
                    {
                        var template = source[i] == null ? SpaceCanvasAssetLibrary.DefaultMaterial : source[i];
                        materials[i] = new Material(template);
                        authoredColors[materials[i]] = baseline.colors[i];
                        authoredMetallic[materials[i]] = baseline.metallicValues[i];
                    }
                    ownedMaterials[renderer] = materials;
                    renderer.sharedMaterials = materials;
                }
                foreach (var material in materials)
                {
                if (material == null) continue;
                var displayColor = color;
                if (preserveAuthored)
                {
                    displayColor = authoredColors[material] * Color.Lerp(Color.white, color, curated != null ? curated.tintStrength : 1f);
                    displayColor.a = alpha * authoredColors[material].a;
                }
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", displayColor);
                if (material.HasProperty("_Color")) material.SetColor("_Color", displayColor);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", preserveAuthored ? Mathf.Max(authoredMetallic[material], metallic) : metallic);
                var transparent = displayColor.a < .999f;
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", transparent ? 1 : 0);
                if (material.HasProperty("_Mode")) material.SetFloat("_Mode", transparent ? 3 : 0);
                if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", transparent ? (int)UnityEngine.Rendering.BlendMode.SrcAlpha : (int)UnityEngine.Rendering.BlendMode.One);
                if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", transparent ? (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : (int)UnityEngine.Rendering.BlendMode.Zero);
                if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", transparent ? 0 : 1);
                if (transparent) { material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.EnableKeyword("_ALPHABLEND_ON"); }
                else { material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.DisableKeyword("_ALPHABLEND_ON"); }
                material.renderQueue = transparent ? 3000 : 2000;
                }
            }
        }

        private void OnDestroy()
        {
            // 编辑器中的材质可由启动场景生成器保存为资源，仅清理运行时实例。
            if (!Application.isPlaying) return;
            ReleaseOwnedMaterials();
        }

        /// <summary>Only release clones created by this component, never prefab resources.
        /// Inactive staging objects may never receive OnDestroy, so audit cleanup calls this explicitly.</summary>
        internal void ReleaseOwnedMaterials()
        {
            foreach (var entry in ownedMaterials)
                foreach (var material in entry.Value)
                    if (material != null)
                    {
                        if (Application.isPlaying) Destroy(material);
                        else DestroyImmediate(material);
                    }
            ownedMaterials.Clear();
            authoredColors.Clear();
            authoredMetallic.Clear();
        }

        private void ApplyColliderState()
        {
            foreach (var collider in GetComponentsInChildren<Collider>(true))
                if (collider.GetComponentInParent<SpaceCanvasEditableObject>(true) == this) collider.enabled = colliderEnabled;
        }
    }

}
