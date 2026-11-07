using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>36 个可替换的开放素材 ID；默认模型由 Unity 基础网格组成，无第三方模型依赖。</summary>
    public static class SpaceCanvasBuiltinAssets
    {
        public static readonly string[] Ids = {
            "primitive.cube", "primitive.sphere", "primitive.cylinder", "primitive.capsule", "primitive.plane",
            "furniture.sofa", "furniture.armchair", "furniture.table", "furniture.desk", "furniture.chair",
            "furniture.shelf", "furniture.tv", "furniture.lamp", "furniture.bed",
            "kitchen.counter", "kitchen.fridge", "kitchen.microwave", "kitchen.sink",
            "office.monitor", "office.keyboard", "office.mouse", "office.office_chair", "office.partition", "office.plant",
            "exhibit.counter", "exhibit.backwall", "exhibit.banner", "exhibit.spotlight", "exhibit.stool",
            "decor.plant", "decor.picture", "decor.rug", "decor.clock", "decor.book", "decor.vase", "decor.cabinet" };
        public static readonly string[] Names = {
            "立方体", "球体", "圆柱体", "胶囊体", "地板",
            "沙发", "单人沙发", "茶几", "书桌", "餐椅", "置物架", "电视", "落地灯", "床",
            "操作台", "冰箱", "微波炉", "水槽", "显示器", "键盘", "鼠标", "办公椅", "隔断", "办公绿植",
            "展台", "背墙", "易拉宝", "射灯", "展凳", "盆栽", "装饰画", "地毯", "时钟", "书本", "花瓶", "收纳柜" };

        public static string DisplayName(string id)
        {
            var index = System.Array.IndexOf(Ids, id);
            return index < 0 ? id : Names[index];
        }

        public static bool IsKnown(string id) { return System.Array.IndexOf(Ids, id) >= 0; }

        public static GameObject Create(string id, Transform parent, Material material = null)
        {
            // CreatePrimitive's default material belongs to the built-in pipeline.
            // Explicit Resources reference also makes the URP shader enter Android builds.
            if (material == null) material = SpaceCanvasAssetLibrary.DefaultMaterial;
            if (SpaceCanvasPremiumAssets.TryCreate(id, parent, material, out var premium)) return premium;
            var root = new GameObject(DisplayName(id));
            root.transform.SetParent(parent, false);
            if (id == "primitive.sphere") Part(root, PrimitiveType.Sphere, Vector3.zero, Vector3.one, material);
            else if (id == "primitive.cylinder") Part(root, PrimitiveType.Cylinder, Vector3.zero, new Vector3(1, .5f, 1), material);
            else if (id == "primitive.capsule") Part(root, PrimitiveType.Capsule, Vector3.zero, new Vector3(.5f, .5f, .5f), material);
            else if (id == "primitive.plane" || id == "decor.rug") Box(root, 0, 0, 0, 1, .025f, 1, material);
            else if (id.Contains("sofa") || id.Contains("armchair"))
            {
                var width = id.Contains("armchair") ? .85f : 2.1f;
                Box(root, 0, -.08f, 0, width, .3f, .8f, material);
                Box(root, 0, .28f, .32f, width, .55f, .16f, material);
                Box(root, -width / 2 + .08f, .1f, 0, .16f, .42f, .8f, material);
                Box(root, width / 2 - .08f, .1f, 0, .16f, .42f, .8f, material);
            }
            else if (id.Contains("chair") || id.Contains("stool"))
            {
                Box(root, 0, 0, 0, .55f, .12f, .55f, material);
                Legs(root, .46f, .4f, .46f, material);
                if (!id.Contains("stool")) Box(root, 0, .32f, .23f, .55f, .6f, .1f, material);
            }
            else if (id == "furniture.table" || id == "furniture.desk")
            {
                Box(root, 0, 0, 0, 1.3f, .1f, .75f, material);
                Legs(root, 1.1f, id.Contains("desk") ? .68f : .34f, .6f, material);
            }
            else if (id.Contains("plant"))
            {
                Part(root, PrimitiveType.Cylinder, new Vector3(0, -.3f, 0), new Vector3(.38f, .18f, .38f), material);
                Part(root, PrimitiveType.Sphere, new Vector3(0, .08f, 0), new Vector3(.6f, .8f, .6f), material);
            }
            else if (id == "furniture.lamp" || id == "exhibit.spotlight")
            {
                Part(root, PrimitiveType.Cylinder, new Vector3(0, -.65f, 0), new Vector3(.45f, .04f, .45f), material);
                Part(root, PrimitiveType.Cylinder, new Vector3(0, -.15f, 0), new Vector3(.05f, .5f, .05f), material);
                Part(root, PrimitiveType.Cylinder, new Vector3(0, .46f, 0), new Vector3(.5f, .15f, .5f), material);
            }
            else if (id == "office.monitor" || id == "furniture.tv")
            {
                Box(root, 0, .08f, 0, 1, .6f, .08f, material);
                Box(root, 0, -.3f, 0, .08f, .2f, .08f, material);
                Box(root, 0, -.4f, 0, .5f, .04f, .25f, material);
            }
            else if (id.Contains("shelf") || id.Contains("cabinet"))
            {
                Box(root, -.48f, 0, 0, .06f, 1.6f, .4f, material);
                Box(root, .48f, 0, 0, .06f, 1.6f, .4f, material);
                for (var i = 0; i < 4; i++) Box(root, 0, -.75f + i * .5f, 0, 1, .06f, .4f, material);
            }
            else if (id == "office.keyboard" || id == "decor.book") Box(root, 0, 0, 0, .45f, .04f, .2f, material);
            else if (id == "office.mouse") Part(root, PrimitiveType.Sphere, Vector3.zero, new Vector3(.1f, .06f, .15f), material);
            else if (id == "decor.vase") Part(root, PrimitiveType.Capsule, Vector3.zero, new Vector3(.25f, .25f, .25f), material);
            else if (id.Contains("picture") || id.Contains("banner") || id.Contains("backwall") || id.Contains("partition")) Box(root, 0, 0, 0, 1.2f, 1.5f, .08f, material);
            else if (id == "decor.clock") Part(root, PrimitiveType.Cylinder, Vector3.zero, new Vector3(.4f, .025f, .4f), material).transform.localRotation = Quaternion.Euler(90, 0, 0);
            else if (id == "furniture.bed") { Box(root, 0, 0, 0, 1.5f, .35f, 2, material); Box(root, 0, .25f, .95f, 1.5f, .8f, .1f, material); }
            else Box(root, 0, 0, 0, 1, 1, 1, material);
            return root;
        }

        private static void Legs(GameObject root, float width, float height, float depth, Material material)
        {
            for (var x = -1; x <= 1; x += 2)
                for (var z = -1; z <= 1; z += 2) Box(root, x * width / 2, -height / 2, z * depth / 2, .065f, height, .065f, material);
        }

        private static void Box(GameObject root, float x, float y, float z, float w, float h, float d, Material material)
        { Part(root, PrimitiveType.Cube, new Vector3(x, y, z), new Vector3(w, h, d), material); }

        private static GameObject Part(GameObject root, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }
    }
}
