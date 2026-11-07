using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Editor
{
    /// <summary>
    /// Creates offline preview sprites from the actual asset library, then saves the MR UI.
    /// Thumbnails ship as small sprites; no extra runtime camera or downloaded UI framework.
    /// </summary>
    [InitializeOnLoad]
    public static class SpaceCanvasUIRefinement
    {
        private const string Request = "Library/SpaceCanvas.RefineUI.request";
        private const string Output = "Assets/Resources/SpaceCanvasPreviews/";
        static SpaceCanvasUIRefinement() { EditorApplication.update += Poll; }
        private static void Poll()
        {
            if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            File.Move(Request,Request+"."+DateTime.UtcNow.Ticks+".started");
            try { Rebuild(); }
            catch(Exception ex) { File.WriteAllText("Library/SpaceCanvas.RefineUI.txt","FAIL\n"+ex); Debug.LogException(ex); }
        }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/界面/生成缩略图并更新精致界面")]
#endif
        public static void Rebuild()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("请先退出播放模式。");
            Directory.CreateDirectory(Output);
            foreach(var id in SpaceCanvasBuiltinAssets.Ids)
            {
                var utility=Preview();
                try
                {
                    var item=SpaceCanvasAssetLibrary.CreateInstance(id,null,out _);
                    utility.AddSingleGO(item);
                    Render(utility,item,Output+id+".png",320,200);
                }
                finally { utility.Cleanup(); }
            }
            foreach(var name in new[]{"livingroom_demo","booth_demo","desk_demo"})
            {
                var utility=Preview();
                try
                {
                    var root=new GameObject("Template preview"); utility.AddSingleGO(root);
                    var project=ProjectSerializer.Deserialize(File.ReadAllText("Assets/StreamingAssets/ExampleProjects/"+name+".spacecanvas"));
                    var items=new Dictionary<string,GameObject>();
                    foreach(var data in project.objects)
                    {
                        var item=SpaceCanvasAssetLibrary.CreateInstance(data.assetId,root.transform,out _);
                        items.Add(data.id,item);
                    }
                    foreach(var data in project.objects)
                    {
                        if(!string.IsNullOrEmpty(data.parentId)) items[data.id].transform.SetParent(items[data.parentId].transform,false);
                        items[data.id].GetComponent<SpaceCanvasEditableObject>().Apply(data);
                    }
                    Render(utility,root,Output+name+".png",480,300);
                }
                finally { utility.Cleanup(); }
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(var path in Directory.GetFiles(Output,"*.png"))
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
                importer.maxTextureSize=512; importer.mipmapEnabled=true; importer.alphaIsTransparency=false;
                importer.isReadable=false; importer.textureCompression=TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            // Only the existing Full Space presentation owns the XRI raycaster setup.
            var builder=AppDomain.CurrentDomain.GetAssemblies().Select(x=>x.GetType("SpaceCanvas.ProjectIO.Editor.XR.SpaceCanvasFullSpaceBuilder")).FirstOrDefault(x=>x!=null);
            if(builder==null) throw new InvalidOperationException("未找到现有 Full Space MR 编辑器工具。");
            builder.GetMethod("ConfigurePresentation",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            builder.GetMethod("RenderPresentationPreview").Invoke(null,null);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Library/SpaceCanvas.RefineUI.txt","PASS\n36 asset previews + 3 template previews\nSaved Full Space MR UI; inspect Library/MRPreview\n");
            Debug.Log("SpaceCanvas：精致界面已保存，36 张素材预览、3 张空间模板预览已接入。");
        }

        private static PreviewRenderUtility Preview()
        {
            var preview=new PreviewRenderUtility();
            preview.camera.clearFlags=CameraClearFlags.SolidColor;
            preview.camera.backgroundColor=SpaceCanvasPresentationStyle.Paper;
            preview.camera.fieldOfView=32;
            preview.camera.nearClipPlane=.01f; preview.camera.farClipPlane=1000;
            preview.camera.allowHDR=false; preview.camera.allowMSAA=true;
            preview.lights[0].intensity=1.25f; preview.lights[0].transform.rotation=Quaternion.Euler(35,-35,0);
            preview.lights[1].intensity=.85f; preview.lights[1].transform.rotation=Quaternion.Euler(20,150,0);
            preview.ambientColor=new Color(.62f,.62f,.62f);
            return preview;
        }

        private static void Render(PreviewRenderUtility utility,GameObject root,string path,int width,int height)
        {
            var renderers=root.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length==0) throw new InvalidDataException(root.name+" 没有可预览网格。");
            var bounds=renderers[0].bounds; foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            // Procedural furniture faces -Z (screens and chair seats); show that
            // side while retaining the reviewed view of imported textured assets.
            var rotation=Quaternion.Euler(19,root.GetComponent<SpaceCanvasPremiumAssetTag>() != null ? 25 : 155,0);
            var tangent=Mathf.Tan(utility.camera.fieldOfView*.5f*Mathf.Deg2Rad);
            var distance=.1f;
            for(var i=0;i<8;i++)
            {
                var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                var view=Quaternion.Inverse(rotation)*(corner-bounds.center);
                distance=Mathf.Max(distance,Mathf.Abs(view.y)/tangent-view.z,Mathf.Abs(view.x)/(tangent*width/height)-view.z);
            }
            distance*=1.08f;
            utility.camera.transform.SetPositionAndRotation(bounds.center-rotation*Vector3.forward*distance,rotation);
            utility.BeginStaticPreview(new Rect(0,0,width,height));
            Texture2D image=null;
            try { utility.Render(true); image=utility.EndStaticPreview(); File.WriteAllBytes(path,image.EncodeToPNG()); }
            finally { if(image!=null) UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}
