using System;
using System.Collections;
using System.IO;
using UnityEngine;
using TMPro;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>Dirty state and recovery are separate from user saves. Imports preserve an independent backup first.</summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasProjectSession : MonoBehaviour
    {
        public SpaceCanvasProjectIOUI projectUI;
        [SerializeField] private TextMeshProUGUI status;
        public bool Dirty { get; private set; }
        public bool RecoveryPending => pending != null;
        public string RecoveryDate => pending?.savedUtc ?? "";
        private SpaceCanvasRecoveryStore store;
        private SpaceCanvasRecoveryStore.Entry pending;
        private string savedFingerprint, recoveredFingerprint;
        private float nextScan, nextSave;
        private string lastRecovery = "";

        private IEnumerator Start()
        {
            yield return null; // Workspace Awake upgrades the saved prefab instances before our baseline.
            store = new SpaceCanvasRecoveryStore(Path.Combine(Application.persistentDataPath, "Recovery"));
            if (SceneExporter.TryCaptureCurrentScene(null, out var project, out _)) savedFingerprint = SpaceCanvasRecoveryStore.Fingerprint(project);
            if (store.TryRead(out pending)) projectUI.ShowRecoveryPrompt();
            projectUI.ProjectSaved += OnSaved;
            nextSave = Time.unscaledTime + 30;
        }

        private void Update()
        {
            if (store == null || Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 2;
            if (SceneExporter.TryCaptureCurrentScene(null, out var project, out _))
            {
                var fingerprint = SpaceCanvasRecoveryStore.Fingerprint(project);
                Dirty = fingerprint != savedFingerprint;
                if (Dirty && pending == null && Time.unscaledTime >= nextSave && !GestureActive()) SaveRecovery(project, fingerprint);
            }
            if (status != null) status.text = (Dirty ? "● 未保存" : "已保存") + (pending != null ? "  ·  有待恢复的副本" : lastRecovery);
        }

        private bool GestureActive()
        { var session = GetComponent<SpaceCanvasEditSession>(); return session != null && session.IsManipulating; }
        private void SaveRecovery(SpaceCanvasProject data, string fingerprint)
        {
            nextSave = Time.unscaledTime + 30;
            if (fingerprint == recoveredFingerprint) return;
            try { store.Write(data); recoveredFingerprint = fingerprint; lastRecovery = "  ·  已自动备份"; }
            catch (Exception ex) { lastRecovery = "  ·  自动备份失败，请手动保存"; Debug.LogWarning("SpaceCanvas 自动备份：" + ex.Message); }
        }

        private void OnSaved(string path)
        {
            try
            {
                var data = ProjectSerializer.Deserialize(File.ReadAllText(path));
                var root = SpaceCanvasRuntime.CurrentProjectRoot;
                if (root != null) { root.projectName = data.projectName; root.createTime = data.createTime; }
                if (SceneExporter.TryCaptureCurrentScene(null, out var current, out _)) savedFingerprint = SpaceCanvasRecoveryStore.Fingerprint(current);
                Dirty = false; lastRecovery = ""; recoveredFingerprint = null;
                if (pending == null) store?.Clear();
            }
            catch (Exception ex) { Debug.LogWarning("SpaceCanvas 保存状态更新失败：" + ex.Message); }
        }

        public bool ProtectBeforeImport(out string error)
        {
            error = "";
            if (!SceneExporter.TryCaptureCurrentScene(null, out var data, out error)) return false;
            Dirty = SpaceCanvasRecoveryStore.Fingerprint(data) != savedFingerprint;
            if (!Dirty) return true;
            try { SpaceCanvasRecoveryStore.Backup(Path.Combine(Application.persistentDataPath, "Projects"), data); return true; }
            catch (Exception ex) { error = "导入已暂停：无法备份当前未保存的作品。请先保存或腾出存储空间。"; Debug.LogWarning(error + " " + ex.Message); return false; }
        }

        public bool Restore(out string error)
        {
            error = "没有可恢复的副本。";
            if (pending == null) return false;
            if (!ProtectBeforeImport(out error)) return false;
            if (!SceneImporter.ImportProject(pending.project, out error, true)) return false;
            pending = null; Dirty = true; nextSave = Time.unscaledTime + 30;
            GetComponent<SpaceCanvasWorkspaceUI>()?.RefreshSelection();
            return true;
        }
        public void DiscardRecovery()
        {
            try { store?.Clear(); pending = null; }
            catch (Exception ex) { projectUI.ShowFeedback("删除恢复副本失败：" + ex.Message, false); }
        }
        private void Flush()
        {
            SpaceCanvasScalarSlider.FinishAll(); GetComponent<SpaceCanvasEditSession>()?.EndGesture(false);
            if (store == null || pending != null) return;
            if (SceneExporter.TryCaptureCurrentScene(null, out var data, out _))
            {
                var fingerprint = SpaceCanvasRecoveryStore.Fingerprint(data);
                if (fingerprint != savedFingerprint) SaveRecovery(data, fingerprint);
            }
        }
        private void OnApplicationPause(bool paused) { if (paused) Flush(); }
        private void OnApplicationQuit() { Flush(); }
        private void OnDestroy() { if (projectUI != null) projectUI.ProjectSaved -= OnSaved; }
        public void BuildStatus(Transform parent)
        {
            if (status != null) { if (Application.isPlaying) Destroy(status.gameObject); else DestroyImmediate(status.gameObject); }
            var toolbar = parent.Find("SpaceCanvasToolbar");
            status = SpaceCanvasPresentationStyle.Label(toolbar != null ? toolbar : parent, "工程保存状态", "已保存", new Vector2(0, -62), new Vector2(850, 28), 16, true);
            if (toolbar != null)
            {
                // The existing toolbar uses a layout group. Keep this annotation outside its flow.
                status.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
            }
            status.alignment = TextAlignmentOptions.Center;
        }
    }
}
