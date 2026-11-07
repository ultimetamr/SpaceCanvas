using UnityEngine;
using UnityEngine.UI;

namespace SpaceCanvas.ProjectIO.XR
{
    /// <summary>A thin cyan ray, amber actionable hover, and a small target ring. Never casts through a nearer object.</summary>
    [RequireComponent(typeof(SpaceCanvasOpenXRPointer),typeof(LineRenderer))]
    public sealed class SpaceCanvasPointerVisual : MonoBehaviour
    {
        private SpaceCanvasOpenXRPointer input;
        private LineRenderer line, ring;
        private readonly Vector3[] ringPoints = new Vector3[25];
        private void Awake()
        {
            input = GetComponent<SpaceCanvasOpenXRPointer>(); line=GetComponent<LineRenderer>();
            line.positionCount=2; line.useWorldSpace=true;
            line.startWidth=.0022f; line.endWidth=.0008f;
            var marker=new GameObject("Pointer target ring"); marker.transform.SetParent(transform,false);
            ring=marker.AddComponent<LineRenderer>(); ring.sharedMaterial=line.sharedMaterial;
            ring.useWorldSpace=true; ring.positionCount=ringPoints.Length; ring.startWidth=ring.endWidth=.001f;
            ring.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows=false; ring.enabled=false;
        }
        private void LateUpdate()
        {
            line.enabled=input.Tracked;
            if(!input.Tracked) {ring.enabled=false;return;}
            var ray=new Ray(transform.position,transform.forward);
            var end=ray.GetPoint(3f); var distance=3f; var valid=false;
            if(input.Interactor.TryGetCurrentUIRaycastResult(out var ui) && ui.gameObject!=null)
            {
                var d=Vector3.Distance(ray.origin,ui.worldPosition);
                if(d<=distance) {distance=d;end=ui.worldPosition;var control=ui.gameObject.GetComponentInParent<Selectable>();valid=control!=null&&control.IsInteractable();}
            }
            if(SpaceCanvasPicking.Raycast(ray,distance,out var hit))
            {end=hit.point;valid=hit.item!=null;}
            var handles = input.session != null ? input.session.Handles : null;
            if (handles != null && handles.Raycast(ray, distance, out var handle))
            { end = handle.point; valid = true; handles.Hover(SpaceCanvasUnityCompatibility.ObjectId(input), handle.axis); }
            var tint=input.Pressed ? new Color(.35f,1f,.76f,.96f) : valid ? new Color(1f,.81f,.45f,.92f) : new Color(.32f,.86f,1f,.52f);
            line.startColor=new Color(tint.r,tint.g,tint.b,tint.a*.42f);line.endColor=tint;
            line.SetPosition(0,ray.GetPoint(.035f));line.SetPosition(1,end);
            ring.enabled=valid;
            if(!valid) return;
            var camera=input.session!=null&&input.session.workspaceUI!=null?input.session.workspaceUI.viewCamera:Camera.main;
            var right=camera!=null?camera.transform.right:transform.right;
            var up=camera!=null?camera.transform.up:transform.up;
            var toward=camera!=null?(camera.transform.position-end).normalized:-transform.forward;
            var center=end+toward*.003f; var radius = input.Pressed ? .004f : .006f;
            for(var i=0;i<ringPoints.Length;i++)
            {var angle=i*Mathf.PI*2/(ringPoints.Length-1);ringPoints[i]=center+(right*Mathf.Cos(angle)+up*Mathf.Sin(angle))*radius;}
            ring.startColor=ring.endColor=tint;ring.SetPositions(ringPoints);
        }
        private void OnDisable() {if(line!=null)line.enabled=false;if(ring!=null)ring.enabled=false;}
    }
}
