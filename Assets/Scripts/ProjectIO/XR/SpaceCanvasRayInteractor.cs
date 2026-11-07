using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SpaceCanvas.ProjectIO.XR
{
    /// <summary>Same deliberate press-edge UI behavior as the reference project; XRI retains drag handling.</summary>
    public sealed class SpaceCanvasRayInteractor : XRRayInteractor
    {
        public override void UpdateUIModel(ref TrackedDeviceModel model)
        {
            base.UpdateUIModel(ref model);
            model.clickOnDown = true;
        }
    }
}
