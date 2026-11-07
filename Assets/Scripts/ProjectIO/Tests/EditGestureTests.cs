using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Tests
{
    public sealed class EditGestureTests
    {
        private GameObject host;
        private Transform oldContent;
        private SpaceCanvasProjectRoot oldProject;
        private SpaceCanvasEditSession session;
        private SpaceCanvasEditableObject item;
        [SetUp]
        public void Setup()
        {
            oldContent = SpaceCanvasRuntime.ContentRoot; oldProject = SpaceCanvasRuntime.CurrentProjectRoot;
            host = new GameObject("Gesture Test"); host.transform.localScale = Vector3.one * .14f;
            SpaceCanvasRuntime.ContentRoot = host.transform; SpaceCanvasRuntime.SetCurrentProjectRoot(null);
            SpaceCanvasUndoService.Clear();
            var project = new SpaceCanvasProject();
            project.objects.Add(new SceneObject {id="parent", assetId="primitive.cube", localPosition=new[]{2f,0f,0f}});
            project.objects.Add(new SceneObject {id="child", assetId="primitive.cube", parentId="parent", localPosition=new[]{1f,0f,0f}});
            Assert.IsTrue(SceneImporter.ImportProject(project, out var error, false), error);
            item = SpaceCanvasRuntime.GetEditableObjects().Single(x=>x.ObjectId=="child");
            session = host.AddComponent<SpaceCanvasEditSession>();
        }
        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
            SpaceCanvasRuntime.ContentRoot = oldContent; SpaceCanvasRuntime.SetCurrentProjectRoot(oldProject);
            SpaceCanvasUndoService.Clear();
        }
        [Test]
        public void TutorialEditEvidenceRequiresCommittedMovementAndExcludesCancellation()
        {
            var count = 0;
            session.EditCommitted += edited => { Assert.That(edited, Is.SameAs(item)); count++; };
            session.BeginGesture(item, Vector3.zero, Quaternion.identity);
            session.EndGesture(false);
            Assert.That(count, Is.Zero);
            session.BeginGesture(item, Vector3.zero, Quaternion.identity);
            session.UpdateGesture(Vector3.right * .03f, Quaternion.identity);
            session.EndGesture(true);
            Assert.That(count, Is.Zero);
            session.BeginGesture(item, Vector3.zero, Quaternion.identity);
            session.UpdateGesture(Vector3.right * .03f, Quaternion.identity);
            session.EndGesture(false);
            Assert.That(count, Is.EqualTo(1));
            session.EndGesture(false);
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void MoveUsesParentCoordinatesAndCreatesExactlyOneUndo()
        {
            Assert.IsTrue(session.BeginGesture(item, Vector3.zero, Quaternion.identity));
            for (var i=1;i<=10;i++) session.UpdateGesture(new Vector3(.014f*i,0,0), Quaternion.identity);
            session.EndGesture(false);
            Assert.That(item.transform.localPosition.x, Is.EqualTo(2f).Within(.0001f));
            Assert.That(SpaceCanvasUndoService.Count, Is.EqualTo(1));
            Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out var error), error);
            item = SpaceCanvasRuntime.GetEditableObjects().Single(x=>x.ObjectId=="child");
            Assert.That(item.transform.localPosition.x, Is.EqualTo(1f).Within(.0001f));
        }
        [Test]
        public void TrackingLossCancelsWithoutAddingUndo()
        {
            var initial = item.transform.position;
            session.BeginGesture(item, Vector3.zero, Quaternion.identity);
            session.UpdateGesture(Vector3.up*.2f, Quaternion.identity);
            session.EndGesture(true);
            Assert.That(Vector3.Distance(item.transform.position, initial), Is.LessThan(.0001f));
            Assert.That(SpaceCanvasUndoService.Count, Is.Zero);
        }
        [Test]
        public void InvalidPointerAndExternalObjectsCannotStartOrChangeGesture()
        {
            Assert.IsFalse(session.BeginGesture(item, new Vector3(float.NaN,0,0), Quaternion.identity));
            Assert.IsFalse(session.BeginGesture(null, Vector3.zero, Quaternion.identity));
            session.BeginGesture(item, Vector3.zero, Quaternion.identity);
            var position = item.transform.position;
            session.UpdateGesture(new Vector3(float.PositiveInfinity,0,0), Quaternion.identity);
            Assert.That(item.transform.position, Is.EqualTo(position));
            session.EndGesture(false); Assert.That(SpaceCanvasUndoService.Count, Is.Zero);
        }
        [Test]
        public void RotationAndScaleCanBothBeUndone()
        {
            session.tool = SpaceCanvasEditTool.Rotate;
            session.BeginGesture(item, Vector3.zero, Quaternion.identity);
            session.UpdateGesture(Vector3.zero, Quaternion.Euler(0,90,0)); session.EndGesture(false);
            Assert.That(Quaternion.Angle(item.transform.rotation, Quaternion.Euler(0,90,0)), Is.LessThan(.01f));
            session.tool = SpaceCanvasEditTool.Scale;
            var p=item.transform.position;
            session.BeginGesture(item, p+Vector3.right*.1f, Quaternion.identity);
            session.UpdateGesture(p+Vector3.right*.2f, Quaternion.identity); session.EndGesture(false);
            Assert.That(item.transform.localScale.x, Is.EqualTo(2f).Within(.001f));
            Assert.That(SpaceCanvasUndoService.Count, Is.EqualTo(2));
            Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out _));
            item=SpaceCanvasRuntime.GetEditableObjects().Single(x=>x.ObjectId=="child");
            Assert.That(item.transform.localScale.x, Is.EqualTo(1f).Within(.001f));
            Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out _));
            item=SpaceCanvasRuntime.GetEditableObjects().Single(x=>x.ObjectId=="child");
            Assert.That(Quaternion.Angle(item.transform.rotation, Quaternion.identity), Is.LessThan(.01f));
        }
    }
}
