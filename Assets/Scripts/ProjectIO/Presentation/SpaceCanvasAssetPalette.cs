using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>Six assets per page: controllers and hands can reach all 36 assets without scrolling.</summary>
    public sealed class SpaceCanvasAssetPalette : MonoBehaviour
    {
        private static readonly string[] Labels = {"全部","基础","家居","厨房","办公","展会","装饰"};
        private static readonly string[] Prefixes = {"","primitive.","furniture.","kitchen.","office.","exhibit.","decor."};
        private Action<string> choose;
        [SerializeField] private int category = 2;
        private int page;
        private readonly List<int> matches = new List<int>();
        public void Bind(Action<string> onChoose) { choose = onChoose; Rebuild(); }
        private void Rebuild()
        {
            for (var i=transform.childCount-1;i>=0;i--)
            {
                var child = transform.GetChild(i).gameObject; child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            matches.Clear();
            for (var i=0;i<SpaceCanvasBuiltinAssets.Ids.Length;i++)
                if (SpaceCanvasBuiltinAssets.Ids[i].StartsWith(Prefixes[category],StringComparison.Ordinal)) matches.Add(i);
            var pages = Mathf.Max(1,Mathf.CeilToInt(matches.Count/6f)); page = Mathf.Clamp(page,0,pages-1);
            SpaceCanvasPresentationStyle.Label(transform,"Eyebrow","01  /  COLLECTION",new Vector2(0,333),new Vector2(364,24),14,true);
            var heading = SpaceCanvasPresentationStyle.Label(transform,"Title","素材库",new Vector2(0,297),new Vector2(364,42),30);
            heading.fontStyle = TMPro.FontStyles.Bold;
            var total = SpaceCanvasPresentationStyle.Label(transform,"Count","36",new Vector2(148,298),new Vector2(60,36),28,true);
            total.alignment = TMPro.TextAlignmentOptions.Right;
            SpaceCanvasPresentationStyle.Rule(transform,new Vector2(0,261),364);
            for (var i=0;i<Labels.Length;i++)
            {
                var selected = i;
                var button = SpaceCanvasPresentationStyle.Button(transform,"Category "+i,Labels[i],
                    new Vector2(-141 + i%4*94,222-i/4*46),new Vector2(86,38),()=>{category=selected;page=0;Rebuild();});
                button.GetComponentInChildren<TMPro.TextMeshProUGUI>().fontSize = 18;
                if (i==category) SpaceCanvasPresentationStyle.Tone(button,SpaceCanvasPresentationStyle.Accent,true);
            }
            for(var slot=0;slot<6;slot++)
            {
                var index = page*6+slot; if(index>=matches.Count) break;
                var assetIndex = matches[index]; var id=SpaceCanvasBuiltinAssets.Ids[assetIndex];
                var button = SpaceCanvasPresentationStyle.Button(transform,SpaceCanvasBuiltinAssets.Names[assetIndex],
                    SpaceCanvasBuiltinAssets.Names[assetIndex],new Vector2(slot%2==0?-95:95,76-slot/2*138),new Vector2(180,128),()=>choose?.Invoke(id));
                SpaceCanvasPresentationStyle.Tone(button,SpaceCanvasPresentationStyle.Paper,true);
                button.GetComponent<SpaceCanvasRoundedPanel>().radius = 14;
                var caption=button.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                caption.fontSize = 18; caption.alignment = TMPro.TextAlignmentOptions.Left;
                SpaceCanvasPresentationStyle.Place(caption.rectTransform,new Vector2(0,-43),new Vector2(150,30));
                var sprite = Resources.Load<Sprite>("SpaceCanvasPreviews/" + id);
                if (sprite != null)
                {
                    var image = new GameObject("Model preview",typeof(RectTransform),typeof(Image)); image.transform.SetParent(button.transform,false);
                    SpaceCanvasPresentationStyle.Place((RectTransform)image.transform,new Vector2(0,14),new Vector2(166,82));
                    var graphic=image.GetComponent<Image>(); graphic.sprite=sprite; graphic.preserveAspect=true; graphic.raycastTarget=false;
                }
                else
                {
                    var placeholder=SpaceCanvasPresentationStyle.Label(button.transform,"Preview fallback",(assetIndex+1).ToString("00"),new Vector2(0,14),new Vector2(150,74),36);
                    placeholder.color=SpaceCanvasPresentationStyle.DarkInk; placeholder.alignment=TMPro.TextAlignmentOptions.Center;
                }
            }
            SpaceCanvasPresentationStyle.Button(transform,"Previous","上一页",new Vector2(-126,-304),new Vector2(112,42),()=>{page--;Rebuild();}).interactable=page>0;
            SpaceCanvasPresentationStyle.Button(transform,"Next","下一页",new Vector2(126,-304),new Vector2(112,42),()=>{page++;Rebuild();}).interactable=page<pages-1;
            var count=SpaceCanvasPresentationStyle.Label(transform,"Page",(page+1)+" / "+pages,new Vector2(0,-304),new Vector2(96,42),18,true);
            count.alignment=TMPro.TextAlignmentOptions.Center;
            SpaceCanvasPresentationStyle.Label(transform,"Hint","轻点素材，开始构建你的空间",new Vector2(0,-347),new Vector2(364,24),16,true);
        }
    }
}
