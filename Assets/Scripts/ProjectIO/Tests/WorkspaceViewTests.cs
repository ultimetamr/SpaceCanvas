using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpaceCanvas.ProjectIO.Tests
{
    /// <summary>Checks the viewing layer against real import/export, picking, placement and undo.</summary>
    public sealed class WorkspaceViewTests
    {
        private GameObject host, anchor, panels;
        private Transform previousContent, content;
        private SpaceCanvasProjectRoot previousProject;
        private SpaceCanvasWorkspaceView view;
        private SpaceCanvasWorkspaceUI ui;
        private SpaceCanvasPlacement placement;
        private SpaceCanvasEditSession session;
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void Setup()
        {
            previousContent = SpaceCanvasRuntime.ContentRoot;
            previousProject = SpaceCanvasRuntime.CurrentProjectRoot;
            anchor = new GameObject("Tracked workbench");
            anchor.transform.SetPositionAndRotation(new Vector3(1, 1.4f, 2), Quaternion.Euler(0, 31, 0));
            anchor.transform.localScale = Vector3.one * .85f;
            host = new GameObject("Workspace view test"); host.transform.SetParent(anchor.transform, false);
            content = new GameObject("Editable Content").transform; content.SetParent(host.transform, false);
            content.localPosition = new Vector3(0, -.2f, .16f); content.localScale = Vector3.one * .16f;
            SpaceCanvasRuntime.ContentRoot = content; SpaceCanvasRuntime.SetCurrentProjectRoot(null);
            SpaceCanvasUndoService.Clear();
            var project = new SpaceCanvasProject { projectName = "View fixture", createTime = 42 };
            project.objects.Add(new SceneObject { id = "parent", localPosition = new[] { 1f, 0f, 2f }, localScale = new[] { 1f, 2f, 1f } });
            project.objects.Add(new SceneObject { id = "child", parentId = "parent", localPosition = new[] { 0f, 1f, 0f } });
            Assert.IsTrue(SceneImporter.ImportProject(project, out var error, false), error);
            ui = host.AddComponent<SpaceCanvasWorkspaceUI>();
            placement = host.AddComponent<SpaceCanvasPlacement>(); placement.workspace = ui; ui.placement = placement;
            session = host.AddComponent<SpaceCanvasEditSession>(); ui.editSession = session;
            typeof(SpaceCanvasEditSession).GetMethod("Awake", Private).Invoke(session, null);
            view = host.AddComponent<SpaceCanvasWorkspaceView>(); view.Initialize(content, ui);
            panels = new GameObject("Camera and panels stay fixed", typeof(RectTransform));
            panels.transform.SetParent(anchor.transform, false);
        }

        [TearDown] public void Cleanup()
        {
            placement.Cancel();
            Object.DestroyImmediate(anchor);
            SpaceCanvasRuntime.ContentRoot = previousContent;
            SpaceCanvasRuntime.SetCurrentProjectRoot(previousProject);
            SpaceCanvasUndoService.Clear();
        }

        private string ObjectsJson()
        {
            Assert.IsTrue(SceneExporter.TryCaptureCurrentScene(null, out var captured, out var error), error);
            captured.updateTime = 0;
            return ProjectSerializer.Serialize(captured);
        }

        [Test] public void EveryViewPreservesProjectHierarchyCoordinatesAndExternalPanels()
        {
            var json = ObjectsJson(); var matrix = panels.transform.localToWorldMatrix;
            var item = SpaceCanvasRuntime.GetEditableObjects().Single(x => x.ObjectId == "parent");
            var initialWorld = item.transform.position;
            foreach (SpaceCanvasWorkspaceView.Preset preset in System.Enum.GetValues(typeof(SpaceCanvasWorkspaceView.Preset)))
            {
                view.Show(preset, false);
                Assert.AreEqual(json, ObjectsJson()); Assert.AreEqual(matrix, panels.transform.localToWorldMatrix);
                Assert.AreEqual(0, SpaceCanvasUndoService.Count);
            }
            view.Show(SpaceCanvasWorkspaceView.Preset.Back, false);
            Assert.That(Vector3.Distance(item.transform.position, initialWorld), Is.GreaterThan(.1f));
            view.ResetView(); Assert.That(Vector3.Distance(item.transform.position, initialWorld), Is.LessThan(.00001f));
        }

        [Test] public void FullOrbitResetAndRepeatedInitializationDoNotAccumulateDrift()
        {
            var matrix = content.localToWorldMatrix;
            for (var i = 0; i < 8; i++) view.Turn(45);
            Assert.That(Quaternion.Angle(content.localRotation, Quaternion.identity), Is.LessThan(.001f));
            view.Show(SpaceCanvasWorkspaceView.Preset.Top, false); view.Initialize(content, ui); view.ResetView();
            Assert.AreEqual(matrix, content.localToWorldMatrix);
            view.Turn(float.NaN); view.Turn(float.PositiveInfinity);
            Assert.AreEqual(matrix, content.localToWorldMatrix);
            Assert.That(content.localScale, Is.EqualTo(Vector3.one * .16f));
        }

        [Test] public void TopViewFitsTheViewingVolumeAndResetRestoresExactBaseline()
        {
            var matrix = content.localToWorldMatrix; var position = content.localPosition;
            var scale = content.localScale; var json = ObjectsJson();
            view.Show(SpaceCanvasWorkspaceView.Preset.Top, false);
            Assert.That(content.localScale.x, Is.LessThan(scale.x));
            Assert.That(content.localPosition.y, Is.GreaterThan(position.y));
            Assert.AreEqual(json, ObjectsJson());
            view.ResetView(); Assert.AreEqual(matrix, content.localToWorldMatrix);
        }

        [Test] public void ImportAndUndoAtSideViewKeepViewingFrameAndOriginalData()
        {
            var json = ObjectsJson(); view.Show(SpaceCanvasWorkspaceView.Preset.Side, false);
            var matrix = content.localToWorldMatrix;
            Assert.IsTrue(SceneImporter.ImportProject(new SpaceCanvasProject { projectName = "replacement" }, out var error, true), error);
            Assert.AreEqual(matrix, content.localToWorldMatrix);
            Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out error), error);
            Assert.AreEqual(matrix, content.localToWorldMatrix); Assert.AreEqual(json, ObjectsJson());
        }

        [Test] public void DragAtTopViewUsesProjectLocalAxesAndOneUndo()
        {
            var json = ObjectsJson(); view.Show(SpaceCanvasWorkspaceView.Preset.Top, false);
            var item = SpaceCanvasRuntime.GetEditableObjects().Single(x => x.ObjectId == "parent");
            var start = item.transform.position; var local = item.transform.localPosition;
            session.axis = SpaceCanvasEditAxis.X;
            Assert.IsTrue(session.BeginGesture(item, start, Quaternion.identity));
            session.UpdateGesture(start + item.transform.parent.TransformVector(Vector3.right * .5f), Quaternion.identity);
            session.EndGesture(false);
            Assert.That(Vector3.Distance(item.transform.localPosition, local + Vector3.right * .5f), Is.LessThan(.00001f));
            Assert.AreEqual(1, SpaceCanvasUndoService.Count);
            Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out var error), error); Assert.AreEqual(json, ObjectsJson());
        }

        [Test] public void TopViewPreviewConfirmsAtSameWorldAndLocalPoseAndCanBeUndone()
        {
            view.Show(SpaceCanvasWorkspaceView.Preset.Top, false);
            var parent = SpaceCanvasRuntime.CurrentProjectRoot.transform;
            placement.Begin("primitive.cube");
            var contact = parent.TransformPoint(new Vector3(-2, 0, -1));
            Assert.IsTrue(placement.UpdateRay(1, new Ray(contact + parent.up, -parent.up)));
            var ghost = (SpaceCanvasEditableObject)typeof(SpaceCanvasPlacement).GetField("ghost", Private).GetValue(placement);
            var local = ghost.transform.localPosition; var world = ghost.transform.position;
            Assert.IsTrue(placement.Confirm(1));
            var added = SpaceCanvasRuntime.GetEditableObjects().Single(x => x.ObjectId != "parent" && x.ObjectId != "child");
            Assert.That(Vector3.Distance(local, added.transform.localPosition), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(world, added.transform.position), Is.LessThan(.00001f));
            Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out _)); Assert.AreEqual(2, SpaceCanvasRuntime.GetEditableObjects().Count);
        }

        [Test] public void ViewChangeCancelsPreviewAndRebindingDoesNotDuplicateButtonsOrListeners()
        {
            placement.Begin("primitive.cube"); Assert.IsTrue(placement.IsPlacing);
            view.Show(SpaceCanvasWorkspaceView.Preset.Side, false); Assert.IsFalse(placement.IsPlacing);
            view.BuildOrBindControls(panels.transform, null); view.BuildOrBindControls(panels.transform, null);
            var buttons = panels.GetComponentsInChildren<Button>(); Assert.AreEqual(7, buttons.Length);
            view.ResetView(); buttons.Single(b => b.name == "右转").onClick.Invoke(); Assert.AreEqual(45, view.Yaw);
            var dock = panels.transform.Find("工作空间视角"); var corners = new Vector3[4];
            foreach (var button in buttons)
            {
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var corner in corners)
                    Assert.IsTrue(((RectTransform)dock).rect.Contains(dock.InverseTransformPoint(corner)), button.name);
            }
        }
    }
}
