using UnityEngine;

namespace SpaceCanvas.ProjectIO.XR
{
    /// <summary>Presentation lifecycle only; JSON, scene objects and undo remain in the existing services.</summary>
    public sealed class SpaceCanvasMRPresentation : MonoBehaviour
    {
        public SpaceCanvasWorkspaceUI workspace;
        public SpaceCanvasProjectIOUI projectUI;
        public SpaceCanvasFullSpaceRuntime runtime;
        public SpaceCanvasOnboarding onboarding;
        private void OnEnable()
        {
            if(projectUI!=null) projectUI.InterfaceChanged+=OnInterfaceChanged;
            if(workspace!=null) workspace.RecenterRequested+=Recenter;
            if(onboarding!=null) onboarding.ViewChanged+=OnTutorialChanged;
        }
        private void Start() { OnInterfaceChanged(); }
        private void OnInterfaceChanged()
        {
            // A page transition consumes the physical press; opening a card cannot click its contents.
            SpaceCanvasOpenXRPointer.ConsumeUIActivation();
            if (projectUI != null && projectUI.ModalOpen)
            {
                var session = projectUI.GetComponent<SpaceCanvasEditSession>();
                if (session != null && session.Handles != null) session.Handles.CancelDrag();
                if (session != null) session.EndGesture(false);
                workspace?.FinishPropertyEdit();
                workspace?.placement?.Cancel();
            }
            if(workspace!=null&&projectUI!=null) workspace.SetModalVisibility(projectUI.ModalOpen);
        }
        private void Recenter()
        {
            SpaceCanvasOpenXRPointer.ConsumeUIActivation();
            if (workspace != null && workspace.editSession != null)
            { workspace.editSession.Handles?.CancelDrag(); workspace.editSession.EndGesture(true); }
            if(runtime!=null)runtime.RecenterWorkbench();
        }
        private void OnTutorialChanged() { SpaceCanvasOpenXRPointer.ConsumeUIActivation(); }
        private void OnDisable()
        {
            if(projectUI!=null)projectUI.InterfaceChanged-=OnInterfaceChanged;
            if(workspace!=null)workspace.RecenterRequested-=Recenter;
            if(onboarding!=null)onboarding.ViewChanged-=OnTutorialChanged;
        }
    }
}
