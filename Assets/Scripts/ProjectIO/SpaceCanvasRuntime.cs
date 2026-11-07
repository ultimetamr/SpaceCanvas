using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    public static class SpaceCanvasRuntime
    {
        public static Transform ContentRoot { get; set; }
        public static SpaceCanvasProjectRoot CurrentProjectRoot { get; private set; }

        public static void SetCurrentProjectRoot(SpaceCanvasProjectRoot root)
        {
            CurrentProjectRoot = root;
        }

        public static List<SpaceCanvasEditableObject> GetEditableObjects()
        {
            if (ContentRoot != null) return new List<SpaceCanvasEditableObject>(ContentRoot.GetComponentsInChildren<SpaceCanvasEditableObject>(true));
            return new List<SpaceCanvasEditableObject>(UnityEngine.Object.FindObjectsOfType<SpaceCanvasEditableObject>(true));
        }

        public static List<SpaceCanvasGroup> GetGroups()
        {
            if (ContentRoot != null) return new List<SpaceCanvasGroup>(ContentRoot.GetComponentsInChildren<SpaceCanvasGroup>(true));
            return new List<SpaceCanvasGroup>(UnityEngine.Object.FindObjectsOfType<SpaceCanvasGroup>(true));
        }

        public static void ClearEditableScene()
        {
            CurrentProjectRoot = null;
            foreach (var root in UnityEngine.Object.FindObjectsOfType<SpaceCanvasProjectRoot>(true))
                if (root != null && (ContentRoot == null || root.transform.IsChildOf(ContentRoot))) DestroyObject(root.gameObject);
            foreach (var group in GetGroups())
                if (group != null) DestroyObject(group.gameObject);
            foreach (var item in GetEditableObjects())
                if (item != null) DestroyObject(item.gameObject);
        }

        /// <summary>
        /// Migrates saved objects to curated resources first, then procedural built-ins.
        /// Keeps editable descendants alive and preserves their local transforms and IDs.
        /// </summary>
        public static int UpgradeBuiltinAssets()
        {
            var candidates = GetEditableObjects();
            var upgraded = 0;
            foreach (var oldEditable in candidates)
            {
                if (oldEditable == null || !SpaceCanvasBuiltinAssets.IsKnown(oldEditable.AssetId)) continue;
                var curatedPrefab = SpaceCanvasAssetLibrary.LoadCuratedPrefab(oldEditable.AssetId);
                var current = oldEditable.GetComponent<SpaceCanvasCuratedAsset>();
                if (curatedPrefab != null)
                {
                    var expected = curatedPrefab.GetComponent<SpaceCanvasCuratedAsset>();
                    if (current != null && current.sourceId == expected.sourceId && current.revision == expected.revision) continue;
                }
                else
                {
                    var premium = oldEditable.GetComponent<SpaceCanvasPremiumAssetTag>();
                    if ((premium != null && premium.revision == SpaceCanvasPremiumAssetTag.CurrentRevision)
                        || oldEditable.AssetId.StartsWith("primitive.", StringComparison.Ordinal)) continue;
                }
                var data = oldEditable.ToSceneObject();
                var parent = oldEditable.transform.parent;
                var sibling = oldEditable.transform.GetSiblingIndex();
                var replacement = SpaceCanvasAssetLibrary.CreateInstance(oldEditable.AssetId, parent, out _);
                replacement.name = oldEditable.gameObject.name;
                var replacementEditable = replacement.GetComponent<SpaceCanvasEditableObject>();
                if (replacementEditable == null) replacementEditable = replacement.AddComponent<SpaceCanvasEditableObject>();
                replacementEditable.Apply(data);
                // Transfer only direct editable descendants; their own children move with them.
                // Destroying the old model first would silently destroy nested user objects.
                foreach (var child in oldEditable.GetComponentsInChildren<SpaceCanvasEditableObject>(true))
                {
                    if (child == oldEditable || child.transform.parent == null) continue;
                    if (child.transform.parent.GetComponentInParent<SpaceCanvasEditableObject>() == oldEditable)
                        child.transform.SetParent(replacement.transform, false);
                }
                if (replacement.transform.parent != null)
                    replacement.transform.SetSiblingIndex(Mathf.Clamp(sibling, 0, replacement.transform.parent.childCount - 1));
                DestroyObject(oldEditable.gameObject);
                upgraded++;
            }
            if (upgraded > 0) Debug.Log("SpaceCanvas：已更新 " + upgraded + " 个场景素材（优先使用真实贴图模型）。");
            return upgraded;
        }

        private static void DestroyObject(GameObject gameObject)
        {
            if (gameObject == null) return;
            // Destroy 到帧末才执行；先禁用并移出容器，避免同一帧再导出时收集旧物体。
            gameObject.SetActive(false);
            gameObject.transform.SetParent(null, false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(gameObject);
            else UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    public static class SpaceCanvasAssetLibrary
    {
        private static Material cachedDefaultMaterial;

        /// <summary>Shared template only. Editable objects clone it before changing appearance.</summary>
        public static Material DefaultMaterial
        {
            get
            {
                if (IsUsablePbr(cachedDefaultMaterial)) return cachedDefaultMaterial;

                var material = Resources.Load<Material>("SpaceCanvasMaterials/DefaultPBR");
                if (IsUsablePbr(material)) return cachedDefaultMaterial = material;

                // Resolve a second serialized source if the default asset was replaced or
                // is incompatible. Keep the resource shared; callers own their clones.
                var cube = Resources.Load<GameObject>("SpaceCanvasAssets/primitive.cube");
                if (cube != null)
                {
                    foreach (var renderer in cube.GetComponentsInChildren<Renderer>(true))
                        foreach (var candidate in renderer.sharedMaterials)
                            if (IsUsablePbr(candidate)) return cachedDefaultMaterial = candidate;
                }

                throw new InvalidOperationException("未找到可用的 URP PBR 材质：请检查 SpaceCanvasMaterials/DefaultPBR 和 SpaceCanvasAssets/primitive.cube 资源。");
            }
        }

        private static bool IsUsablePbr(Material material)
        {
            return material != null && material.shader != null && material.shader.isSupported
                && material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal);
        }

        /// <summary>Dedicated override folder avoids selecting legacy placeholder prefabs.</summary>
        public static GameObject LoadCuratedPrefab(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId)) return null;
            var prefab = Resources.Load<GameObject>("SpaceCanvasCurated/" + assetId);
            return prefab != null && prefab.GetComponent<SpaceCanvasCuratedAsset>() != null ? prefab : null;
        }

        public static GameObject CreateInstance(string assetId, Transform parent, out bool missing)
        {
            missing = false;
            var prefab = Resources.Load<GameObject>("SpaceCanvasAssets/" + assetId);
            if (prefab == null) prefab = Resources.Load<GameObject>(assetId);
            GameObject instance;
            var curated = LoadCuratedPrefab(assetId);
            if (curated != null)
            {
                instance = UnityEngine.Object.Instantiate(curated, parent);
            }
            else if (SpaceCanvasBuiltinAssets.IsKnown(assetId))
            {
                instance = SpaceCanvasBuiltinAssets.Create(assetId, parent);
            }
            else if (prefab != null)
            {
                instance = UnityEngine.Object.Instantiate(prefab, parent);
            }
            else
            {
                missing = true;
                // Use the registered Resources prefab in Spatial builds so the native renderer
                // can instantiate the replacement as well as Unity's managed scene graph.
                var replacement = Resources.Load<GameObject>("SpaceCanvasAssets/primitive.cube");
                instance = replacement != null ? UnityEngine.Object.Instantiate(replacement) : GameObject.CreatePrimitive(PrimitiveType.Cube);
                if (parent != null) instance.transform.SetParent(parent, false);
                instance.name = "MissingAsset_" + (string.IsNullOrWhiteSpace(assetId) ? "primitive.cube" : assetId);
                if (replacement == null)
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                        renderer.sharedMaterial = DefaultMaterial;
            }
            var editable = instance.GetComponent<SpaceCanvasEditableObject>();
            if (editable == null) editable = instance.AddComponent<SpaceCanvasEditableObject>();
            editable.AssetId = string.IsNullOrWhiteSpace(assetId) ? "primitive.cube" : assetId;
            return instance;
        }
    }

    public static class SpaceCanvasUndoService
    {
        private static readonly Stack<string> Snapshots = new Stack<string>();
        public const int MaxSteps = 20;

        public static int Count { get { return Snapshots.Count; } }
        public static void Clear() { Snapshots.Clear(); }

        public static void PushSnapshot(SpaceCanvasProject project)
        {
            if (project == null) return;
            Snapshots.Push(ProjectSerializer.Serialize(project));
            while (Snapshots.Count > MaxSteps) TrimOldest();
        }

        public static bool UndoLast(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (Snapshots.Count == 0)
            {
                errorMessage = "没有可撤销的工程操作。";
                return false;
            }
            try
            {
                // Keep the snapshot if restoration fails so the user can retry.
                var project = ProjectSerializer.Deserialize(Snapshots.Peek());
                if (!SceneImporter.ImportProject(project, out errorMessage, false)) return false;
                Snapshots.Pop();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = "撤销失败：" + ex.Message;
                return false;
            }
        }

        private static void TrimOldest()
        {
            var items = new List<string>(Snapshots);
            Snapshots.Clear();
            for (var i = Math.Min(MaxSteps, items.Count) - 1; i >= 0; i--) Snapshots.Push(items[i]);
        }
    }
}
