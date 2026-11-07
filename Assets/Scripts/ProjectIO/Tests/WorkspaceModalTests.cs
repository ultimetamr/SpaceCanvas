using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpaceCanvas.ProjectIO.Tests
{
    /// <summary>
    /// Exercises saved MR UI after restoring runtime button delegates.
    /// Opens an isolated temporary copy and closes it without saving test changes.
    /// </summary>
    public sealed class WorkspaceModalTests
    {
        private Scene scene;
        private Scene previousScene;
        private string temporaryPath;
        private SpaceCanvasProjectIOUI ui;
        private Transform toolbar;

        [SetUp]
        public void OpenSavedPresentation()
        {
            previousScene = SceneManager.GetActiveScene();
            temporaryPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Scripts/ProjectIO/Tests/TemporaryModalValidation.unity");
            Assert.That(AssetDatabase.CopyAsset("Assets/Scenes/SpaceCanvas_FullSpaceMR.unity", temporaryPath), Is.True);
            scene = EditorSceneManager.OpenScene(temporaryPath, OpenSceneMode.Additive);
            ui = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<SpaceCanvasProjectIOUI>(true)).Single();
            toolbar = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Canvas>(true))
                .Single(x => x.name == "SpaceCanvasToolbar").transform;
            typeof(SpaceCanvasProjectIOUI).GetMethod("BindExistingToolbar", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
        }

        [TearDown]
        public void CloseWithoutSaving()
        {
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            if (previousScene.IsValid()) SceneManager.SetActiveScene(previousScene);
            if (!string.IsNullOrEmpty(temporaryPath)) AssetDatabase.DeleteAsset(temporaryPath);
        }

        private Button ToolbarButton(string name) => toolbar.GetComponentsInChildren<Button>(true).Single(x => x.name == name);

        [Test]
        public void ContextualInspectorControlsStayWithinSavedPanelAndRebindAfterRestart()
        {
            var workspace = ui.GetComponent<SpaceCanvasWorkspaceUI>();
            var previousRoot = SpaceCanvasRuntime.ContentRoot;
            var previousProject = SpaceCanvasRuntime.CurrentProjectRoot;
            var content = ui.GetComponent<SpaceCanvasWorkspace>().contentRoot;
            try
            {
                SpaceCanvasRuntime.ContentRoot = content;
                SpaceCanvasRuntime.SetCurrentProjectRoot(content.GetComponentInChildren<SpaceCanvasProjectRoot>());
                typeof(SpaceCanvasWorkspaceUI).GetMethod("Start",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(workspace,null);
                Assert.IsNull(workspace.Selected);
                workspace.SelectObject(content.GetComponentsInChildren<SpaceCanvasEditableObject>().First());
                var body = (RectTransform)typeof(SpaceCanvasWorkspaceUI).GetField("propertyBody",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(workspace);
                var corners = new Vector3[4];
                foreach (var page in new[] { "变换页", "外观页" })
                {
                    body.GetComponentsInChildren<Button>().Single(x=>x.name==page).onClick.Invoke(); Canvas.ForceUpdateCanvases();
                    foreach (var selectable in body.GetComponentsInChildren<Selectable>())
                    {
                        ((RectTransform)selectable.transform).GetWorldCorners(corners);
                        foreach(var corner in corners)
                        {
                            var local = body.InverseTransformPoint(corner);
                            Assert.That(local.x,Is.InRange(body.rect.xMin-.5f,body.rect.xMax+.5f),selectable.name);
                            Assert.That(local.y,Is.InRange(body.rect.yMin-.5f,body.rect.yMax+.5f),selectable.name);
                        }
                    }
                    foreach(var text in body.GetComponentsInChildren<TMPro.TextMeshProUGUI>())
                    {
                        text.ForceMeshUpdate();
                        Assert.That(text.preferredHeight,Is.LessThanOrEqualTo(text.rectTransform.rect.height+1),text.name);
                        if(!SpaceCanvasUnityCompatibility.IsTextWrappingEnabled(text)) Assert.That(text.preferredWidth,Is.LessThanOrEqualTo(text.rectTransform.rect.width+1),text.name);
                    }
                }
            }
            finally { SpaceCanvasRuntime.ContentRoot=previousRoot; SpaceCanvasRuntime.SetCurrentProjectRoot(previousProject); }
        }

        [Test]
        public void LayoutAndRecoveryAreExclusiveModalsWithBoundedTextAndTrackedRaycasters()
        {
            var settings = ui.GetComponent<SpaceCanvasWorkspaceSettingsUI>(); Assert.IsNotNull(settings);
            settings.Open(); Assert.IsTrue(ui.layoutPanel.activeSelf); AssertToolbarAvailable(false);
            ui.OpenExternalPanel(ui.recoveryPanel); Assert.IsFalse(ui.layoutPanel.activeSelf); Assert.IsTrue(ui.recoveryPanel.activeSelf);
            foreach (var panel in new[] { ui.layoutPanel, ui.recoveryPanel, ui.confirmPanel })
            {
                Assert.IsNotNull(panel.GetComponent(System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit")));
                panel.SetActive(true); Canvas.ForceUpdateCanvases();
                foreach(var slider in panel.GetComponentsInChildren<Slider>())
                    Assert.That(slider.handleRect.rect.height, Is.LessThanOrEqualTo(((RectTransform)slider.transform).rect.height), "Slider thumb exceeds row");
                foreach(var text in panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>())
                {
                    text.ForceMeshUpdate();
                    Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 1),panel.name + " / " + text.name);
                    if(!SpaceCanvasUnityCompatibility.IsTextWrappingEnabled(text)) Assert.That(text.preferredWidth,Is.LessThanOrEqualTo(text.rectTransform.rect.width + 1),panel.name + " / " + text.name);
                }
                panel.SetActive(false);
            }
            ui.CloseExternalPanel(ui.recoveryPanel); AssertToolbarAvailable(true);
        }

        private void AssertToolbarAvailable(bool available)
        {
            var group = toolbar.GetComponent<CanvasGroup>();
            Assert.That(group, Is.Not.Null);
            Assert.That(group.interactable, Is.EqualTo(available));
            Assert.That(group.blocksRaycasts, Is.EqualTo(available));
            Assert.That(group.alpha, Is.EqualTo(available ? 1f : .18f).Within(.001f));
        }

        [Test]
        public void AllTutorialPagesAndToolbarControlsFitTheirSavedBounds()
        {
            var tutorial = ui.GetComponent<SpaceCanvasOnboarding>();
            tutorial.Restart();
            var coach = (GameObject)typeof(SpaceCanvasOnboarding).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tutorial);
            var canvas = coach.GetComponent<Canvas>();
            Assert.IsNotNull(canvas.GetComponent(System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit")));
            for (var i = 0; i < 6; i++)
            {
                Canvas.ForceUpdateCanvases();
                foreach (var text in coach.GetComponentsInChildren<TMPro.TextMeshProUGUI>())
                {
                    text.ForceMeshUpdate();
                    Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 1), tutorial.Flow.Step + ": " + text.name + " height");
                    if (!SpaceCanvasUnityCompatibility.IsTextWrappingEnabled(text)) Assert.That(text.preferredWidth, Is.LessThanOrEqualTo(text.rectTransform.rect.width + 1), tutorial.Flow.Step + ": " + text.name + " width");
                }
                tutorial.Flow.Continue(); tutorial.RefreshView();
            }
            var parentRect = (RectTransform)toolbar;
            var corners = new Vector3[4];
            foreach (var button in toolbar.GetComponentsInChildren<Button>())
            {
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var corner in corners) Assert.IsTrue(parentRect.rect.Contains(parentRect.InverseTransformPoint(corner)), button.name + " exceeds toolbar");
            }
        }

        [Test]
        public void TutorialReplayYieldsToModalsAndBrowsingExamplesNeverImportsAProject()
        {
            var tutorial = ui.GetComponent<SpaceCanvasOnboarding>();
            Assert.That(tutorial, Is.Not.Null);
            var hadPreference = PlayerPrefs.HasKey(SpaceCanvasOnboarding.SeenPreference);
            var oldPreference = PlayerPrefs.GetInt(SpaceCanvasOnboarding.SeenPreference);
            try
            {
                PlayerPrefs.SetInt(SpaceCanvasOnboarding.SeenPreference, 1);
                typeof(SpaceCanvasOnboarding).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tutorial, null);
                ToolbarButton("新手教学").onClick.Invoke();
                var coach = (GameObject)typeof(SpaceCanvasOnboarding).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tutorial);
                Assert.IsTrue(coach.activeSelf);
                var roots = scene.GetRootGameObjects();
                var objectIds = roots.SelectMany(x => x.GetComponentsInChildren<SpaceCanvasEditableObject>(true)).Select(x => SpaceCanvasUnityCompatibility.ObjectId(x)).ToArray();
                for (var i = 0; i < 4; i++) tutorial.Flow.Continue();
                Assert.That(tutorial.Flow.Step, Is.EqualTo(SpaceCanvasTutorialStep.BrowseExamples));
                ui.ShowOfficialExamples(); // Closed import panels are not evidence of actual discovery.
                Assert.That(tutorial.Flow.Step, Is.EqualTo(SpaceCanvasTutorialStep.BrowseExamples));
                ui.OpenImportPanel();
                Assert.IsFalse(coach.activeSelf);
                ui.ShowOfficialExamples();
                Assert.That(tutorial.Flow.Step, Is.EqualTo(SpaceCanvasTutorialStep.Complete));
                Assert.IsFalse(coach.activeSelf);
                ui.CloseImportPanel();
                Assert.IsTrue(coach.activeSelf);
                CollectionAssert.AreEqual(objectIds, roots.SelectMany(x => x.GetComponentsInChildren<SpaceCanvasEditableObject>(true)).Select(x => SpaceCanvasUnityCompatibility.ObjectId(x)).ToArray());
                tutorial.Dismiss();
                Assert.IsFalse(coach.activeSelf);
                Assert.IsTrue(PlayerPrefs.HasKey(SpaceCanvasOnboarding.SeenPreference));
                ui.ReplayTutorial();
                Assert.That(tutorial.Flow.Step, Is.EqualTo(SpaceCanvasTutorialStep.Welcome));
            }
            finally
            {
                if (hadPreference) PlayerPrefs.SetInt(SpaceCanvasOnboarding.SeenPreference, oldPreference);
                else PlayerPrefs.DeleteKey(SpaceCanvasOnboarding.SeenPreference);
            }
        }

        [Test]
        public void AboutButtonRebindsAndBlocksUnderlyingToolbarUntilClosed()
        {
            ToolbarButton("关于开源").onClick.Invoke();
            Assert.That(ui.aboutPanel.activeSelf, Is.True);
            AssertToolbarAvailable(false);
            ui.aboutPanel.GetComponentsInChildren<Button>(true).Single(x => x.name == "关闭").onClick.Invoke();
            Assert.That(ui.ModalOpen, Is.False);
            AssertToolbarAvailable(true);
        }

        [Test]
        public void LocalVersionConfirmationDisplaysMetadataAndCancelPreservesWorkspace()
        {
            var path = System.IO.Path.Combine(SpaceCanvasProjectCatalog.LocalProjectsDirectory, "确认版本-" + System.Guid.NewGuid().ToString("N") + ".spacecanvas");
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                var project = new SpaceCanvasProject { projectName = "版本预览测试", updateTime = 1700000000 };
                project.objects.Add(new SceneObject { id = "preview-only" });
                System.IO.File.WriteAllText(path, ProjectSerializer.Serialize(project));
                var originalIds = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<SpaceCanvasEditableObject>(true)).Select(x => SpaceCanvasUnityCompatibility.ObjectId(x)).ToArray();
                ui.ImportProjectFile(path);
                Assert.IsTrue(ui.confirmPanel.activeSelf); AssertToolbarAvailable(false);
                var labels = ui.confirmPanel.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Select(x => x.text).ToArray();
                Assert.That(labels, Does.Contain(System.IO.Path.GetFileName(path)));
                StringAssert.Contains("1 个物体", ui.confirmationText.text);
                StringAssert.Contains(SpaceCanvasProjectEntry.FormatTime(project.updateTime), ui.confirmationText.text);
                var corners = new Vector3[4]; var panel = (RectTransform)ui.confirmPanel.transform;
                foreach (var button in ui.confirmPanel.GetComponentsInChildren<Button>())
                {
                    ((RectTransform)button.transform).GetWorldCorners(corners);
                    foreach (var corner in corners) Assert.IsTrue(panel.rect.Contains(panel.InverseTransformPoint(corner)), button.name);
                }
                ui.CancelImport();
                CollectionAssert.AreEqual(originalIds, scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<SpaceCanvasEditableObject>(true)).Select(x => SpaceCanvasUnityCompatibility.ObjectId(x)).ToArray());
                Assert.IsFalse(ui.confirmPanel.activeSelf);
            }
            finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        }

        [Test]
        public void OfficialTemplateConfirmationCanCancelAndRestoreToolbar()
        {
            ToolbarButton("导入工程").onClick.Invoke();
            ui.importPanel.GetComponentsInChildren<Button>(true).Single(x => x.name == "官方示例").onClick.Invoke();
            var rows = ui.officialListRoot.GetComponentsInChildren<Button>();
            Assert.That(rows.Length, Is.EqualTo(3));
            rows[0].onClick.Invoke();
            Assert.That(ui.confirmPanel.activeSelf, Is.True);
            Assert.That(ui.importPanel.activeSelf, Is.False);
            AssertToolbarAvailable(false);
            ui.confirmPanel.GetComponentsInChildren<Button>(true).Single(x => x.name == "取消").onClick.Invoke();
            Assert.That(ui.confirmPanel.activeSelf, Is.False);
            Assert.That(ui.officialListRoot.gameObject.activeInHierarchy, Is.True);
            AssertToolbarAvailable(false);
            ui.importPanel.GetComponentsInChildren<Button>(true).Single(x => x.name == "关闭").onClick.Invoke();
            Assert.That(ui.ModalOpen, Is.False);
            AssertToolbarAvailable(true);
        }
    }
}
