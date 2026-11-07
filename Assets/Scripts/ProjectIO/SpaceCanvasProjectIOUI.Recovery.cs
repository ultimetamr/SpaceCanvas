using UnityEngine;
using TMPro;

namespace SpaceCanvas.ProjectIO
{
    public sealed partial class SpaceCanvasProjectIOUI
    {
        private void BindProjectName()
        { if (projectNameInput != null) { projectNameInput.onEndEdit.RemoveListener(UpdateProjectName); projectNameInput.onEndEdit.AddListener(UpdateProjectName); } }
        private void UpdateProjectName(string name)
        { var root = SpaceCanvasRuntime.CurrentProjectRoot; if (root != null && !string.IsNullOrWhiteSpace(name)) root.projectName = name.Trim(); }
        private void RefreshProjectName()
        { if (projectNameInput != null && SpaceCanvasRuntime.CurrentProjectRoot != null) projectNameInput.SetTextWithoutNotify(SpaceCanvasRuntime.CurrentProjectRoot.projectName); }
        private void SaveAndImport()
        { if (TrySaveProject(projectNameInput == null ? "我的空间" : projectNameInput.text)) ConfirmImport(); }

        /// <summary>Shared modal lifecycle keeps the tutorial, dock raycasts and physical activation barrier in sync.</summary>
        public void OpenExternalPanel(GameObject panel)
        {
            FinishGesture();
            foreach (var view in new[] { importPanel, aboutPanel, confirmPanel, layoutPanel, recoveryPanel })
                if (view != null) view.SetActive(view == panel);
            NotifyInterfaceChanged();
        }
        public void CloseExternalPanel(GameObject panel)
        { if (panel != null) panel.SetActive(false); NotifyInterfaceChanged(); }

        public void ShowRecoveryPrompt()
        {
            var session = GetComponent<SpaceCanvasProjectSession>();
            if (session == null || !session.RecoveryPending) { ShowMessage("没有待恢复的作品。", true); return; }
            OpenExternalPanel(recoveryPanel);
        }
        private void CloseRecovery() { CloseExternalPanel(recoveryPanel); }
        private void DiscardRecovery() { GetComponent<SpaceCanvasProjectSession>()?.DiscardRecovery(); CloseRecovery(); }
        private void RestoreRecovery()
        {
            FinishGesture();
            var session = GetComponent<SpaceCanvasProjectSession>();
            if (session == null) return;
            if (session.Restore(out var error)) { RefreshProjectName(); CloseRecovery(); ShowMessage("作品已恢复，仍可撤销。请为恢复的作品保存工程。", true); }
            else ShowMessage(error, false);
        }

        public void BuildRecoveryView()
        {
            recoveryPanel = SpaceCanvasPresentationStyle.SurfacePanel(ViewRoot, "恢复未保存作品", Vector2.zero, new Vector2(720, 340), SpaceCanvasPresentationStyle.Surface, 20).gameObject;
            SpaceCanvasPresentationStyle.Dock(recoveryPanel, new Vector3(0, 10, -330), new Vector2(720, 340), 0, 3200);
            recoveryPanel.GetComponent<Canvas>().worldCamera = viewCamera;
            recoveryPanel.GetComponent<UnityEngine.UI.Image>().color = new Color(SpaceCanvasPresentationStyle.Surface.r, SpaceCanvasPresentationStyle.Surface.g, SpaceCanvasPresentationStyle.Surface.b, 1);
            SpaceCanvasPresentationStyle.Label(recoveryPanel.transform, "Recovery heading", "找到上次未保存的作品", new Vector2(0, 108), new Vector2(640, 48), 29);
            var message = SpaceCanvasPresentationStyle.Label(recoveryPanel.transform, "Recovery detail", "可以恢复自动保存的布局。\n恢复前也会保护当前作品；稍后可从“布局”中重新打开。", new Vector2(0, 25), new Vector2(640, 90), 21, true);
            SpaceCanvasUnityCompatibility.SetTextWrapping(message, true);
            SpaceCanvasPresentationStyle.Button(recoveryPanel.transform, "丢弃副本", "丢弃副本", new Vector2(-216, -104), new Vector2(200, 52), DiscardRecovery);
            SpaceCanvasPresentationStyle.Button(recoveryPanel.transform, "稍后恢复", "稍后恢复", new Vector2(0, -104), new Vector2(200, 52), CloseRecovery);
            var restore = SpaceCanvasPresentationStyle.Button(recoveryPanel.transform, "恢复作品", "恢复作品", new Vector2(216, -104), new Vector2(200, 52), RestoreRecovery);
            SpaceCanvasPresentationStyle.Tone(restore, SpaceCanvasPresentationStyle.Accent, true);
            recoveryPanel.SetActive(false);
        }
    }
}
