using NUnit.Framework;

namespace SpaceCanvas.ProjectIO.Tests
{
    public sealed class TutorialFlowTests
    {
        [Test]
        public void OnlyCurrentSuccessfulActionAdvancesAndDismissedFlowIgnoresActions()
        {
            var flow = new SpaceCanvasTutorialFlow();
            Assert.IsFalse(flow.Record(SpaceCanvasTutorialAction.AssetAdded));
            flow.Restart();
            Assert.IsFalse(flow.Record(SpaceCanvasTutorialAction.ProjectSaved));
            flow.Continue();
            Assert.IsFalse(flow.Record(SpaceCanvasTutorialAction.ObjectEdited));
            Assert.IsTrue(flow.Record(SpaceCanvasTutorialAction.AssetAdded));
            Assert.IsFalse(flow.Record(SpaceCanvasTutorialAction.AssetAdded));
            Assert.IsTrue(flow.Record(SpaceCanvasTutorialAction.ObjectEdited));
            Assert.IsTrue(flow.Record(SpaceCanvasTutorialAction.ProjectSaved));
            Assert.IsTrue(flow.Record(SpaceCanvasTutorialAction.ExamplesViewed));
            Assert.That(flow.Step, Is.EqualTo(SpaceCanvasTutorialStep.Complete));
            Assert.IsFalse(flow.Record(SpaceCanvasTutorialAction.ExamplesViewed));
            Assert.IsTrue(flow.Active);
            flow.Continue();
            Assert.IsFalse(flow.Active);
            Assert.IsFalse(flow.Record(SpaceCanvasTutorialAction.AssetAdded));
        }

        [Test]
        public void SkipAndReplayCannotLeaveTutorialStuckOrRetainPreviousProgress()
        {
            var flow = new SpaceCanvasTutorialFlow(); flow.Restart();
            for (var i = 0; i < 6; i++) Assert.IsTrue(flow.Continue());
            Assert.IsFalse(flow.Continue());
            flow.Restart();
            Assert.That(flow.Step, Is.EqualTo(SpaceCanvasTutorialStep.Welcome));
            flow.Dismiss();
            Assert.IsFalse(flow.Continue());
            flow.Restart();
            Assert.IsTrue(flow.Active);
            Assert.That(flow.Step, Is.EqualTo(SpaceCanvasTutorialStep.Welcome));
        }
    }
}
