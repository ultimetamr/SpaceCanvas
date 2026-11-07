using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// 保存时生成一次实际模型预览，不拍摄透视画面、用户或界面。
    /// 临时预览仅复制网格和材质引用，不复制交互脚本，不移动原物体，不改变原材质。
    /// PNG 内嵌于 JSON，随头显保存文件一起传输；预览不可用不会阻止保存或导入。
    /// </summary>
    public static class SpaceCanvasProjectThumbnail
    {
        public const int MaxPngBytes = 256 * 1024;
        public const int MaxDimension = 1024;
        public const int Width = 320, Height = 200;

        public static SpaceCanvasProjectPreview Capture(long captureTime)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            GameObject stage = null;
            Texture2D image = null;
            RenderTexture target = null;
            var previous = RenderTexture.active;
            var baked = new List<Mesh>();
            try
            {
                var renderers = new HashSet<Renderer>();
                foreach (var editable in SpaceCanvasRuntime.GetEditableObjects())
                    if (editable != null && editable.gameObject.activeInHierarchy)
                        foreach (var renderer in editable.GetComponentsInChildren<Renderer>())
                            if (renderer.enabled && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) renderers.Add(renderer);
                if (renderers.Count == 0 || renderers.Count > 512) return null;
                var bounds = default(Bounds);
                var first = true;
                foreach (var renderer in renderers)
                { if (first) { bounds = renderer.bounds; first = false; } else bounds.Encapsulate(renderer.bounds); }
                if (float.IsNaN(bounds.size.sqrMagnitude) || float.IsInfinity(bounds.size.sqrMagnitude)) return null;

                const int previewLayer = 31;
                var offset = new Vector3(10000, 10000, 10000);
                stage = new GameObject("SpaceCanvas temporary project thumbnail") { hideFlags = HideFlags.HideAndDontSave };
                stage.SetActive(false);
                var vertices = 0L;
                foreach (var renderer in renderers)
                {
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skin)
                    { mesh = new Mesh(); skin.BakeMesh(mesh); baked.Add(mesh); }
                    else mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    vertices += mesh.vertexCount;
                    if (vertices > 1000000) return null;
                    var part = new GameObject("Preview mesh") { layer = previewLayer, hideFlags = HideFlags.HideAndDontSave };
                    part.transform.SetParent(stage.transform, false);
                    part.transform.SetPositionAndRotation(renderer.transform.position - bounds.center + offset, renderer.transform.rotation);
                    part.transform.localScale = renderer.transform.lossyScale;
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var copy = part.AddComponent<MeshRenderer>();
                    copy.sharedMaterials = renderer.sharedMaterials;
                    copy.shadowCastingMode = ShadowCastingMode.Off;
                    copy.receiveShadows = false;
                }
                var cameraObject = new GameObject("Preview camera") { hideFlags = HideFlags.HideAndDontSave };
                cameraObject.transform.SetParent(stage.transform, false);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.cullingMask = 1 << previewLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.83f, .81f, .75f, 1);
                camera.orthographic = true;
                camera.aspect = (float)Width / Height;
                camera.orthographicSize = Mathf.Max(.05f, bounds.extents.magnitude) * 1.1f;
                var radius = camera.orthographicSize;
                var rotation = Quaternion.Euler(27, 155, 0);
                camera.transform.SetPositionAndRotation(offset - rotation * Vector3.forward * (radius * 3 + 1), rotation);
                camera.nearClipPlane = .01f;
                camera.farClipPlane = radius * 7 + 10;
                camera.allowHDR = false; camera.allowMSAA = false; camera.useOcclusionCulling = false;
                for (var i = 0; i < 2; i++)
                {
                    var lightObject = new GameObject("Preview light") { hideFlags = HideFlags.HideAndDontSave };
                    lightObject.transform.SetParent(stage.transform, false);
                    lightObject.transform.rotation = Quaternion.Euler(i == 0 ? 40 : 20, i == 0 ? -35 : 150, 0);
                    var light = lightObject.AddComponent<Light>();
                    light.type = LightType.Directional; light.intensity = i == 0 ? 1.2f : .65f;
                    light.cullingMask = 1 << previewLayer; light.shadows = LightShadows.None;
                }
                target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                stage.SetActive(true);
                camera.Render();
                RenderTexture.active = target;
                image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); image.Apply();
                var png = image.EncodeToPNG();
                if (png.Length > MaxPngBytes) return null;
                return new SpaceCanvasProjectPreview { captureTime = captureTime, pngBase64 = Convert.ToBase64String(png) };
            }
            catch (Exception ex)
            { Debug.LogWarning("SpaceCanvas：本次预览未生成，工程数据仍可保存。" + ex.Message); return null; }
            finally
            {
                RenderTexture.active = previous;
                // 同步拍摄已结束，立即释放临时相机，避免下一帧进入 XR 或物体注册流程。
                if (stage != null) Object.DestroyImmediate(stage);
                foreach (var mesh in baked) if (mesh != null) Object.DestroyImmediate(mesh);
                if (image != null) Object.DestroyImmediate(image);
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
        }

        /// <summary>解码前验证 PNG 签名、体积与 IHDR 尺寸；不让损坏预览拖垮工程读取。</summary>
        public static Texture2D Decode(SpaceCanvasProjectPreview preview)
        {
            if (preview == null || preview.encoding != "png-base64" || string.IsNullOrEmpty(preview.pngBase64)
                || preview.pngBase64.Length > (MaxPngBytes + 2) / 3 * 4) return null;
            Texture2D texture = null;
            try
            {
                var bytes = Convert.FromBase64String(preview.pngBase64);
                if (!HasSafePngHeader(bytes)) return null;
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (texture.LoadImage(bytes, true)) return texture;
            }
            catch (Exception) { /* 预览可选：格式错误仅显示无预览状态。 */ }
            if (texture != null) Object.DestroyImmediate(texture);
            return null;
        }

        public static bool HasSafePngHeader(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 33 || bytes.Length > MaxPngBytes) return false;
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (var i = 0; i < signature.Length; i++) if (bytes[i] != signature[i]) return false;
            if (bytes[8] != 0 || bytes[9] != 0 || bytes[10] != 0 || bytes[11] != 13
                || bytes[12] != 73 || bytes[13] != 72 || bytes[14] != 68 || bytes[15] != 82) return false;
            var w = ReadBigEndian(bytes, 16); var h = ReadBigEndian(bytes, 20);
            return w > 0 && h > 0 && w <= MaxDimension && h <= MaxDimension;
        }
        private static uint ReadBigEndian(byte[] bytes, int i) =>
            ((uint)bytes[i] << 24) | ((uint)bytes[i + 1] << 16) | ((uint)bytes[i + 2] << 8) | bytes[i + 3];
    }
}
