using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Text = TMPro.TextMeshProUGUI;

namespace SpaceCanvas.ProjectIO
{
    public sealed partial class SpaceCanvasProjectIOUI
    {
        private SpaceCanvasProjectEntry pendingProjectVersion;
        private readonly Dictionary<string, Sprite> versionSprites = new Dictionary<string, Sprite>();
        private Image versionConfirmationImage;
        private Text versionConfirmationName, versionConfirmationId, versionPreviewStatus;

        private Sprite GetVersionSprite(SpaceCanvasProjectEntry entry)
        {
            if (entry == null || !entry.IsValid) return null;
            var key = entry.FilePath + ":" + entry.VersionId;
            if (versionSprites.TryGetValue(key, out var existing)) return existing;
            // 限制一次浏览占用的 GPU 内存；超出时仍能看到版本元数据并正常导入。
            if (versionSprites.Count >= 64) return null;
            var image = SpaceCanvasProjectThumbnail.Decode(entry.Project.preview);
            var sprite = image == null ? null : Sprite.Create(image, new Rect(0, 0, image.width, image.height), Vector2.one * .5f);
            versionSprites.Add(key, sprite);
            return sprite;
        }

        private void ReleaseVersionSprites()
        {
            foreach (var sprite in versionSprites.Values)
            {
                if (sprite == null) continue;
                var texture = sprite.texture;
                if (Application.isPlaying) { Destroy(sprite); if (texture != null) Destroy(texture); }
                else { DestroyImmediate(sprite); if (texture != null) DestroyImmediate(texture); }
            }
            versionSprites.Clear();
        }
        private void OnDestroy() { ReleaseVersionSprites(); }

        /// <summary>兼容已序列化的 MR 场景：启动时升级确认卡，不要求重建整个工作台。</summary>
        private void EnsureVersionConfirmationLayout()
        {
            if (confirmPanel == null || !fullSpaceLayout) return;
            var panel = (RectTransform)confirmPanel.transform;
            panel.sizeDelta = new Vector2(760, 480);
            var eyebrow = panel.Find("Confirm eyebrow") as RectTransform;
            if (eyebrow != null) eyebrow.anchoredPosition = new Vector2(0, 202);
            var heading = panel.Find("Confirm heading") as RectTransform;
            if (heading != null) heading.anchoredPosition = new Vector2(0, 161);
            if (confirmationText != null)
            {
                confirmationText.rectTransform.anchoredPosition = new Vector2(0, -102);
                confirmationText.rectTransform.sizeDelta = new Vector2(680, 86);
                confirmationText.fontSize = 18;
            }
            foreach (var button in panel.GetComponentsInChildren<Button>(true))
            {
                var rect = (RectTransform)button.transform;
                rect.anchoredPosition = new Vector2(button.name == "取消" ? -222 : button.name == "保存并导入" ? 0 : 222, -192);
                rect.sizeDelta = new Vector2(208, 52);
            }
            versionConfirmationImage = panel.Find("Version preview")?.GetComponent<Image>();
            if (versionConfirmationImage == null)
            {
                var go = new GameObject("Version preview", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(panel, false);
                var rect = (RectTransform)go.transform; rect.sizeDelta = new Vector2(240, 150); rect.anchoredPosition = new Vector2(-220, 39);
                versionConfirmationImage = go.GetComponent<Image>(); versionConfirmationImage.preserveAspect = true; versionConfirmationImage.raycastTarget = false;
            }
            versionConfirmationName = panel.Find("Version name")?.GetComponent<Text>() ??
                SpaceCanvasPresentationStyle.Label(panel, "Version name", "选择工程版本", new Vector2(126, 69), new Vector2(400, 62), 24);
            versionConfirmationName.overflowMode = TextOverflowModes.Ellipsis;
            versionConfirmationId = panel.Find("Version id")?.GetComponent<Text>() ??
                SpaceCanvasPresentationStyle.Label(panel, "Version id", "", new Vector2(126, 5), new Vector2(400, 44), 16, true);
            versionPreviewStatus = panel.Find("Version preview status")?.GetComponent<Text>() ??
                SpaceCanvasPresentationStyle.Label(panel, "Version preview status", "此版本暂无预览", new Vector2(-220, 39), new Vector2(230, 100), 16, true);
            versionPreviewStatus.alignment = TextAlignmentOptions.Center;
            versionPreviewStatus.raycastTarget = false;
        }

        private void ShowVersionConfirmation(string fileName, string version, Sprite preview)
        {
            EnsureVersionConfirmationLayout();
            if (versionConfirmationName != null) versionConfirmationName.text = fileName;
            if (versionConfirmationId != null) versionConfirmationId.text = version;
            if (versionConfirmationImage != null)
            { versionConfirmationImage.sprite = preview; versionConfirmationImage.color = preview == null ? new Color(.84f, .72f, .5f, .16f) : Color.white; }
            if (versionPreviewStatus != null) versionPreviewStatus.gameObject.SetActive(preview == null);
            if (!fullSpaceLayout && confirmationText != null) confirmationText.text = fileName + "\n" + version + "\n" + confirmationText.text;
        }
    }
}
