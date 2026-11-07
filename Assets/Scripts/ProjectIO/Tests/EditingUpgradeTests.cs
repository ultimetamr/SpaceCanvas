using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceCanvas.ProjectIO.Tests
{
    public sealed class RecoveryJournalTests
    {
        private string directory;
        [SetUp] public void Setup() { directory = Path.Combine(Path.GetTempPath(), "SpaceCanvasRecoveryTest-" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [Test] public void InterruptedLatestSlotFallsBackToPreviousCompleteProject()
        {
            var store = new SpaceCanvasRecoveryStore(directory);
            store.Write(new SpaceCanvasProject { projectName = "first" });
            store.Write(new SpaceCanvasProject { projectName = "second" });
            File.WriteAllText(Path.Combine(directory, "recovery-0.json"), "{broken");
            Assert.IsTrue(store.TryRead(out var recovered)); Assert.AreEqual("first", recovered.project.projectName);
            store.Write(new SpaceCanvasProject { projectName = "third" });
            Assert.IsTrue(store.TryRead(out recovered)); Assert.AreEqual("third", recovered.project.projectName);
            store.Clear(); Assert.IsFalse(store.TryRead(out _));
        }
        [Test] public void FingerprintIgnoresCaptureTimeButTracksAppearanceAndDoesNotMutateInput()
        {
            var project = new SpaceCanvasProject { updateTime = 123 }; project.objects.Add(new SceneObject { id = "one" });
            var fingerprint = SpaceCanvasRecoveryStore.Fingerprint(project); Assert.AreEqual(123, project.updateTime);
            project.updateTime = 456; Assert.AreEqual(fingerprint, SpaceCanvasRecoveryStore.Fingerprint(project));
            project.objects[0].alpha = .2f; Assert.AreNotEqual(fingerprint, SpaceCanvasRecoveryStore.Fingerprint(project));
        }
        [Test] public void BackupRetainsProjectIdentityAndNeverOverwritesAnotherBackup()
        {
            var data = new SpaceCanvasProject { projectName = "My room", createTime = 12 };
            var first = SpaceCanvasRecoveryStore.Backup(directory, data); var second = SpaceCanvasRecoveryStore.Backup(directory, data);
            Assert.AreNotEqual(first, second); var read = ProjectSerializer.Deserialize(File.ReadAllText(first));
            Assert.AreEqual("My room", read.projectName); Assert.AreEqual(12, read.createTime);
        }
        [Test] public void InvalidNewSnapshotCannotReplaceTheLastGoodRecovery()
        {
            var store = new SpaceCanvasRecoveryStore(directory); store.Write(new SpaceCanvasProject { projectName = "good" });
            var invalid = new SpaceCanvasProject(); invalid.objects.Add(new SceneObject { id = "invalid", localPosition = new[] { float.NaN, 0f, 0f } });
            Assert.Catch(() => store.Write(invalid)); Assert.IsTrue(store.TryRead(out var entry)); Assert.AreEqual("good", entry.project.projectName);
        }
    }

    public sealed class EditingUpgradeTests
    {
        private GameObject root;
        private Transform previousContent;
        private SpaceCanvasProjectRoot previousProject;
        [SetUp] public void Setup()
        {
            previousContent = SpaceCanvasRuntime.ContentRoot; previousProject = SpaceCanvasRuntime.CurrentProjectRoot;
            root = new GameObject("Editing validation"); SpaceCanvasRuntime.ContentRoot = root.transform; SpaceCanvasRuntime.SetCurrentProjectRoot(null); SpaceCanvasUndoService.Clear();
            Assert.IsTrue(SceneImporter.ImportProject(new SpaceCanvasProject(), out _, false));
        }
        [TearDown] public void Cleanup()
        {
            foreach (var item in root.GetComponentsInChildren<SpaceCanvasEditableObject>(true))
                typeof(SpaceCanvasEditableObject).GetMethod("ReleaseOwnedMaterials", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(item, null);
            UnityEngine.Object.DestroyImmediate(root); SpaceCanvasRuntime.ContentRoot = previousContent; SpaceCanvasRuntime.SetCurrentProjectRoot(previousProject); SpaceCanvasUndoService.Clear();
        }
        [Test] public void PlacementCancellationDoesNotExportPreviewOrConsumeUndo()
        {
            var workspace = root.AddComponent<SpaceCanvasWorkspaceUI>(); var placement = root.AddComponent<SpaceCanvasPlacement>(); placement.workspace = workspace;
            var before = SpaceCanvasRuntime.GetEditableObjects().Count;
            placement.Begin("primitive.cube"); Assert.IsTrue(placement.IsPlacing);
            placement.UpdateRay(1, new Ray(new Vector3(0, 2, 0), Vector3.down));
            Assert.IsTrue(placement.HasValidPose);
            Assert.IsTrue(SceneExporter.TryCaptureCurrentScene(null, out var data, out _)); Assert.AreEqual(before, data.objects.Count);
            placement.Cancel(); Assert.IsFalse(placement.IsPlacing); Assert.AreEqual(0, SpaceCanvasUndoService.Count);
        }
        [Test] public void PreviewPointerLeasePreventsTwoHandFlickerAndRejectsInvalidConfirm()
        {
            var workspace = root.AddComponent<SpaceCanvasWorkspaceUI>(); var placement = root.AddComponent<SpaceCanvasPlacement>(); placement.workspace = workspace;
            placement.Begin("primitive.cube");
            Assert.IsFalse(placement.Confirm(1));
            Assert.IsTrue(placement.UpdateRay(1, new Ray(Vector3.up * 2, Vector3.down)));
            Assert.IsFalse(placement.UpdateRay(2, new Ray(Vector3.one * 2, Vector3.down)));
            Assert.IsFalse(placement.Confirm(2)); placement.Cancel();
        }
        [Test] public void TransparentPreviewConfirmsOnceAtPreviewPoseAndUndoRemovesOnlyThePlacedAsset()
        {
            var template = Resources.Load<Material>("SpaceCanvasMaterials/PlacementPreview");
            Assert.IsNotNull(template, "The Android build must retain an authored transparent shader variant.");
            var workspace = root.AddComponent<SpaceCanvasWorkspaceUI>();
            var placement = root.AddComponent<SpaceCanvasPlacement>(); placement.workspace = workspace;
            placement.Begin("furniture.chair");
            Assert.IsTrue(placement.UpdateRay(1, new Ray(new Vector3(.4f, 2, .3f), Vector3.down)));
            var ghost = (SpaceCanvasEditableObject)typeof(SpaceCanvasPlacement).GetField("ghost", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(placement);
            var previewPosition = ghost.transform.localPosition;
            var previewMaterials = ghost.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).ToArray();
            Assert.IsNotEmpty(previewMaterials);
            foreach (var material in previewMaterials)
            {
                Assert.AreEqual(template.shader, material.shader);
                Assert.That(material.GetColor("_BaseColor").a, Is.EqualTo(.38f).Within(.0001f));
                Assert.AreEqual(1f, material.GetFloat("_Surface"));
                Assert.AreEqual(0f, material.GetFloat("_ZWrite"));
                Assert.IsTrue(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));
            }
            Assert.IsTrue(ghost.GetComponentsInChildren<Collider>(true).All(c => !c.enabled));
            Assert.AreEqual(0, SpaceCanvasRuntime.GetEditableObjects().Count);
            Assert.IsTrue(placement.Confirm(1));
            Assert.IsFalse(placement.Confirm(1), "A held trigger cannot create the same asset twice.");
            var placed = SpaceCanvasRuntime.GetEditableObjects().Single();
            Assert.AreEqual(previewPosition, placed.transform.localPosition);
            Assert.AreEqual(1f, placed.Alpha);
            Assert.IsTrue(placed.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .All(m => !m.HasProperty("_Surface") || m.GetFloat("_Surface") == 0f));
            foreach (var material in previewMaterials) Assert.IsTrue(material == null, "Preview clones must be released.");
            Assert.AreEqual(1, SpaceCanvasUndoService.Count);
            Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out var error), error);
            Assert.AreEqual(0, SpaceCanvasRuntime.GetEditableObjects().Count);
        }
        [Test] public void GroundingAccountsForCenteredAndBottomPivotsAndNegativeScale()
        {
            var contact = new Vector3(2, 1, 3);
            Assert.AreEqual(1.5f, SpaceCanvasGeometry.GroundedPosition(contact, new Bounds(Vector3.zero, Vector3.one), Vector3.one).y);
            Assert.AreEqual(1f, SpaceCanvasGeometry.GroundedPosition(contact, new Bounds(Vector3.up * .5f, Vector3.one), Vector3.one).y);
            Assert.AreEqual(2f, SpaceCanvasGeometry.GroundedPosition(contact, new Bounds(Vector3.up * .5f, Vector3.one), new Vector3(1,-1,1)).y);
        }
        [Test] public void ConstrainedMoveRespectsRotatedScaledParentAndUntouchedCoordinates()
        {
            var item = SpaceCanvasAssetLibrary.CreateInstance("primitive.cube", SpaceCanvasRuntime.CurrentProjectRoot.transform, out _).GetComponent<SpaceCanvasEditableObject>();
            item.transform.parent.localRotation = Quaternion.Euler(0, 90, 0); item.transform.parent.localScale = Vector3.one * .2f;
            item.transform.localPosition = new Vector3(.13f,.17f,.19f);
            var session = root.AddComponent<SpaceCanvasEditSession>(); session.axis = SpaceCanvasEditAxis.X; session.snapToGrid = true;
            Assert.IsTrue(session.BeginGesture(item, Vector3.zero, Quaternion.identity));
            session.UpdateGesture(new Vector3(.3f,.2f,-.2f), Quaternion.identity); session.EndGesture(false);
            Assert.AreEqual(.17f,item.transform.localPosition.y); Assert.AreEqual(.19f,item.transform.localPosition.z);
            Assert.That(item.transform.localPosition.x/.25f, Is.EqualTo(Mathf.Round(item.transform.localPosition.x/.25f)).Within(.0001));
            Assert.AreEqual(1,SpaceCanvasUndoService.Count);
        }
        [Test] public void ConstrainedRotationSnapsToFifteenDegrees()
        {
            var item = SpaceCanvasAssetLibrary.CreateInstance("primitive.cube", SpaceCanvasRuntime.CurrentProjectRoot.transform, out _).GetComponent<SpaceCanvasEditableObject>();
            var session = root.AddComponent<SpaceCanvasEditSession>(); session.tool = SpaceCanvasEditTool.Rotate; session.axis = SpaceCanvasEditAxis.Y; session.snapToGrid = true;
            session.BeginGesture(item, Vector3.zero, Quaternion.identity); session.UpdateGesture(Vector3.zero, Quaternion.Euler(0,22,0)); session.EndGesture(false);
            Assert.That(Quaternion.Angle(item.transform.rotation, Quaternion.Euler(0,15,0)),Is.LessThan(.01));
        }
        [Test] public void SliderProducesOneUndoForContinuousChangesAndNoUndoWhenReturningToInitialValue()
        {
            var item = SpaceCanvasAssetLibrary.CreateInstance("primitive.cube", SpaceCanvasRuntime.CurrentProjectRoot.transform, out _).GetComponent<SpaceCanvasEditableObject>();
            var go = new GameObject("Slider", typeof(RectTransform), typeof(Slider), typeof(SpaceCanvasScalarSlider)); go.transform.SetParent(root.transform);
            var transaction = go.GetComponent<SpaceCanvasScalarSlider>(); transaction.target = item; transaction.opacity = true; transaction.slider = go.GetComponent<Slider>(); transaction.Bind();
            for(var i=1;i<8;i++) transaction.Preview(1-i*.1f); transaction.Finish();
            Assert.AreEqual(1,SpaceCanvasUndoService.Count); Assert.That(item.Alpha,Is.EqualTo(.3f).Within(.0001));
            transaction.Preview(.8f); transaction.Preview(.3f); transaction.Finish(); Assert.AreEqual(1,SpaceCanvasUndoService.Count);
        }
        [Test] public void HundredObjectProjectRoundTripsAndSingleUndoRestoresPreviousScene()
        {
            var data = new SpaceCanvasProject { projectName = "100 object stress", createTime = 1 };
            for(var i=0;i<100;i++) data.objects.Add(new SceneObject { id="stress-"+i,assetId="primitive.cube",localPosition=new[]{(i%10)*.3f,.15f,(i/10)*.3f},localScale=new[]{.2f,.2f,.2f} });
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.IsTrue(SceneImporter.ImportProject(data,out var error,true),error); Assert.IsTrue(SceneExporter.TryCaptureCurrentScene(null,out var exported,out error),error);
            Assert.AreEqual(100,exported.objects.Count); Assert.AreEqual(SpaceCanvasRecoveryStore.Fingerprint(data),SpaceCanvasRecoveryStore.Fingerprint(exported));
            Assert.AreEqual(1,SpaceCanvasUndoService.Count); Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out error),error); Assert.AreEqual(0,SpaceCanvasRuntime.GetEditableObjects().Count);
            Debug.Log("SpaceCanvas editor stress: 100 objects import/export/undo " + clock.ElapsedMilliseconds + " ms; editor CPU only, not device performance.");
        }
        [Test] public void ComfortValuesRejectNonFiniteAndClampWithinReach()
        {
            Assert.AreEqual(1.5f,SpaceCanvasWorkspaceSettingsUI.Sanitize(float.NaN,1.5f,1.2f,2.2f));
            Assert.AreEqual(2.2f,SpaceCanvasWorkspaceSettingsUI.Sanitize(99,1.5f,1.2f,2.2f));
        }

        [Test] public void DisabledPictureCanBePickedMovedAndUndoneWithoutEnablingCollision()
        {
            var parent = new GameObject("Rotated mirrored parent"); parent.transform.SetParent(root.transform, false);
            parent.transform.SetLocalPositionAndRotation(new Vector3(0, 2, 0), Quaternion.Euler(10, 30, 0));
            parent.transform.localScale = new Vector3(-.3f, .4f, .2f);
            var picture = SpaceCanvasAssetLibrary.CreateInstance("decor.picture", parent.transform, out _).GetComponent<SpaceCanvasEditableObject>();
            picture.Apply(new SceneObject { id = "picture", assetId = "decor.picture", colliderEnabled = false, localScale = new[] { .8f, .8f, .2f } });
            var center = picture.transform.TransformPoint(SpaceCanvasGeometry.LocalBounds(picture).center);
            var normal = picture.transform.forward;
            Physics.SyncTransforms();
            Assert.IsTrue(SpaceCanvasPicking.Raycast(new Ray(center + normal, -normal), 2, out var hit));
            Assert.AreSame(picture, hit.item); Assert.That(hit.distance, Is.InRange(.9f, 1.1f));
            var edit = root.AddComponent<SpaceCanvasEditSession>();
            var start = picture.transform.localPosition;
            Assert.IsTrue(edit.BeginGesture(hit.item, hit.point, Quaternion.identity));
            edit.UpdateGesture(hit.point + Vector3.up * .1f, Quaternion.identity); edit.EndGesture(false);
            Assert.AreNotEqual(start, picture.transform.localPosition);
            Assert.IsFalse(picture.ToSceneObject().colliderEnabled);
            foreach (var collider in picture.GetComponentsInChildren<Collider>(true)) Assert.IsFalse(collider.enabled);
            Assert.AreEqual(1, SpaceCanvasUndoService.Count);
            Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out var error), error);
            var restored = SpaceCanvasRuntime.GetEditableObjects().Single(x => x.ObjectId == "picture");
            Assert.That(restored.transform.localPosition, Is.EqualTo(start)); Assert.IsFalse(restored.ColliderEnabled);
        }

        [Test] public void DisabledCollisionPickingHonorsPhysicalOcclusionInactiveStateAndWorkspaceScope()
        {
            var picture = SpaceCanvasAssetLibrary.CreateInstance("decor.picture", root.transform, out _).GetComponent<SpaceCanvasEditableObject>();
            picture.Apply(new SceneObject { assetId = "decor.picture", colliderEnabled = false });
            var center = picture.transform.TransformPoint(SpaceCanvasGeometry.LocalBounds(picture).center);
            var ray = new Ray(center + Vector3.forward * 2, Vector3.back);
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.transform.SetParent(root.transform, false);
            blocker.transform.position = ray.GetPoint(.5f); blocker.transform.localScale = Vector3.one * .1f;
            Physics.SyncTransforms();
            Assert.IsTrue(SpaceCanvasPicking.Raycast(ray, 3, out var blocked)); Assert.IsNull(blocked.item);
            blocker.SetActive(false); Physics.SyncTransforms();
            Assert.IsTrue(SpaceCanvasPicking.Raycast(ray, 3, out var visible)); Assert.AreSame(picture, visible.item);
            picture.gameObject.SetActive(false); Assert.IsFalse(SpaceCanvasPicking.Raycast(ray, 3, out _));
            picture.gameObject.SetActive(true);
            var otherScope = new GameObject("Other workspace"); otherScope.transform.SetParent(root.transform, false);
            SpaceCanvasRuntime.ContentRoot = otherScope.transform;
            Assert.IsFalse(SpaceCanvasPicking.Raycast(ray, 3, out _)); SpaceCanvasRuntime.ContentRoot = root.transform;
        }
    }
}
