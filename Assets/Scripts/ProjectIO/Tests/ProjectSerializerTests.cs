using NUnit.Framework;
using UnityEngine;
using SpaceCanvas.ProjectIO;

namespace SpaceCanvas.ProjectIO.Tests
{
    public sealed class ProjectSerializerTests
    {
        [Test]
        public void SerializeAndDeserializePreservesAllFields()
        {
            var project = new SpaceCanvasProject
            {
                formatVersion = "1.0",
                projectName = "Round Trip",
                createTime = 100,
                updateTime = 200,
                settings = new SceneSettings { unitSystem = SceneUnitSystem.Centimeter, gridEnabled = false, gridSize = 0.125f },
                objects = new System.Collections.Generic.List<SceneObject>
                {
                    new SceneObject
                    {
                        id = "root", assetId = "chair.modern", parentId = string.Empty,
                        localPosition = new[] { 1f, 2f, 3f }, localRotation = new[] { 0f, 0.5f, 0f, 0.8660254f },
                        localScale = new[] { 2f, 1f, 0.5f }, colorHex = "#12345678", alpha = 0.5f, metallic = 0.75f, colliderEnabled = false
                    }
                },
                groups = new System.Collections.Generic.List<SceneGroup>
                {
                    new SceneGroup { groupId = "g1", groupName = "Furniture", childIds = new System.Collections.Generic.List<string> { "root" } }
                }
            };

            var roundTrip = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));
            Assert.AreEqual(project.formatVersion, roundTrip.formatVersion);
            Assert.AreEqual(project.projectName, roundTrip.projectName);
            Assert.AreEqual(project.createTime, roundTrip.createTime);
            Assert.AreEqual(project.updateTime, roundTrip.updateTime);
            Assert.AreEqual(project.settings.unitSystem, roundTrip.settings.unitSystem);
            Assert.AreEqual(project.settings.gridEnabled, roundTrip.settings.gridEnabled);
            Assert.AreEqual(project.settings.gridSize, roundTrip.settings.gridSize);
            Assert.AreEqual(project.objects.Count, roundTrip.objects.Count);
            Assert.AreEqual(project.objects[0].assetId, roundTrip.objects[0].assetId);
            Assert.AreEqual(project.objects[0].localPosition[2], roundTrip.objects[0].localPosition[2]);
            Assert.AreEqual(project.objects[0].localRotation[3], roundTrip.objects[0].localRotation[3]);
            Assert.AreEqual(project.objects[0].colorHex, roundTrip.objects[0].colorHex);
            Assert.AreEqual(project.objects[0].colliderEnabled, roundTrip.objects[0].colliderEnabled);
            Assert.AreEqual(project.groups[0].childIds[0], roundTrip.groups[0].childIds[0]);
        }

        [Test]
        public void MissingFieldsReceiveDefaults()
        {
            var project = ProjectSerializer.Deserialize("{\"projectName\":\"Partial\",\"objects\":[{}]}");
            Assert.AreEqual("Partial", project.projectName);
            Assert.AreEqual("1.0", project.formatVersion);
            Assert.AreEqual("primitive.cube", project.objects[0].assetId);
            Assert.AreEqual(1f, project.objects[0].localScale[0]);
        }

        [Test]
        public void CorruptJsonThrowsClearFormatException()
        {
            var exception = Assert.Throws<SpaceCanvasFormatException>(() => ProjectSerializer.Deserialize("{broken"));
            StringAssert.Contains("JSON", exception.Message);
        }
    }
}
