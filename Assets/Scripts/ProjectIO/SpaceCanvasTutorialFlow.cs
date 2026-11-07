namespace SpaceCanvas.ProjectIO
{
    public enum SpaceCanvasTutorialStep { Welcome, AddAsset, EditObject, SaveProject, BrowseExamples, Complete }
    public enum SpaceCanvasTutorialAction { AssetAdded, ObjectEdited, ProjectSaved, ExamplesViewed }

    /// <summary>Ordered evidence-based progression; actions from earlier steps cannot skip later tasks.</summary>
    public sealed class SpaceCanvasTutorialFlow
    {
        public SpaceCanvasTutorialStep Step { get; private set; }
        public bool Active { get; private set; }
        public void Restart() { Step = SpaceCanvasTutorialStep.Welcome; Active = true; }
        public void Dismiss() { Active = false; }
        public bool Continue()
        {
            if (!Active) return false;
            if (Step == SpaceCanvasTutorialStep.Complete) { Dismiss(); return true; }
            Step++;
            return true;
        }
        public bool Record(SpaceCanvasTutorialAction action)
        {
            if (!Active) return false;
            var expected = Step == SpaceCanvasTutorialStep.AddAsset ? SpaceCanvasTutorialAction.AssetAdded
                : Step == SpaceCanvasTutorialStep.EditObject ? SpaceCanvasTutorialAction.ObjectEdited
                : Step == SpaceCanvasTutorialStep.SaveProject ? SpaceCanvasTutorialAction.ProjectSaved
                : SpaceCanvasTutorialAction.ExamplesViewed;
            if (Step < SpaceCanvasTutorialStep.AddAsset || Step > SpaceCanvasTutorialStep.BrowseExamples || action != expected) return false;
            return Continue();
        }
    }
}
