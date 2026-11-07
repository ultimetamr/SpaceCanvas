using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceCanvas.ProjectIO.Tests
{
    /// <summary>Behavior regressions for real ray geometry, transactional editing, copying and contextual UI.</summary>
    public sealed class InteractionPolishTests
    {
        private GameObject host;
        private Transform previousContent;
        private SpaceCanvasProjectRoot previousProject;
        private SpaceCanvasEditableObject item;
        private SpaceCanvasEditSession session;
        private SpaceCanvasTransformHandles handles;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void Setup()
        {
            previousContent = SpaceCanvasRuntime.ContentRoot; previousProject = SpaceCanvasRuntime.CurrentProjectRoot;
            host = new GameObject("Interaction polish validation"); host.transform.localScale = Vector3.one * .16f;
            SpaceCanvasRuntime.ContentRoot = host.transform; SpaceCanvasRuntime.SetCurrentProjectRoot(null); SpaceCanvasUndoService.Clear();
            var data = new SpaceCanvasProject(); data.objects.Add(new SceneObject { id = "chair", assetId = "primitive.cube" });
            Assert.IsTrue(SceneImporter.ImportProject(data, out var error, false), error);
            item = SpaceCanvasRuntime.GetEditableObjects().Single();
            session = host.AddComponent<SpaceCanvasEditSession>(); Invoke(session,"Awake"); handles = session.Handles;
            Invoke(handles,"Awake"); session.Select(item); Physics.SyncTransforms();
        }
        [TearDown] public void Cleanup()
        {
            var workspace = host.GetComponent<SpaceCanvasWorkspaceUI>();
            if (workspace != null && workspace.ViewRoot != null) Object.DestroyImmediate(workspace.ViewRoot.gameObject);
            foreach (var editable in host.GetComponentsInChildren<SpaceCanvasEditableObject>(true)) Invoke(editable,"ReleaseOwnedMaterials");
            Object.DestroyImmediate(host); SpaceCanvasRuntime.ContentRoot = previousContent;
            SpaceCanvasRuntime.SetCurrentProjectRoot(previousProject); SpaceCanvasUndoService.Clear();
        }
        private static void Invoke(object target, string method) => target.GetType().GetMethod(method,Private).Invoke(target,null);
        private Ray Aim(Vector3 point) => new Ray(point + Vector3.back, Vector3.forward);
        private SpaceCanvasTransformHandles.Hit Grip(SpaceCanvasEditAxis axis, Vector3 point) => new SpaceCanvasTransformHandles.Hit { axis=axis, point=point, distance=1 };

        [Test] public void AxisGeometryRejectsEndOnAndNonFiniteRays()
        {
            Assert.IsFalse(SpaceCanvasHandleMath.AxisParameter(new Ray(Vector3.back,Vector3.forward),Vector3.zero,Vector3.forward,out _));
            Assert.IsFalse(SpaceCanvasHandleMath.AxisParameter(new Ray(new Vector3(float.NaN,0,0),Vector3.forward),Vector3.zero,Vector3.right,out _));
            Assert.IsTrue(SpaceCanvasHandleMath.AxisParameter(Aim(Vector3.right*.2f),Vector3.zero,Vector3.right,out var parameter));
            Assert.That(parameter,Is.EqualTo(.2f).Within(.0001));
        }
        [Test] public void RaySegmentClampsToFiniteEndpointAndRejectsPointsBehindRay()
        {
            var error = SpaceCanvasHandleMath.RaySegment(new Ray(new Vector3(2,0,-1),Vector3.forward),Vector3.zero,Vector3.right,out var distance,out var point);
            Assert.That(error,Is.EqualTo(1).Within(.0001)); Assert.That(distance,Is.EqualTo(1).Within(.0001)); Assert.AreEqual(Vector3.right,point);
            error = SpaceCanvasHandleMath.RaySegment(new Ray(Vector3.forward,Vector3.forward),Vector3.zero,Vector3.right,out distance,out _);
            Assert.That(distance,Is.Zero); Assert.That(error,Is.GreaterThanOrEqualTo(1));
        }
        [Test] public void MoveHandleUsesLocalMetersAndProducesOneUndoForWholeDrag()
        {
            var point = Vector3.right*.12f;
            Assert.IsTrue(handles.BeginDrag(1,Grip(SpaceCanvasEditAxis.X,point),Aim(point),Quaternion.identity));
            Assert.IsFalse(handles.BeginDrag(2,Grip(SpaceCanvasEditAxis.X,point),Aim(point),Quaternion.identity));
            handles.Drag(2,Aim(Vector3.right*.8f)); Assert.That(item.transform.localPosition.x,Is.Zero);
            for(var i=1;i<=6;i++) handles.Drag(1,Aim(point+Vector3.right*i*.01f));
            Assert.That(item.transform.localPosition.x,Is.EqualTo(.375f).Within(.001));
            Assert.That(item.transform.localPosition.y,Is.Zero); Assert.That(item.transform.localPosition.z,Is.Zero);
            handles.EndDrag(1,false); Assert.AreEqual(1,SpaceCanvasUndoService.Count); Assert.AreEqual(SpaceCanvasEditAxis.Free,session.axis);
            Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out _));
            Assert.That(SpaceCanvasRuntime.GetEditableObjects().Single().transform.localPosition,Is.EqualTo(Vector3.zero));
        }
        [Test] public void CancelOrReturnToOriginalPoseDoesNotConsumeUndo()
        {
            var p=Vector3.right*.12f;
            Assert.IsTrue(handles.BeginDrag(1,Grip(SpaceCanvasEditAxis.X,p),Aim(p),Quaternion.identity));
            handles.Drag(1,Aim(p+Vector3.right*.06f)); handles.EndDrag(1,true);
            Assert.That(item.transform.localPosition.magnitude,Is.LessThan(.0001)); Assert.AreEqual(0,SpaceCanvasUndoService.Count);
            Assert.IsTrue(handles.BeginDrag(1,Grip(SpaceCanvasEditAxis.X,p),Aim(p),Quaternion.identity));
            handles.Drag(1,Aim(p+Vector3.right*.06f)); handles.Drag(1,Aim(p)); handles.EndDrag(1,false);
            Assert.AreEqual(0,SpaceCanvasUndoService.Count);
        }
        [Test] public void RotationRingTracksRayAroundPlaneAndSupportsSnapping()
        {
            session.tool=SpaceCanvasEditTool.Rotate; session.snapToGrid=true;
            var from=Vector3.right*.12f; var target=Quaternion.Euler(0,38,0)*from;
            Assert.IsTrue(handles.BeginDrag(1,Grip(SpaceCanvasEditAxis.Y,from),new Ray(from+Vector3.up,Vector3.down),Quaternion.identity));
            handles.Drag(1,new Ray(target+Vector3.up,Vector3.down)); handles.EndDrag(1,false);
            Assert.That(Quaternion.Angle(item.transform.rotation,Quaternion.Euler(0,45,0)),Is.LessThan(.01)); Assert.AreEqual(1,SpaceCanvasUndoService.Count);
        }
        [Test] public void ScaleHandlePreservesProportionsAndLeavesCollisionSettingUntouched()
        {
            item.transform.localScale=new Vector3(1,2,3); item.ColliderEnabled=false; session.tool=SpaceCanvasEditTool.Scale;
            var p=Vector3.right*.2f;
            Assert.IsTrue(handles.BeginDrag(1,Grip(SpaceCanvasEditAxis.X,p),Aim(p),Quaternion.identity));
            handles.Drag(1,Aim(p+Vector3.right*.05f)); handles.EndDrag(1,false);
            Assert.That(item.transform.localScale.y/item.transform.localScale.x,Is.EqualTo(2).Within(.001));
            Assert.That(item.transform.localScale.z/item.transform.localScale.x,Is.EqualTo(3).Within(.001));
            Assert.That(item.transform.localScale.x,Is.GreaterThan(1)); Assert.IsFalse(item.ColliderEnabled);
        }
        [Test] public void HandlesCannotBePickedThroughForegroundGeometryOrOutsideWorkspace()
        {
            var p=Vector3.right*.12f; var ray=Aim(p);
            Assert.IsTrue(handles.Raycast(ray,2,out _));
            var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.transform.SetParent(host.transform,false);
            blocker.transform.position=ray.GetPoint(.5f); blocker.transform.localScale=Vector3.one;
            Physics.SyncTransforms(); Assert.IsFalse(handles.Raycast(ray,2,out _));
            blocker.SetActive(false); Physics.SyncTransforms();
            item.gameObject.SetActive(false); Assert.IsFalse(handles.Raycast(ray,2,out _));
        }
        [Test] public void ContextualInspectorHasExclusiveTabsAndNoAutomaticObjectSelection()
        {
            var workspace=BuildWorkspace();
            workspace.SelectObject(null); Assert.IsNull(workspace.Selected);
            Assert.That(workspace.ViewRoot.GetComponentsInChildren<Slider>().Length,Is.Zero);
            workspace.SelectObject(item);
            Assert.That(workspace.ViewRoot.GetComponentsInChildren<Slider>().Length,Is.Zero);
            workspace.ViewRoot.GetComponentsInChildren<Button>().Single(x=>x.name=="外观页").onClick.Invoke();
            Assert.That(workspace.ViewRoot.GetComponentsInChildren<Slider>().Length,Is.EqualTo(2));
            Assert.IsFalse(workspace.ViewRoot.GetComponentsInChildren<Button>().Any(x=>x.name=="增加 0"));
            workspace.ViewRoot.GetComponentsInChildren<Button>().Single(x=>x.name=="变换页").onClick.Invoke();
            Assert.That(workspace.ViewRoot.GetComponentsInChildren<Slider>().Length,Is.Zero);
            Assert.IsTrue(workspace.ViewRoot.GetComponentsInChildren<Button>().Any(x=>x.name=="增加 0"));
        }
        [Test] public void AppearancePresetSurvivesExportUndoAndDoesNotModifyAnotherObject()
        {
            var workspace=BuildWorkspace(); workspace.SelectObject(item);
            Assert.IsTrue(workspace.ApplyAppearance("#A9C6AD",.8f,.3f));
            Assert.AreEqual(1,SpaceCanvasUndoService.Count);
            Assert.IsTrue(SceneExporter.TryCaptureCurrentScene(null,out var captured,out _));
            var json=ProjectSerializer.Serialize(captured); var data=ProjectSerializer.Deserialize(json).objects.Single();
            Assert.AreEqual("#A9C6ADFF",data.colorHex); Assert.AreEqual(.8f,data.alpha); Assert.AreEqual(.3f,data.metallic);
            Assert.IsTrue(workspace.ApplyAppearance("#A9C6AD",.8f,.3f)); Assert.AreEqual(1,SpaceCanvasUndoService.Count);
            Assert.IsFalse(workspace.ApplyAppearance("broken",1,0)); Assert.AreEqual(1,SpaceCanvasUndoService.Count);
            Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out _)); Assert.AreEqual(1,SpaceCanvasRuntime.GetEditableObjects().Single().Alpha);
        }
        [Test] public void DuplicateSubtreeRemapsIdsAndOneUndoRestoresOriginalHierarchy()
        {
            var child=SpaceCanvasAssetLibrary.CreateInstance("primitive.sphere",item.transform,out _).GetComponent<SpaceCanvasEditableObject>();
            child.Apply(new SceneObject{id="child",assetId="primitive.sphere",parentId=item.ObjectId,localPosition=new[]{0f,2f,0f}});
            var workspace=BuildWorkspace(); workspace.SelectObject(item); var sourceId=item.ObjectId;
            Assert.IsTrue(workspace.DuplicateSelected());
            var items=SpaceCanvasRuntime.GetEditableObjects(); Assert.AreEqual(4,items.Count);
            Assert.AreEqual(4,items.Select(x=>x.ObjectId).Distinct().Count()); Assert.AreNotEqual(sourceId,workspace.Selected.ObjectId);
            var copiedChild=workspace.Selected.GetComponentsInChildren<SpaceCanvasEditableObject>().Single(x=>x!=workspace.Selected);
            Assert.AreEqual(new Vector3(0,2,0),copiedChild.transform.localPosition); Assert.AreNotEqual("child",copiedChild.ObjectId);
            Assert.AreEqual(1,SpaceCanvasUndoService.Count); Assert.IsTrue(SceneExporter.TryCaptureCurrentScene(null,out var exported,out _));
            Assert.IsTrue(SceneImporter.ImportProject(ProjectSerializer.Deserialize(ProjectSerializer.Serialize(exported)),out _,false));
            Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out _)); Assert.AreEqual(2,SpaceCanvasRuntime.GetEditableObjects().Count);
        }
        private SpaceCanvasWorkspaceUI BuildWorkspace()
        {
            var workspace=host.AddComponent<SpaceCanvasWorkspaceUI>(); workspace.fullSpaceLayout=true;
            workspace.editSession=session; session.workspaceUI=workspace; workspace.BuildInterface(); return workspace;
        }
    }
}
